namespace KeyForge.Core.Models;

public sealed class AppSettings
{
    public bool StartWithWindows { get; set; }

    public bool StartMinimized { get; set; }

    public bool ShowActiveProfileNotification { get; set; } = true;

    public ThemeMode Theme { get; set; } = ThemeMode.Dark;

    public string ThemePreset { get; set; } = "Obsidian Gold Red";

    public double BackgroundOpacity { get; set; } = 0.78;

    public double BackgroundBlur { get; set; } = 2;

    public double KeyboardScale { get; set; } = 0.88;

    public bool ShowCompactDiagnostics { get; set; } = true;

    public string EmergencyDisableHotkey { get; set; } = "Ctrl+Shift+F12";

    public bool IgnoreInjectedInput { get; set; } = true;

    public bool BlockOriginalKeyByDefault { get; set; } = true;

    public int MacroMinimumDelayMs { get; set; } = 20;

    public int ProfileSwitchPollingIntervalMs { get; set; } = 250;

    public bool WarnWhenAntiCheatMayBeActive { get; set; } = true;

    public bool DisableMacrosInOnlineGameProfiles { get; set; }

    public bool AutoCheckForUpdates { get; set; } = true;

    public DateTimeOffset? LastUpdateCheckUtc { get; set; }

    public AppSettings Clone() => new()
    {
        StartWithWindows = StartWithWindows,
        StartMinimized = StartMinimized,
        ShowActiveProfileNotification = ShowActiveProfileNotification,
        Theme = Theme,
        ThemePreset = ThemePreset,
        BackgroundOpacity = BackgroundOpacity,
        BackgroundBlur = BackgroundBlur,
        KeyboardScale = KeyboardScale,
        ShowCompactDiagnostics = ShowCompactDiagnostics,
        EmergencyDisableHotkey = EmergencyDisableHotkey,
        IgnoreInjectedInput = IgnoreInjectedInput,
        BlockOriginalKeyByDefault = BlockOriginalKeyByDefault,
        MacroMinimumDelayMs = MacroMinimumDelayMs,
        ProfileSwitchPollingIntervalMs = ProfileSwitchPollingIntervalMs,
        WarnWhenAntiCheatMayBeActive = WarnWhenAntiCheatMayBeActive,
        DisableMacrosInOnlineGameProfiles = DisableMacrosInOnlineGameProfiles,
        AutoCheckForUpdates = AutoCheckForUpdates,
        LastUpdateCheckUtc = LastUpdateCheckUtc
    };
}
