using KeyForge.Core.Models;
using KeyForge.Storage;
using Velopack;
using Velopack.Sources;

namespace KeyForge.App;

internal sealed class UpdateService
{
    private readonly AppSettings _settings;
    private readonly ISettingsRepository _settingsRepository;
    private UpdateManager? _updateManager;

    public UpdateService(AppSettings settings, ISettingsRepository settingsRepository)
    {
        _settings = settings;
        _settingsRepository = settingsRepository;
    }

    public UpdateInfo? PendingUpdate { get; private set; }

    public bool ShouldCheckAutomatically()
    {
        if (!_settings.AutoCheckForUpdates)
        {
            return false;
        }

        return _settings.LastUpdateCheckUtc is null ||
               DateTimeOffset.UtcNow - _settings.LastUpdateCheckUtc.Value >= DistributionInfo.AutoUpdateCheckInterval;
    }

    public async Task<UpdateCheckResult> CheckForUpdatesAsync(CancellationToken cancellationToken = default)
    {
        var managerResult = GetUpdateManager();
        if (managerResult.Status != UpdateCheckStatus.Ready || managerResult.Manager is null)
        {
            return managerResult.ToCheckResult();
        }

        var pendingRestart = managerResult.Manager.UpdatePendingRestart;
        if (pendingRestart is not null)
        {
            return UpdateCheckResult.UpdateReady(pendingRestart.Version?.ToString() ?? "new version");
        }

        await MarkUpdateCheckAttemptedAsync(cancellationToken);

        try
        {
            var update = await managerResult.Manager.CheckForUpdatesAsync();
            if (update is null)
            {
                PendingUpdate = null;
                return UpdateCheckResult.NoUpdate("KeyForge is up to date.");
            }

            PendingUpdate = update;
            var version = update.TargetFullRelease.Version?.ToString() ?? "new version";
            return UpdateCheckResult.UpdateAvailable(version, update);
        }
        catch (Exception ex)
        {
            AppLog.Error("Failed to check for updates.", ex);
            return UpdateCheckResult.Error("Update check failed. Try again later.");
        }
    }

    public async Task<UpdateCheckResult> DownloadUpdateAsync(
        UpdateInfo update,
        Action<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var managerResult = GetUpdateManager();
        if (managerResult.Status != UpdateCheckStatus.Ready || managerResult.Manager is null)
        {
            return managerResult.ToCheckResult();
        }

        try
        {
            await managerResult.Manager.DownloadUpdatesAsync(update, progress, cancellationToken);
            PendingUpdate = update;
            var pendingRestart = managerResult.Manager.UpdatePendingRestart ?? update.TargetFullRelease;
            return UpdateCheckResult.UpdateReady(pendingRestart.Version?.ToString() ?? "new version");
        }
        catch (Exception ex)
        {
            AppLog.Error("Failed to download update.", ex);
            return UpdateCheckResult.Error("Update download failed. Try again later.");
        }
    }

    public bool CanApplyDownloadedUpdate()
    {
        var managerResult = GetUpdateManager();
        return managerResult.Manager?.UpdatePendingRestart is not null;
    }

    public void ApplyDownloadedUpdateAndRestart()
    {
        var managerResult = GetUpdateManager();
        if (managerResult.Status != UpdateCheckStatus.Ready || managerResult.Manager is null)
        {
            throw new InvalidOperationException(managerResult.ToCheckResult().Message);
        }

        managerResult.Manager.ApplyUpdatesAndRestart(managerResult.Manager.UpdatePendingRestart);
    }

    private async Task MarkUpdateCheckAttemptedAsync(CancellationToken cancellationToken)
    {
        _settings.LastUpdateCheckUtc = DateTimeOffset.UtcNow;
        await _settingsRepository.SaveAsync(_settings, cancellationToken);
    }

    private UpdateManagerResult GetUpdateManager()
    {
        var repositoryUrl = DistributionInfo.ReleaseRepositoryUrl;
        if (!DistributionInfo.IsUsableRepositoryUrl(repositoryUrl))
        {
            return UpdateManagerResult.NoRepository();
        }

        try
        {
            _updateManager ??= new UpdateManager(
                new GithubSource(repositoryUrl, string.Empty, prerelease: false),
                new UpdateOptions { ExplicitChannel = DistributionInfo.ReleaseChannel });

            return _updateManager.IsInstalled
                ? UpdateManagerResult.Ready(_updateManager)
                : UpdateManagerResult.NotInstalled();
        }
        catch (Exception ex)
        {
            AppLog.Error("Failed to initialize update manager.", ex);
            return UpdateManagerResult.Error();
        }
    }
}

internal enum UpdateCheckStatus
{
    Ready,
    NoRepository,
    NotInstalled,
    NoUpdate,
    UpdateAvailable,
    UpdateReady,
    Error
}

internal sealed record UpdateCheckResult(
    UpdateCheckStatus Status,
    string Message,
    string? Version = null,
    UpdateInfo? Update = null)
{
    public static UpdateCheckResult NoRepository() =>
        new(UpdateCheckStatus.NoRepository, "Update feed is not configured.");

    public static UpdateCheckResult NotInstalled() =>
        new(UpdateCheckStatus.NotInstalled, "Install KeyForge with the official setup to enable updates.");

    public static UpdateCheckResult NoUpdate(string message) =>
        new(UpdateCheckStatus.NoUpdate, message);

    public static UpdateCheckResult UpdateAvailable(string version, UpdateInfo update) =>
        new(UpdateCheckStatus.UpdateAvailable, $"KeyForge {version} is available.", version, update);

    public static UpdateCheckResult UpdateReady(string version) =>
        new(UpdateCheckStatus.UpdateReady, $"KeyForge {version} is ready to install.", version);

    public static UpdateCheckResult Error(string message) =>
        new(UpdateCheckStatus.Error, message);
}

internal sealed record UpdateManagerResult(UpdateCheckStatus Status, UpdateManager? Manager)
{
    public static UpdateManagerResult Ready(UpdateManager manager) => new(UpdateCheckStatus.Ready, manager);

    public static UpdateManagerResult NoRepository() => new(UpdateCheckStatus.NoRepository, null);

    public static UpdateManagerResult NotInstalled() => new(UpdateCheckStatus.NotInstalled, null);

    public static UpdateManagerResult Error() => new(UpdateCheckStatus.Error, null);

    public UpdateCheckResult ToCheckResult() => Status switch
    {
        UpdateCheckStatus.NoRepository => UpdateCheckResult.NoRepository(),
        UpdateCheckStatus.NotInstalled => UpdateCheckResult.NotInstalled(),
        _ => UpdateCheckResult.Error("Updates are unavailable right now.")
    };
}
