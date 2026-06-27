using System.Text.Json;
using KeyForge.Core.Models;
using KeyForge.Core.Services;
using KeyForge.Core.Validation;
using KeyForge.Storage;

namespace KeyForge.Tests;

public sealed class ProfileAndStorageTests
{
    [Fact]
    public void DuplicateInputKeysAreInvalid()
    {
        var profile = new KeyForgeProfile
        {
            ProfileName = "Test",
            Target = new ProfileTarget { Exe = "notepad.exe" },
            Bindings =
            [
                new KeyBinding { Input = "LeftAlt", Type = BindingType.Simple, Output = [MacroStep.Press("B")] },
                new KeyBinding { Input = "LeftAlt", Type = BindingType.Simple, Output = [MacroStep.Press("C")] }
            ]
        };

        var result = new ProfileValidator().Validate(profile);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, issue => issue.Message.Contains("Multiple bindings", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void MacroDelayBoundsAreInvalid()
    {
        var profile = new KeyForgeProfile
        {
            ProfileName = "Test",
            Target = new ProfileTarget { Exe = "notepad.exe" },
            Bindings =
            [
                new KeyBinding
                {
                    Input = "F1",
                    Type = BindingType.Macro,
                    Output = [MacroStep.Wait(5)]
                }
            ]
        };

        var result = new ProfileValidator(new AppSettings { MacroMinimumDelayMs = 20 }).Validate(profile);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, issue => issue.Message.Contains("at least 20ms", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void MacroStepDelayBoundsApplyToTimedKeySteps()
    {
        var profile = new KeyForgeProfile
        {
            ProfileName = "Test",
            Target = new ProfileTarget { Exe = "notepad.exe" },
            Bindings =
            [
                new KeyBinding
                {
                    Input = "F1",
                    Type = BindingType.Macro,
                    Output =
                    [
                        new MacroStep
                        {
                            Action = MacroStepAction.Press,
                            Key = "A",
                            DelayMs = 5,
                            DelayPlacement = MacroStepDelayPlacement.Before
                        }
                    ]
                }
            ]
        };

        var result = new ProfileValidator(new AppSettings { MacroMinimumDelayMs = 20 }).Validate(profile);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, issue => issue.Message.Contains("at least 20ms", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ReplaceBindingKeepsOneBindingForInput()
    {
        var profile = new KeyForgeProfile();

        ProfileValidator.ReplaceBinding(profile, new KeyBinding
        {
            Input = "CapsLock",
            Type = BindingType.Simple,
            Output = [MacroStep.Press("Escape")]
        });
        ProfileValidator.ReplaceBinding(profile, new KeyBinding
        {
            Input = "CapsLock",
            Type = BindingType.Simple,
            Output = [MacroStep.Press("Space")]
        });

        Assert.Single(profile.Bindings);
        Assert.Equal("Space", profile.Bindings[0].Output[0].Key);
    }

    [Fact]
    public void ProfileMatcherSelectsAutoProfileForForegroundExe()
    {
        var skyrim = new KeyForgeProfile
        {
            ProfileName = "Skyrim",
            Target = new ProfileTarget { Exe = "SkyrimSE.exe" },
            Mode = ProfileMode.Auto
        };

        var active = new ActiveWindowInfo
        {
            ProcessName = "SkyrimSE",
            ExecutableName = "skyrimse.exe",
            WindowTitle = "Skyrim Special Edition"
        };

        var selected = ProfileMatcher.SelectActiveProfile([skyrim], active);

        Assert.Same(skyrim, selected);
    }

    [Fact]
    public void ForegroundAutoProfileWinsOverAlwaysOnProfile()
    {
        var auto = new KeyForgeProfile
        {
            ProfileName = "Auto",
            Target = new ProfileTarget { Exe = "notepad.exe" },
            Mode = ProfileMode.Auto
        };
        var alwaysOn = new KeyForgeProfile
        {
            ProfileName = "Manual",
            Mode = ProfileMode.AlwaysOn
        };

        var selected = ProfileMatcher.SelectActiveProfile(
            [auto, alwaysOn],
            new ActiveWindowInfo { ExecutableName = "notepad.exe", ProcessName = "notepad" });

        Assert.Same(auto, selected);
    }

    [Fact]
    public void ProfileMatcherReturnsForegroundProfilesThenAlwaysOnOverlays()
    {
        var overlay = new KeyForgeProfile
        {
            ProfileName = "Overlay",
            Mode = ProfileMode.AlwaysOn,
            Bindings = [new KeyBinding { Input = "F1", Type = BindingType.Simple, Output = [MacroStep.Press("A")] }]
        };
        var exeOnly = new KeyForgeProfile
        {
            ProfileName = "Exe Only",
            Target = new ProfileTarget { Exe = "DD2.exe" },
            Mode = ProfileMode.Auto,
            Bindings = [new KeyBinding { Input = "F1", Type = BindingType.Simple, Output = [MacroStep.Press("B")] }]
        };
        var titleSpecific = new KeyForgeProfile
        {
            ProfileName = "Title Specific",
            Target = new ProfileTarget { Exe = "DD2.exe", WindowTitleContains = "Dragon" },
            Mode = ProfileMode.Auto,
            Bindings = [new KeyBinding { Input = "F1", Type = BindingType.Simple, Output = [MacroStep.Press("C")] }]
        };

        var stack = ProfileMatcher.SelectActiveProfiles(
            [overlay, exeOnly, titleSpecific],
            new ActiveWindowInfo
            {
                ExecutableName = "DD2.exe",
                ProcessName = "DD2",
                WindowTitle = "Dragon's Dogma 2"
            });

        Assert.Equal(["Title Specific", "Exe Only", "Overlay"], stack.Select(profile => profile.ProfileName));
    }

    [Fact]
    public async Task JsonProfileRepositoryRoundTripsProfile()
    {
        var root = Path.Combine(Path.GetTempPath(), "KeyForge.Tests", Guid.NewGuid().ToString("n"));
        var repository = new JsonProfileRepository(root);
        var profile = new KeyForgeProfile
        {
            ProfileId = "round-trip",
            ProfileName = "Round Trip",
            Target = new ProfileTarget { Exe = "notepad.exe", ExecutablePath = @"C:\Windows\System32\notepad.exe" },
            ArtworkPath = @"C:\Images\round-trip.png",
            IconPath = @"C:\Images\round-trip-icon.png",
            Bindings =
            [
                new KeyBinding
                {
                    Input = "LeftAlt",
                    Type = BindingType.Combo,
                    Output = [MacroStep.KeyDown("LeftCtrl"), MacroStep.Press("B"), MacroStep.KeyUp("LeftCtrl")]
                }
            ]
        };

        await repository.SaveAsync(profile);
        var loaded = await repository.LoadAllAsync();
        var json = await File.ReadAllTextAsync(Path.Combine(root, "profiles", "round-trip.json"));

        Assert.Single(loaded);
        Assert.Equal("Round Trip", loaded[0].ProfileName);
        Assert.Equal(@"C:\Windows\System32\notepad.exe", loaded[0].Target.ExecutablePath);
        Assert.Equal(@"C:\Images\round-trip.png", loaded[0].ArtworkPath);
        Assert.Equal(@"C:\Images\round-trip-icon.png", loaded[0].IconPath);
        Assert.Equal(BindingType.Combo, loaded[0].Bindings[0].Type);
        Assert.Contains("\"profileId\"", json);
        Assert.Contains("\"combo\"", json);

        Directory.Delete(root, recursive: true);
    }

    [Fact]
    public void JsonStorageOptionsUseCamelCaseEnumValues()
    {
        var binding = new KeyBinding
        {
            Input = "F1",
            Type = BindingType.Macro,
            Output =
            [
                new MacroStep
                {
                    Action = MacroStepAction.KeyDown,
                    Key = "LeftCtrl",
                    DelayMs = 50,
                    DelayPlacement = MacroStepDelayPlacement.Before
                }
            ]
        };

        var json = JsonSerializer.Serialize(binding, JsonStorageOptions.Default);

        Assert.Contains("\"type\": \"macro\"", json);
        Assert.Contains("\"action\": \"keyDown\"", json);
        Assert.Contains("\"delayPlacement\": \"before\"", json);
    }

    [Fact]
    public void OldProfileJsonLoadsWithVisualDefaults()
    {
        const string json = """
        {
          "version": "0.1.0",
          "profileId": "old",
          "profileName": "Old Profile",
          "target": { "exe": "notepad.exe", "windowTitleContains": null },
          "mode": "auto",
          "enabled": true,
          "bindings": []
        }
        """;

        var profile = JsonSerializer.Deserialize<KeyForgeProfile>(json, JsonStorageOptions.Default);

        Assert.NotNull(profile);
        Assert.Equal("Old Profile", profile.ProfileName);
        Assert.Null(profile.ArtworkPath);
        Assert.Null(profile.AccentHint);
        Assert.Null(profile.IconPath);
        Assert.Null(profile.Target.ExecutablePath);
    }

    [Fact]
    public void SettingsJsonRoundTripsVisualPreferences()
    {
        var settings = new AppSettings
        {
            ThemePreset = "Custom",
            CustomThemeBackground = "#101820",
            CustomThemePanel = "#203040",
            CustomThemeAccent = "#AABBCC",
            CustomThemeAccentAlt = "#CCDDEE",
            CustomThemeKeyboardKey = "#334455",
            CustomThemeMappedKey = "#556677",
            BackgroundOpacity = 0.44,
            BackgroundBlur = 12,
            ShowCompactDiagnostics = false,
            CreateDesktopShortcut = false,
            AutoCheckForUpdates = false,
            LastUpdateCheckUtc = DateTimeOffset.Parse("2026-06-26T18:00:00Z")
        };

        var json = JsonSerializer.Serialize(settings, JsonStorageOptions.Default);
        var roundTrip = JsonSerializer.Deserialize<AppSettings>(json, JsonStorageOptions.Default);

        Assert.NotNull(roundTrip);
        Assert.Equal("Custom", roundTrip.ThemePreset);
        Assert.Equal("#101820", roundTrip.CustomThemeBackground);
        Assert.Equal("#203040", roundTrip.CustomThemePanel);
        Assert.Equal("#AABBCC", roundTrip.CustomThemeAccent);
        Assert.Equal("#CCDDEE", roundTrip.CustomThemeAccentAlt);
        Assert.Equal("#334455", roundTrip.CustomThemeKeyboardKey);
        Assert.Equal("#556677", roundTrip.CustomThemeMappedKey);
        Assert.Equal(0.44, roundTrip.BackgroundOpacity);
        Assert.Equal(12, roundTrip.BackgroundBlur);
        Assert.False(roundTrip.ShowCompactDiagnostics);
        Assert.False(roundTrip.CreateDesktopShortcut);
        Assert.False(roundTrip.AutoCheckForUpdates);
        Assert.Equal(settings.LastUpdateCheckUtc, roundTrip.LastUpdateCheckUtc);
    }
}
