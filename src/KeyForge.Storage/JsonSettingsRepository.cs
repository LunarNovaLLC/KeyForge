using System.Text.Json;
using KeyForge.Core.Models;

namespace KeyForge.Storage;

public sealed class JsonSettingsRepository : ISettingsRepository
{
    private readonly string _settingsPath;

    public JsonSettingsRepository(string? rootDirectory = null)
    {
        var root = rootDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "KeyForge");
        _settingsPath = Path.Combine(root, "settings.json");
    }

    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_settingsPath))
        {
            return new AppSettings();
        }

        await using var stream = File.OpenRead(_settingsPath);
        return await JsonSerializer.DeserializeAsync<AppSettings>(
            stream,
            JsonStorageOptions.Default,
            cancellationToken) ?? new AppSettings();
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(_settingsPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = $"{_settingsPath}.{Guid.NewGuid():n}.tmp";
        await using (var stream = File.Create(tempPath))
        {
            await JsonSerializer.SerializeAsync(stream, settings, JsonStorageOptions.Default, cancellationToken);
        }

        if (File.Exists(_settingsPath))
        {
            File.Delete(_settingsPath);
        }

        File.Move(tempPath, _settingsPath);
    }
}
