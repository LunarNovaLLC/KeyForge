namespace KeyForge.Core.Models;

public sealed class AppSettings
{
    public bool StartWithWindows { get; set; }

    public bool StartMinimized { get; set; }

    public bool CreateDesktopShortcut { get; set; } = true;

    public string ThemePreset { get; set; } = "Obsidian";

    public string CustomThemeBackground { get; set; } = "#07080D";

    public string CustomThemePanel { get; set; } = "#141821";

    public string CustomThemeAccent { get; set; } = "#F2C230";

    public string CustomThemeAccentAlt { get; set; } = "#E6483E";

    public string CustomThemeKeyboardKey { get; set; } = "#1B202C";

    public string CustomThemeMappedKey { get; set; } = "#382019";

    public double BackgroundOpacity { get; set; } = 0.78;

    public double BackgroundBlur { get; set; } = 2;

    public bool ShowCompactDiagnostics { get; set; } = true;

    public string EmergencyDisableHotkey { get; set; } = "Ctrl+Shift+F12";

    public bool IgnoreInjectedInput { get; set; } = true;

    public bool BlockOriginalKeyByDefault { get; set; } = true;

    public int MacroMinimumDelayMs { get; set; } = 20;

    public int ProfileSwitchPollingIntervalMs { get; set; } = 250;

    public bool WarnWhenAntiCheatMayBeActive { get; set; } = true;

    public bool AutoCheckForUpdates { get; set; } = true;

    public DateTimeOffset? LastUpdateCheckUtc { get; set; }

    public AppSettings Clone() => new()
    {
        StartWithWindows = StartWithWindows,
        StartMinimized = StartMinimized,
        CreateDesktopShortcut = CreateDesktopShortcut,
        ThemePreset = ThemePreset,
        CustomThemeBackground = CustomThemeBackground,
        CustomThemePanel = CustomThemePanel,
        CustomThemeAccent = CustomThemeAccent,
        CustomThemeAccentAlt = CustomThemeAccentAlt,
        CustomThemeKeyboardKey = CustomThemeKeyboardKey,
        CustomThemeMappedKey = CustomThemeMappedKey,
        BackgroundOpacity = BackgroundOpacity,
        BackgroundBlur = BackgroundBlur,
        ShowCompactDiagnostics = ShowCompactDiagnostics,
        EmergencyDisableHotkey = EmergencyDisableHotkey,
        IgnoreInjectedInput = IgnoreInjectedInput,
        BlockOriginalKeyByDefault = BlockOriginalKeyByDefault,
        MacroMinimumDelayMs = MacroMinimumDelayMs,
        ProfileSwitchPollingIntervalMs = ProfileSwitchPollingIntervalMs,
        WarnWhenAntiCheatMayBeActive = WarnWhenAntiCheatMayBeActive,
        AutoCheckForUpdates = AutoCheckForUpdates,
        LastUpdateCheckUtc = LastUpdateCheckUtc
    };
}
