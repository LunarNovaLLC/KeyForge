using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Security.Principal;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using KeyForge.Core.Keyboard;
using KeyForge.Core.Models;
using KeyForge.Core.Services;
using KeyForge.Core.Validation;
using KeyForge.Input;
using KeyForge.Process;
using KeyForge.Storage;
using Microsoft.Win32;
using Forms = System.Windows.Forms;
using CoreKeyBinding = KeyForge.Core.Models.KeyBinding;
using CoreThemeMode = KeyForge.Core.Models.ThemeMode;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;
using WpfApplication = System.Windows.Application;
using WpfBrush = System.Windows.Media.Brush;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfButton = System.Windows.Controls.Button;
using WpfColor = System.Windows.Media.Color;
using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;
using WpfMessageBox = System.Windows.MessageBox;
using WpfTextBox = System.Windows.Controls.TextBox;

namespace KeyForge.App;

public partial class MainWindow : Window
{
    private readonly ObservableCollection<KeyForgeProfile> _profiles = [];
    private readonly ObservableCollection<KeyForgeProfile> _filteredProfiles = [];
    private readonly ObservableCollection<MacroStep> _macroSteps = [];
    private readonly Dictionary<string, WpfButton> _keyboardButtons = new(StringComparer.OrdinalIgnoreCase);
    private readonly IProfileRepository _profileRepository = new JsonProfileRepository();
    private readonly ISettingsRepository _settingsRepository = new JsonSettingsRepository();
    private readonly IForegroundAppDetector _foregroundAppDetector = new Win32ForegroundAppDetector();
    private readonly DispatcherTimer _activeWindowTimer = new();
    private readonly ObservableCollection<string> _diagnosticMessages = [];
    private readonly Dictionary<string, ProfileDetailsWindow> _profileWindows = new(StringComparer.OrdinalIgnoreCase);
    private KeyEditorPopupWindow? _keyEditorWindow;
    private readonly string[] _themePresets =
    [
        "Obsidian Gold Red",
        "Electric Blue",
        "Violet Neon",
        "High Contrast"
    ];

    private AppSettings _settings = new();
    private LowLevelKeyboardHookService? _keyboardHook;
    private SendInputSender? _inputSender;
    private MacroRunner? _macroRunner;
    private RemappingEngine? _remappingEngine;
    private Forms.NotifyIcon? _notifyIcon;
    private string? _lastTrayMenuSignature;
    private KeyForgeProfile? _selectedProfile;
    private CoreKeyBinding? _editingBinding;
    private string? _selectedKey;
    private CaptureMode _captureMode;
    private bool _isLoadingUi;
    private bool _reallyExit;
    private string? _lastForegroundSignature;
    private string? _lastActiveProfileId;
    private IReadOnlyList<KeyForgeProfile> _activeProfiles = [];
    private ActiveWindowInfo _lastActiveWindow = ActiveWindowInfo.Empty;

    public MainWindow()
    {
        _isLoadingUi = true;
        InitializeComponent();
        _isLoadingUi = false;
        VersionText.Text = $"v{GetDisplayVersion()}";

        ProfileListBox.ItemsSource = _filteredProfiles;
        ModeComboBox.ItemsSource = Enum.GetValues<ProfileMode>();
        BindingTypeComboBox.ItemsSource = Enum.GetValues<BindingType>();
        MacroActionComboBox.ItemsSource = Enum.GetValues<MacroStepAction>();
        ThemeComboBox.ItemsSource = _themePresets;
        MacroKeyComboBox.ItemsSource = KeyCatalog.All.OrderBy(key => key.Label).ToList();
        MacroStepsListBox.ItemsSource = _macroSteps;
        DiagnosticsLogListBox.ItemsSource = _diagnosticMessages;
        DiagnosticsProfilePathText.Text = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "KeyForge",
            "profiles");
        DiagnosticsBackgroundPathText.Text = ProfileAssetService.BackgroundStorageDirectory;
        DiagnosticsLogPathText.Text = Path.Combine(AppLog.LogDirectory, "keyforge.log");

        BuildKeyboard();
        SetEditorEnabled(false);

        Loaded += MainWindow_Loaded;
        _activeWindowTimer.Tick += ActiveWindowTimer_Tick;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        _settings = await _settingsRepository.LoadAsync();
        if (NormalizeBackgroundDefaults())
        {
            await _settingsRepository.SaveAsync(_settings);
        }

        ApplyThemePreset(_settings.ThemePreset);
        ApplySettingsToUi();

        var profiles = await _profileRepository.LoadAllAsync();
        foreach (var profile in profiles)
        {
            _profiles.Add(profile);
        }

        ApplyProfileFilter();
        ProfileListBox.SelectedItem = _filteredProfiles.FirstOrDefault();

        InitializeRemappingEngine();
        InitializeTray();
        UpdateActiveWindowTimer();
        _activeWindowTimer.Start();
        UpdateGlobalStatus("Running. Ctrl+Shift+F12 disables all remapping.");
        ApplyKeyboardScale();
        UpdateProfileArtwork();
        RefreshDiagnosticsPanel();

        if (_settings.StartMinimized || App.StartMinimizedRequested)
        {
            _ = Dispatcher.BeginInvoke(new Action(Hide), DispatcherPriority.ApplicationIdle);
        }
    }

    private void InitializeRemappingEngine()
    {
        _keyboardHook = new LowLevelKeyboardHookService();
        _inputSender = new SendInputSender();
        _macroRunner = new MacroRunner(_inputSender, _settings);
        _remappingEngine = new RemappingEngine(_keyboardHook, _inputSender, _macroRunner, _settings);
        _remappingEngine.StatusChanged += (_, status) =>
            Dispatcher.BeginInvoke(() =>
            {
                if (status.Contains("error", StringComparison.OrdinalIgnoreCase))
                {
                    AppLog.Error(status);
                }

                UpdateGlobalStatus(status);
            });

        try
        {
            _remappingEngine.Start();
        }
        catch (Exception ex)
        {
            UpdateGlobalStatus($"Input hook unavailable: {ex.Message}");
            WpfMessageBox.Show(
                this,
                $"KeyForge opened, but the global keyboard hook could not start.\n\n{ex.Message}",
                "Input Hook",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void BuildKeyboard()
    {
        KeyboardHost.Children.Clear();
        _keyboardButtons.Clear();
        var scale = Math.Clamp(_settings.KeyboardScale, 0.72, 1.08);
        var keyWidth = 48 * scale;
        var keyHeight = 42 * scale;
        var gap = 5 * scale;
        var rowGap = 7 * scale;
        var xStep = keyWidth + gap;
        var yStep = keyHeight + rowGap;
        var canvas = new Canvas
        {
            SnapsToDevicePixels = true
        };
        double maxRight = 0;
        double maxBottom = 0;

        foreach (var key in KeyboardLayout.VisualKeys)
        {
            var width = key.Width * keyWidth + Math.Max(0, key.Width - 1) * gap;
            var height = key.Height * keyHeight + Math.Max(0, key.Height - 1) * rowGap;
            var left = key.X * xStep;
            var top = key.Y * yStep;
            var button = new WpfButton
            {
                Width = width,
                Height = height,
                Tag = key.Code,
                FontWeight = FontWeights.SemiBold,
                ToolTip = key.Code,
                Padding = new Thickness(2)
            };
            button.Click += KeyboardButton_Click;
            Canvas.SetLeft(button, left);
            Canvas.SetTop(button, top);
            canvas.Children.Add(button);
            _keyboardButtons[key.Code] = button;
            maxRight = Math.Max(maxRight, left + width);
            maxBottom = Math.Max(maxBottom, top + height);
        }

        canvas.Width = maxRight;
        canvas.Height = maxBottom;
        KeyboardHost.Width = maxRight;
        KeyboardHost.Height = maxBottom;
        KeyboardHost.Children.Add(canvas);
        UpdateKeyboardVisuals();
    }

    private void KeyboardButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not WpfButton { Tag: string key })
        {
            return;
        }

        if (_selectedProfile is null && _activeProfiles.FirstOrDefault() is { } activeProfile)
        {
            _selectedProfile = activeProfile;
            ProfileListBox.SelectedItem = activeProfile;
        }

        _selectedKey = key;
        LoadSelectedBinding();
        UpdateKeyboardVisuals();
        if (_selectedProfile is not null)
        {
            OpenKeyEditor(_selectedProfile, key, sender as WpfButton);
        }
    }

    private void ProfileListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _selectedProfile = ProfileListBox.SelectedItem as KeyForgeProfile;
        _selectedKey = null;
        RefreshProfileFields();
        LoadSelectedBinding();
        UpdateKeyboardVisuals();
    }

    private void ProfileListBoxItem_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (FindVisualParent<System.Windows.Controls.CheckBox>(e.OriginalSource as DependencyObject) is not null)
        {
            return;
        }

        if ((sender as FrameworkElement)?.DataContext is not KeyForgeProfile profile)
        {
            return;
        }

        _selectedProfile = profile;
        ProfileListBox.SelectedItem = profile;
        RefreshProfileFields();
        LoadSelectedBinding();
        UpdateKeyboardVisuals();
        OpenProfileDetails(profile, _selectedKey);
    }

    private void ProfileSearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        ApplyProfileFilter();
    }

    private async void NewProfileButton_Click(object sender, RoutedEventArgs e)
    {
        var profile = new KeyForgeProfile
        {
            ProfileId = CreateUniqueProfileId("new-profile"),
            ProfileName = "New Profile",
            Mode = ProfileMode.Auto,
            Enabled = true
        };

        _profiles.Add(profile);
        await _profileRepository.SaveAsync(profile);
        ApplyProfileFilter();
        ProfileListBox.SelectedItem = profile;
        UpdateTrayMenu();
        OpenProfileDetails(profile, null);
    }

    private async void SelectExeButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Applications (*.exe)|*.exe|All files (*.*)|*.*",
            Title = "Select target executable"
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        var exeName = System.IO.Path.GetFileName(dialog.FileName);
        var profile = _selectedProfile;
        if (profile is null)
        {
            profile = new KeyForgeProfile
            {
                ProfileId = CreateUniqueProfileId(System.IO.Path.GetFileNameWithoutExtension(exeName)),
                ProfileName = System.IO.Path.GetFileNameWithoutExtension(exeName),
                Mode = ProfileMode.Auto
            };
            _profiles.Add(profile);
        }

        profile.Target.Exe = exeName;
        profile.Target.ExecutablePath = dialog.FileName;
        profile.Target.WindowTitleContains = null;
        profile.IconPath = ProfileAssetService.TryExtractIcon(profile, dialog.FileName) ?? profile.IconPath;
        if (profile.ProfileName is "New Profile" or "")
        {
            profile.ProfileName = System.IO.Path.GetFileNameWithoutExtension(exeName);
        }

        await _profileRepository.SaveAsync(profile);
        ApplyProfileFilter();
        ProfileListBox.SelectedItem = profile;
        RefreshProfileFields();
        UpdateTrayMenu();
    }

    private async void SelectRunningWindowButton_Click(object sender, RoutedEventArgs e)
    {
        var windows = _foregroundAppDetector.GetOpenWindows();
        if (windows.Count == 0)
        {
            WpfMessageBox.Show(
                this,
                "No visible running windows were found. Open the game or app first, then try again.",
                "Select Running Window",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var picker = new RunningWindowPickerWindow(windows)
        {
            Owner = this
        };

        if (picker.ShowDialog() != true || picker.SelectedWindow is null)
        {
            return;
        }

        var selectedWindow = picker.SelectedWindow;
        var exeName = selectedWindow.ExecutableName;
        if (string.IsNullOrWhiteSpace(exeName) && !string.IsNullOrWhiteSpace(selectedWindow.ProcessName))
        {
            exeName = selectedWindow.ProcessName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                ? selectedWindow.ProcessName
                : $"{selectedWindow.ProcessName}.exe";
        }

        if (string.IsNullOrWhiteSpace(exeName))
        {
            WpfMessageBox.Show(
                this,
                "KeyForge could not identify the executable for that window.",
                "Select Running Window",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        var profile = _selectedProfile;
        if (profile is null)
        {
            profile = new KeyForgeProfile
            {
                ProfileId = CreateUniqueProfileId(Path.GetFileNameWithoutExtension(exeName)),
                ProfileName = Path.GetFileNameWithoutExtension(exeName),
                Mode = ProfileMode.Auto,
                Enabled = true
            };
            _profiles.Add(profile);
        }

        profile.Target.Exe = exeName;
        profile.Target.ExecutablePath = selectedWindow.ExecutablePath;
        profile.Target.WindowTitleContains = picker.MatchWindowTitle ? selectedWindow.WindowTitle : null;
        profile.IconPath = ProfileAssetService.TryExtractIcon(profile, selectedWindow.ExecutablePath) ?? profile.IconPath;
        if (profile.ProfileName is "New Profile" or "")
        {
            profile.ProfileName = Path.GetFileNameWithoutExtension(exeName);
        }

        await _profileRepository.SaveAsync(profile);
        ApplyProfileFilter(keepSelection: profile);
        ProfileListBox.SelectedItem = profile;
        RefreshProfileFields();
        UpdateProfileArtwork();
        UpdateTrayMenu();
    }

    private async void ChooseArtworkButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedProfile is null)
        {
            WpfMessageBox.Show(this, "Select a profile before choosing artwork.", "Profile Artwork", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new OpenFileDialog
        {
            Title = "Choose profile artwork",
            Filter = "Images (*.png;*.jpg;*.jpeg;*.bmp;*.webp)|*.png;*.jpg;*.jpeg;*.bmp;*.webp|All files (*.*)|*.*"
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        _selectedProfile.ArtworkPath = ProfileAssetService.CopyArtwork(_selectedProfile, dialog.FileName);
        _selectedProfile.ModifiedAt = DateTimeOffset.UtcNow;
        await SaveSelectedProfileAsync();
        RefreshProfileFields();
        UpdateProfileArtwork();
        UpdateGlobalStatus($"Background image updated for {_selectedProfile.ProfileName}.");
        if (_profileWindows.TryGetValue(_selectedProfile.ProfileId, out var openProfileWindow))
        {
            openProfileWindow.RefreshProfile();
        }
    }

    private async void ImportProfileButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "KeyForge profiles (*.json)|*.json|All files (*.*)|*.*",
            Title = "Import KeyForge profile"
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            var profile = await _profileRepository.ImportAsync(dialog.FileName);
            ReplaceProfileInCollection(profile);
            ApplyProfileFilter();
            ProfileListBox.SelectedItem = profile;
            UpdateTrayMenu();
        }
        catch (Exception ex)
        {
            WpfMessageBox.Show(this, ex.Message, "Import Failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void ExportProfileButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedProfile is null)
        {
            return;
        }

        var dialog = new SaveFileDialog
        {
            Filter = "KeyForge profiles (*.json)|*.json|All files (*.*)|*.*",
            FileName = $"{_selectedProfile.ProfileName}.json",
            Title = "Export KeyForge profile"
        };

        if (dialog.ShowDialog(this) == true)
        {
            await _profileRepository.ExportAsync(_selectedProfile, dialog.FileName);
        }
    }

    private async void DeleteProfileButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedProfile is null)
        {
            return;
        }

        var answer = WpfMessageBox.Show(
            this,
            $"Delete profile '{_selectedProfile.ProfileName}'?",
            "Delete Profile",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (answer != MessageBoxResult.Yes)
        {
            return;
        }

        var profile = _selectedProfile;
        if (_profileWindows.Remove(profile.ProfileId, out var detailsWindow))
        {
            detailsWindow.Close();
        }

        await _profileRepository.DeleteAsync(profile.ProfileId);
        _profiles.Remove(profile);
        _selectedProfile = null;
        ApplyProfileFilter();
        ProfileListBox.SelectedItem = _filteredProfiles.FirstOrDefault();
        _remappingEngine?.SetActiveProfile(null);
        UpdateTrayMenu();
    }

    private async void ProfileField_Changed(object sender, EventArgs e)
    {
        if (_isLoadingUi || _selectedProfile is null)
        {
            return;
        }

        var shouldRefreshProfileList = sender is not WpfTextBox;

        _selectedProfile.ProfileName = ProfileNameTextBox.Text.Trim();
        _selectedProfile.Target.Exe = TargetExeTextBox.Text.Trim();
        _selectedProfile.Target.WindowTitleContains = NullIfWhiteSpace(WindowTitleTextBox.Text);
        _selectedProfile.Mode = ModeComboBox.SelectedItem is ProfileMode mode ? mode : ProfileMode.Auto;
        _selectedProfile.Enabled = ProfileEnabledCheckBox.IsChecked == true;
        _selectedProfile.ModifiedAt = DateTimeOffset.UtcNow;

        await SaveSelectedProfileAsync();
        if (shouldRefreshProfileList)
        {
            ApplyProfileFilter(keepSelection: _selectedProfile);
        }

        UpdateKeyboardVisuals();
        UpdateTrayMenu();
    }

    private void BindingTypeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoadingUi || _editingBinding is null)
        {
            return;
        }

        _editingBinding.Type = BindingTypeComboBox.SelectedItem is BindingType type ? type : BindingType.Simple;
        if (_editingBinding.Type == BindingType.Disabled)
        {
            _editingBinding.Output.Clear();
            _macroSteps.Clear();
        }
        else if (_editingBinding.Type == BindingType.Macro)
        {
            _editingBinding.Output = _macroSteps.Select(step => step.Clone()).ToList();
        }

        UpdateBindingPreview();
    }

    private void CaptureSimpleButton_Click(object sender, RoutedEventArgs e)
    {
        if (!CanEditBinding())
        {
            return;
        }

        _captureMode = CaptureMode.Simple;
        UpdateGlobalStatus("Press the replacement key.");
        Focus();
    }

    private void CaptureComboButton_Click(object sender, RoutedEventArgs e)
    {
        if (!CanEditBinding())
        {
            return;
        }

        _captureMode = CaptureMode.Combo;
        UpdateGlobalStatus("Hold modifiers, then press the combo key.");
        Focus();
    }

    private void Window_PreviewKeyDown(object sender, WpfKeyEventArgs e)
    {
        if (_captureMode == CaptureMode.None || _editingBinding is null)
        {
            return;
        }

        var key = WpfKeyToKeyForgeKey(e);
        if (key is null)
        {
            return;
        }

        if (_captureMode == CaptureMode.Simple)
        {
            _editingBinding.Type = BindingType.Simple;
            _editingBinding.Output = [MacroStep.Press(key)];
            _macroSteps.Clear();
        }
        else
        {
            var steps = BuildComboSteps(key);
            if (steps.Count == 0)
            {
                return;
            }

            _editingBinding.Type = steps.Count == 1 ? BindingType.Simple : BindingType.Combo;
            _editingBinding.Output = steps;
            _macroSteps.Clear();
        }

        BindingTypeComboBox.SelectedItem = _editingBinding.Type;
        _captureMode = CaptureMode.None;
        UpdateBindingPreview();
        UpdateGlobalStatus("Captured binding. Save to apply it.");
        e.Handled = true;
    }

    private void AddStepButton_Click(object sender, RoutedEventArgs e)
    {
        if (!CanEditBinding())
        {
            return;
        }

        if (_macroSteps.Count >= ProfileValidator.MaxMacroSteps)
        {
            WpfMessageBox.Show(this, "Macros are limited to 10 steps.", "Macro Limit", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var action = MacroActionComboBox.SelectedItem is MacroStepAction selectedAction
            ? selectedAction
            : MacroStepAction.Press;

        MacroStep step;
        if (action == MacroStepAction.Wait)
        {
            var delay = ParseInt(MacroDelayTextBox.Text, 50);
            step = MacroStep.Wait(Math.Clamp(delay, _settings.MacroMinimumDelayMs, ProfileValidator.MaxMacroDelayMs));
        }
        else
        {
            var key = MacroKeyComboBox.SelectedValue as string ?? KeyCatalog.All.First().Code;
            step = action switch
            {
                MacroStepAction.KeyDown => MacroStep.KeyDown(key),
                MacroStepAction.KeyUp => MacroStep.KeyUp(key),
                _ => MacroStep.Press(key)
            };
        }

        _macroSteps.Add(step);
        if (_editingBinding is not null)
        {
            _editingBinding.Type = BindingType.Macro;
            _editingBinding.Output = _macroSteps.Select(item => item.Clone()).ToList();
            BindingTypeComboBox.SelectedItem = BindingType.Macro;
            UpdateBindingPreview();
        }
    }

    private void RemoveStepButton_Click(object sender, RoutedEventArgs e)
    {
        if (MacroStepsListBox.SelectedItem is MacroStep step)
        {
            _macroSteps.Remove(step);
            if (_editingBinding is not null)
            {
                _editingBinding.Output = _macroSteps.Select(item => item.Clone()).ToList();
                UpdateBindingPreview();
            }
        }
    }

    private async void TestMacroButton_Click(object sender, RoutedEventArgs e)
    {
        if (_macroRunner is null || _macroSteps.Count == 0)
        {
            return;
        }

        try
        {
            await _macroRunner.RunAsync(_macroSteps.Select(step => step.Clone()));
        }
        catch (Exception ex)
        {
            WpfMessageBox.Show(this, ex.Message, "Macro Test Failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void SaveBindingButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedProfile is null || _selectedKey is null || _editingBinding is null)
        {
            return;
        }

        _editingBinding.Input = _selectedKey;
        _editingBinding.Type = BindingTypeComboBox.SelectedItem is BindingType type ? type : _editingBinding.Type;
        _editingBinding.BlockOriginal = BlockOriginalCheckBox.IsChecked == true;
        _editingBinding.RepeatWhileHeld = RepeatWhileHeldCheckBox.IsChecked == true;

        if (_editingBinding.Type == BindingType.Macro)
        {
            _editingBinding.Output = _macroSteps.Select(step => step.Clone()).ToList();
        }
        else if (_editingBinding.Type == BindingType.Disabled)
        {
            _editingBinding.Output.Clear();
        }

        var profileCopy = CloneProfile(_selectedProfile);
        ProfileValidator.ReplaceBinding(profileCopy, _editingBinding.Clone());
        var result = new ProfileValidator(_settings).Validate(profileCopy);
        if (!result.IsValid)
        {
            WpfMessageBox.Show(
                this,
                string.Join(Environment.NewLine, result.Errors.Select(issue => issue.Message)),
                "Binding Invalid",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        ProfileValidator.ReplaceBinding(_selectedProfile, _editingBinding.Clone());
        await SaveSelectedProfileAsync();
        LoadSelectedBinding();
        UpdateKeyboardVisuals();
        UpdateTrayMenu();
    }

    private async void ClearBindingButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedProfile is null || _selectedKey is null)
        {
            return;
        }

        _selectedProfile.Bindings.RemoveAll(binding =>
            string.Equals(KeyCatalog.Normalize(binding.Input), _selectedKey, StringComparison.OrdinalIgnoreCase));
        _selectedProfile.ModifiedAt = DateTimeOffset.UtcNow;
        await SaveSelectedProfileAsync();
        LoadSelectedBinding();
        UpdateKeyboardVisuals();
        UpdateTrayMenu();
    }

    private async void SaveSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        _settings.StartWithWindows = StartWithWindowsCheck.IsChecked == true;
        _settings.StartMinimized = StartMinimizedCheck.IsChecked == true;
        _settings.ShowActiveProfileNotification = NotificationsCheck.IsChecked == true;
        _settings.ThemePreset = ThemeComboBox.SelectedItem as string ?? "Obsidian Gold Red";
        _settings.BackgroundOpacity = Math.Clamp(ParseDouble(BackgroundOpacityTextBox.Text, 0.78), 0.15, 0.9);
        _settings.BackgroundBlur = Math.Clamp(ParseDouble(BackgroundBlurTextBox.Text, 2), 0, 24);
        _settings.KeyboardScale = Math.Clamp(KeyboardScaleSlider.Value, 0.72, 1.08);
        _settings.ShowCompactDiagnostics = ShowDiagnosticsCheckBox.IsChecked == true;
        _settings.IgnoreInjectedInput = IgnoreInjectedCheck.IsChecked == true;
        _settings.BlockOriginalKeyByDefault = BlockDefaultCheck.IsChecked == true;
        _settings.MacroMinimumDelayMs = Math.Clamp(ParseInt(MinDelayTextBox.Text, 20), 20, 5000);
        _settings.ProfileSwitchPollingIntervalMs = Math.Clamp(ParseInt(PollingIntervalTextBox.Text, 250), 100, 5000);
        _settings.WarnWhenAntiCheatMayBeActive = AntiCheatWarningCheck.IsChecked == true;
        _settings.DisableMacrosInOnlineGameProfiles = DisableOnlineMacrosCheck.IsChecked == true;

        await _settingsRepository.SaveAsync(_settings);
        WindowsStartupService.Apply(_settings.StartWithWindows);
        ApplyThemePreset(_settings.ThemePreset);
        ApplyKeyboardScale();
        UpdateProfileArtwork();
        CompactDiagnosticsPanel.Visibility = _settings.ShowCompactDiagnostics ? Visibility.Visible : Visibility.Collapsed;
        UpdateActiveWindowTimer();
        UpdateGlobalStatus("Settings saved.");
    }

    private void PauseButton_Click(object sender, RoutedEventArgs e)
    {
        if (_remappingEngine is null)
        {
            return;
        }

        _remappingEngine.SetPaused(!_remappingEngine.IsPaused);
        UpdatePauseButtonContent();
        UpdateTrayMenu();
    }

    private void ActiveWindowTimer_Tick(object? sender, EventArgs e)
    {
        var activeWindow = _foregroundAppDetector.GetActiveWindow();
        _lastActiveWindow = activeWindow;
        ActiveExeText.Text = string.IsNullOrWhiteSpace(activeWindow.ExecutableName)
            ? "No foreground app"
            : activeWindow.ExecutableName;

        var activeProfiles = ProfileMatcher.SelectActiveProfiles(_profiles, activeWindow);
        _activeProfiles = activeProfiles;
        _remappingEngine?.SetActiveProfiles(activeProfiles);
        ActiveProfileText.Text = FormatActiveProfileStack(activeProfiles);
        ActiveStackText.Text = $"Stack: {FormatActiveProfileStack(activeProfiles)}";
        ForegroundTitleText.Text = string.IsNullOrWhiteSpace(activeWindow.WindowTitle)
            ? "Window: none"
            : $"Window: {activeWindow.WindowTitle}";
        CaptureStateText.Text = GetKeyboardCaptureState(activeWindow, activeProfiles);
        UpdateActiveTargetDiagnostics(activeWindow, activeProfiles);
        RefreshDiagnosticsPanel();
        UpdateTrayMenu();
    }

    private void UpdateActiveTargetDiagnostics(ActiveWindowInfo activeWindow, IReadOnlyList<KeyForgeProfile> activeProfiles)
    {
        var foregroundSignature = $"{activeWindow.ProcessId}:{activeWindow.ExecutableName}:{activeWindow.WindowTitle}";
        var activeProfileId = string.Join("|", activeProfiles.Select(profile => profile.ProfileId));
        if (string.Equals(_lastForegroundSignature, foregroundSignature, StringComparison.Ordinal) &&
            string.Equals(_lastActiveProfileId, activeProfileId, StringComparison.Ordinal))
        {
            return;
        }

        _lastForegroundSignature = foregroundSignature;
        _lastActiveProfileId = activeProfileId;

        if (activeProfiles.Count == 0)
        {
            var message = string.IsNullOrWhiteSpace(activeWindow.ExecutableName)
                ? "No active profile"
                : $"No active profile for {activeWindow.ExecutableName}";
            if (!IsKeyForgeForeground(activeWindow))
            {
                UpdateGlobalStatus(message);
            }
            else
            {
                GlobalStatusText.Text = "KeyForge is focused. Waiting for a matching app.";
            }

            MatchStateText.Text = string.IsNullOrWhiteSpace(activeWindow.ExecutableName) ? "Waiting" : "Mismatch";
            HookStateText.Text = _keyboardHook?.IsRunning == true ? "Keyboard Capture: running" : "Keyboard Capture: stopped";
            if (!IsKeyForgeForeground(activeWindow))
            {
                AppLog.Info($"{message}. Window='{activeWindow.WindowTitle}' PID={activeWindow.ProcessId}");
            }
            return;
        }

        if (activeWindow.IsElevated == true && !IsCurrentProcessElevated())
        {
            var warning = $"Active profile stack: {FormatActiveProfileStack(activeProfiles)}. {activeWindow.ExecutableName} is elevated; run KeyForge as administrator.";
            UpdateGlobalStatus(warning);
            MatchStateText.Text = "Needs Admin";
            HookStateText.Text = _keyboardHook?.IsRunning == true ? "Keyboard Capture: needs admin" : "Keyboard Capture: stopped";
            AppLog.Info(warning);
            return;
        }

        var status = $"Active profile stack: {FormatActiveProfileStack(activeProfiles)} matched {activeWindow.ExecutableName}";
        UpdateGlobalStatus(status);
        MatchStateText.Text = _remappingEngine?.IsPaused == true ? "Paused" : "Active";
        HookStateText.Text = _keyboardHook?.IsRunning == true ? "Keyboard Capture: running" : "Keyboard Capture: stopped";
        AppLog.Info($"{status}. Window='{activeWindow.WindowTitle}' PID={activeWindow.ProcessId} Elevated={activeWindow.IsElevated?.ToString() ?? "unknown"}");
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_reallyExit)
        {
            return;
        }

        e.Cancel = true;
        Hide();
        _notifyIcon?.ShowBalloonTip(1200, "KeyForge", "KeyForge is still running in the tray.", Forms.ToolTipIcon.Info);
    }

    protected override void OnClosed(EventArgs e)
    {
        _activeWindowTimer.Stop();
        foreach (var window in _profileWindows.Values.ToList())
        {
            window.Close();
        }

        _profileWindows.Clear();
        _keyEditorWindow?.Close();
        _keyEditorWindow = null;
        _notifyIcon?.Dispose();
        _remappingEngine?.Dispose();
        base.OnClosed(e);
    }

    private void RefreshProfileFields()
    {
        _isLoadingUi = true;
        try
        {
            var hasProfile = _selectedProfile is not null;
            ProfileNameTextBox.IsEnabled = hasProfile;
            TargetExeTextBox.IsEnabled = hasProfile;
            WindowTitleTextBox.IsEnabled = hasProfile;
            ModeComboBox.IsEnabled = hasProfile;
            ProfileEnabledCheckBox.IsEnabled = hasProfile;
            ExportProfileButton.IsEnabled = hasProfile;
            DeleteProfileButton.IsEnabled = hasProfile;

            SetTextIfDifferent(ProfileNameTextBox, _selectedProfile?.ProfileName ?? string.Empty);
            SetTextIfDifferent(TargetExeTextBox, _selectedProfile?.Target.Exe ?? string.Empty);
            SetTextIfDifferent(WindowTitleTextBox, _selectedProfile?.Target.WindowTitleContains ?? string.Empty);
            TargetExeChipText.Text = _selectedProfile is null
                ? "No profile"
                : string.IsNullOrWhiteSpace(_selectedProfile.Target.Exe) ? "No target" : _selectedProfile.Target.Exe;
            ArtworkPathText.Text = string.IsNullOrWhiteSpace(_selectedProfile?.ArtworkPath)
                ? "No profile artwork selected"
                : $"Artwork: {Path.GetFileName(_selectedProfile.ArtworkPath)}";
            ModeComboBox.SelectedItem = _selectedProfile?.Mode ?? ProfileMode.Auto;
            ProfileEnabledCheckBox.IsChecked = _selectedProfile?.Enabled ?? false;
            BindingsCountText.Text = _selectedProfile is null
                ? "No profile selected"
                : $"{_selectedProfile.Bindings.Count} bindings";
        }
        finally
        {
            _isLoadingUi = false;
        }
        UpdateProfileArtwork();
        RefreshDiagnosticsPanel();
    }

    private void LoadSelectedBinding()
    {
        _isLoadingUi = true;
        try
        {
            _macroSteps.Clear();

            if (_selectedProfile is null || _selectedKey is null)
            {
                _editingBinding = null;
                SelectedKeyText.Text = "None";
                CurrentBindingText.Text = "Default";
                WarningTextBlock.Text = "Select a key on the keyboard to edit its binding.";
                SetEditorEnabled(false);
                return;
            }

            var existing = _selectedProfile.Bindings.FirstOrDefault(binding =>
                string.Equals(KeyCatalog.Normalize(binding.Input), _selectedKey, StringComparison.OrdinalIgnoreCase));
            _editingBinding = existing?.Clone() ?? new CoreKeyBinding
            {
                Input = _selectedKey,
                Type = BindingType.Simple,
                BlockOriginal = _settings.BlockOriginalKeyByDefault
            };

            SelectedKeyText.Text = KeyCatalog.LabelFor(_selectedKey);
            CurrentBindingText.Text = BindingFormatter.Format(existing);
            BindingTypeComboBox.SelectedItem = _editingBinding.Type;
            BlockOriginalCheckBox.IsChecked = _editingBinding.BlockOriginal;
            RepeatWhileHeldCheckBox.IsChecked = _editingBinding.RepeatWhileHeld;

            if (_editingBinding.Type == BindingType.Macro)
            {
                foreach (var step in _editingBinding.Output.Select(step => step.Clone()))
                {
                    _macroSteps.Add(step);
                }
            }

            MacroActionComboBox.SelectedItem = MacroStepAction.Press;
            MacroKeyComboBox.SelectedValue = "A";
            SetEditorEnabled(true);
            UpdateBindingWarning();
        }
        finally
        {
            _isLoadingUi = false;
        }
    }

    private void UpdateBindingPreview()
    {
        if (_editingBinding is null)
        {
            return;
        }

        if (_editingBinding.Type == BindingType.Macro)
        {
            _editingBinding.Output = _macroSteps.Select(step => step.Clone()).ToList();
        }

        CurrentBindingText.Text = BindingFormatter.Format(_editingBinding);
        UpdateBindingWarning();
    }

    private void UpdateBindingWarning()
    {
        var messages = new List<string>();
        if (_selectedKey is not null && KeyCatalog.IsRisky(_selectedKey))
        {
            messages.Add($"Warning: remapping {KeyCatalog.LabelFor(_selectedKey)} can affect system or game shortcuts.");
        }

        if (_editingBinding?.Type == BindingType.Macro && _settings.WarnWhenAntiCheatMayBeActive)
        {
            messages.Add("Some games prohibit macros, especially online competitive games.");
        }

        WarningTextBlock.Text = string.Join(Environment.NewLine, messages);
    }

    private void SetEditorEnabled(bool enabled)
    {
        BindingTypeComboBox.IsEnabled = enabled;
        BlockOriginalCheckBox.IsEnabled = enabled;
        RepeatWhileHeldCheckBox.IsEnabled = enabled;
        CaptureSimpleButton.IsEnabled = enabled;
        CaptureComboButton.IsEnabled = enabled;
        MacroStepsListBox.IsEnabled = enabled;
        MacroActionComboBox.IsEnabled = enabled;
        MacroKeyComboBox.IsEnabled = enabled;
        MacroDelayTextBox.IsEnabled = enabled;
        AddStepButton.IsEnabled = enabled;
        RemoveStepButton.IsEnabled = enabled;
        TestMacroButton.IsEnabled = enabled && _inputSender is not null;
        SaveBindingButton.IsEnabled = enabled;
        ClearBindingButton.IsEnabled = enabled;
    }

    private bool CanEditBinding()
    {
        return _selectedProfile is not null && _selectedKey is not null && _editingBinding is not null;
    }

    private void UpdateKeyboardVisuals()
    {
        foreach (var definition in KeyCatalog.All)
        {
            if (!_keyboardButtons.TryGetValue(definition.Code, out var button))
            {
                continue;
            }

            var binding = _selectedProfile?.Bindings.FirstOrDefault(candidate =>
                string.Equals(KeyCatalog.Normalize(candidate.Input), definition.Code, StringComparison.OrdinalIgnoreCase));
            var output = BindingFormatter.Format(binding);
            var label = binding is null
                ? definition.Label
                : $"{definition.Label}{Environment.NewLine}{output}";

            button.Content = new TextBlock
            {
                Text = label,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                FontSize = (binding is null ? 13 : 11) * Math.Clamp(_settings.KeyboardScale, 0.72, 1.08),
                Foreground = WpfBrushes.White
            };

            button.Background = binding is null
                ? (WpfBrush)FindResource("KeyboardKeyBrush")
                : binding.Type == BindingType.Macro
                    ? (WpfBrush)FindResource("KeyboardKeyMacroBrush")
                    : (WpfBrush)FindResource("KeyboardKeyMappedBrush");
            button.BorderBrush = string.Equals(_selectedKey, definition.Code, StringComparison.OrdinalIgnoreCase)
                ? (WpfBrush)FindResource("AccentBrush")
                : binding?.Type == BindingType.Disabled
                    ? (WpfBrush)FindResource("DangerBrush")
                    : new SolidColorBrush(WpfColor.FromArgb(230, 74, 88, 110));
            button.BorderThickness = string.Equals(_selectedKey, definition.Code, StringComparison.OrdinalIgnoreCase)
                ? new Thickness(2)
                : new Thickness(1);
        }

        if (BindingsCountText is not null)
        {
            BindingsCountText.Text = _selectedProfile is null
                ? "No profile selected"
                : $"{_selectedProfile.Bindings.Count} bindings";
        }
    }

    private void ApplyProfileFilter(KeyForgeProfile? keepSelection = null)
    {
        var query = ProfileSearchTextBox?.Text?.Trim() ?? string.Empty;
        var selected = keepSelection ?? ProfileListBox?.SelectedItem as KeyForgeProfile;

        _filteredProfiles.Clear();
        foreach (var profile in _profiles
                     .Where(profile => string.IsNullOrWhiteSpace(query) ||
                                       profile.ProfileName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                                       profile.Target.Exe.Contains(query, StringComparison.OrdinalIgnoreCase))
                     .OrderBy(profile => profile.ProfileName, StringComparer.OrdinalIgnoreCase))
        {
            _filteredProfiles.Add(profile);
        }

        if (selected is not null && _filteredProfiles.Contains(selected))
        {
            ProfileListBox!.SelectedItem = selected;
        }
    }

    private async Task SaveSelectedProfileAsync()
    {
        if (_selectedProfile is null)
        {
            return;
        }

        await _profileRepository.SaveAsync(_selectedProfile);
        UpdateKeyboardVisuals();
    }

    private void ApplySettingsToUi()
    {
        _isLoadingUi = true;
        try
        {
            StartWithWindowsCheck.IsChecked = _settings.StartWithWindows;
            StartMinimizedCheck.IsChecked = _settings.StartMinimized;
            NotificationsCheck.IsChecked = _settings.ShowActiveProfileNotification;
            ThemeComboBox.SelectedItem = _settings.Theme;
            ThemeComboBox.SelectedItem = string.IsNullOrWhiteSpace(_settings.ThemePreset)
                ? "Obsidian Gold Red"
                : _settings.ThemePreset;
            BackgroundOpacityTextBox.Text = _settings.BackgroundOpacity.ToString("0.##");
            BackgroundBlurTextBox.Text = _settings.BackgroundBlur.ToString("0.##");
            BackgroundOpacitySlider.Value = Math.Clamp(_settings.BackgroundOpacity, 0.15, 0.9);
            BackgroundBlurSlider.Value = Math.Clamp(_settings.BackgroundBlur, 0, 24);
            KeyboardScaleSlider.Value = Math.Clamp(_settings.KeyboardScale, 0.72, 1.08);
            KeyboardScaleText.Text = $"Keyboard Size: {Math.Round(_settings.KeyboardScale * 100)}%";
            ShowDiagnosticsCheckBox.IsChecked = _settings.ShowCompactDiagnostics;
            CompactDiagnosticsPanel.Visibility = _settings.ShowCompactDiagnostics ? Visibility.Visible : Visibility.Collapsed;
            EmergencyHotkeyTextBox.Text = _settings.EmergencyDisableHotkey;
            IgnoreInjectedCheck.IsChecked = _settings.IgnoreInjectedInput;
            BlockDefaultCheck.IsChecked = _settings.BlockOriginalKeyByDefault;
            MinDelayTextBox.Text = _settings.MacroMinimumDelayMs.ToString();
            PollingIntervalTextBox.Text = _settings.ProfileSwitchPollingIntervalMs.ToString();
            AntiCheatWarningCheck.IsChecked = _settings.WarnWhenAntiCheatMayBeActive;
            DisableOnlineMacrosCheck.IsChecked = _settings.DisableMacrosInOnlineGameProfiles;
        }
        finally
        {
            _isLoadingUi = false;
        }
    }

    private void InitializeTray()
    {
        _notifyIcon = new Forms.NotifyIcon
        {
            Icon = LoadTrayIcon(),
            Text = "KeyForge",
            Visible = true
        };
        _notifyIcon.DoubleClick += (_, _) => ShowMainWindow();
        UpdateTrayMenu();
    }

    private void UpdateTrayMenu()
    {
        if (_notifyIcon is null)
        {
            return;
        }

        _notifyIcon.Text = GetTrayText();

        var signature = BuildTrayMenuSignature();
        if (_notifyIcon.ContextMenuStrip?.Visible == true)
        {
            return;
        }

        if (_notifyIcon.ContextMenuStrip is not null &&
            string.Equals(_lastTrayMenuSignature, signature, StringComparison.Ordinal))
        {
            return;
        }

        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Open KeyForge", null, (_, _) => ShowMainWindow());
        menu.Items.Add(_remappingEngine?.IsPaused == true ? "Resume Remapping" : "Pause Remapping", null, (_, _) => PauseButton_Click(this, new RoutedEventArgs()));

        var profilesMenu = new Forms.ToolStripMenuItem("Profiles");
        foreach (var profile in _profiles.OrderBy(profile => profile.ProfileName, StringComparer.OrdinalIgnoreCase))
        {
            var item = new Forms.ToolStripMenuItem(profile.ProfileName)
            {
                Checked = _activeProfiles.Any(active => string.Equals(active.ProfileId, profile.ProfileId, StringComparison.OrdinalIgnoreCase))
            };
            item.Click += (_, _) =>
            {
                ShowMainWindow();
                ProfileListBox.SelectedItem = profile;
                OpenProfileDetails(profile, null);
            };
            profilesMenu.DropDownItems.Add(item);
        }

        menu.Items.Add(profilesMenu);
        menu.Items.Add("Settings", null, (_, _) =>
        {
            ShowMainWindow();
            ShowSettingsOverlay();
        });
        menu.Items.Add("Quit", null, (_, _) => QuitApplication());

        _notifyIcon.ContextMenuStrip?.Dispose();
        _notifyIcon.ContextMenuStrip = menu;
        _lastTrayMenuSignature = signature;
    }

    private void ShowMainWindow()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    private string BuildTrayMenuSignature()
    {
        var profileSignature = string.Join(";", _profiles
            .OrderBy(profile => profile.ProfileName, StringComparer.OrdinalIgnoreCase)
            .Select(profile => $"{profile.ProfileId}:{profile.ProfileName}:{profile.Enabled}:{profile.Mode}"));
        var activeSignature = string.Join(",", _activeProfiles.Select(profile => profile.ProfileId));
        return $"{_remappingEngine?.IsPaused == true}|{activeSignature}|{profileSignature}";
    }

    private string GetTrayText()
    {
        var text = _activeProfiles.Count == 0
            ? "KeyForge - Running"
            : $"KeyForge - {FormatActiveProfileStack(_activeProfiles)}";

        return text.Length <= 63 ? text : $"{text[..60]}...";
    }

    private void QuitApplication()
    {
        _reallyExit = true;
        _keyEditorWindow?.Close();
        _notifyIcon?.Dispose();
        _remappingEngine?.Dispose();
        WpfApplication.Current.Shutdown();
    }

    private void UpdateActiveWindowTimer()
    {
        _activeWindowTimer.Interval = TimeSpan.FromMilliseconds(Math.Clamp(_settings.ProfileSwitchPollingIntervalMs, 100, 5000));
    }

    private void UpdateGlobalStatus(string status)
    {
        GlobalStatusText.Text = status;
        UpdatePauseButtonContent();
        AddDiagnosticMessage(status);
    }

    private void UpdatePauseButtonContent()
    {
        if (PauseButton is null)
        {
            return;
        }

        PauseButton.Content = _remappingEngine?.IsPaused == true ? "\uE768" : "\uE769";
        PauseButton.ToolTip = _remappingEngine?.IsPaused == true ? "Resume remapping" : "Pause remapping";
    }

    private async void ProfileEnabledToggle_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not KeyForgeProfile profile)
        {
            return;
        }

        await _profileRepository.SaveAsync(profile);
        ApplyProfileFilter(keepSelection: _selectedProfile);
        UpdateKeyboardVisuals();
        UpdateTrayMenu();
        AddDiagnosticMessage($"{profile.ProfileName} {(profile.Enabled ? "enabled" : "disabled")}.");
        e.Handled = true;
    }

    private void OpenProfileDetails(KeyForgeProfile profile, string? selectedKey)
    {
        if (_profileWindows.TryGetValue(profile.ProfileId, out var existing))
        {
            existing.RefreshProfile();
            if (existing.WindowState == WindowState.Minimized)
            {
                existing.WindowState = WindowState.Normal;
            }

            existing.Activate();
            return;
        }

        var window = new ProfileDetailsWindow(profile, _settings, _profileRepository, _macroRunner)
        {
            Owner = this
        };
        window.ProfileChanged += ProfileDetailsWindow_ProfileChanged;
        window.Closed += (_, _) => _profileWindows.Remove(profile.ProfileId);
        _profileWindows[profile.ProfileId] = window;
        window.Show();
    }

    private void ProfileDetailsWindow_ProfileChanged(object? sender, KeyForgeProfile profile)
    {
        ApplyProfileFilter(keepSelection: profile);
        ProfileListBox.SelectedItem = profile;
        _selectedProfile = profile;
        RefreshProfileFields();
        UpdateKeyboardVisuals();
        UpdateProfileArtwork();
        UpdateTrayMenu();
    }

    private void OpenKeyEditor(KeyForgeProfile profile, string key, WpfButton? keyButton)
    {
        if (_keyEditorWindow is null)
        {
            _keyEditorWindow = new KeyEditorPopupWindow(_settings, _profileRepository, _macroRunner)
            {
                Owner = this
            };
            _keyEditorWindow.ProfileChanged += KeyEditorWindow_ProfileChanged;
            _keyEditorWindow.Closed += (_, _) => _keyEditorWindow = null;
        }

        _keyEditorWindow.Load(profile, key);
        PositionKeyEditorWindow(_keyEditorWindow, keyButton);
        if (!_keyEditorWindow.IsVisible)
        {
            _keyEditorWindow.Show();
        }

        _keyEditorWindow.Activate();
    }

    private void PositionKeyEditorWindow(Window editor, WpfButton? keyButton)
    {
        if (keyButton is null)
        {
            editor.Left = Left + Width - editor.Width - 36;
            editor.Top = Top + 120;
            return;
        }

        var screenPoint = keyButton.PointToScreen(new System.Windows.Point(keyButton.ActualWidth + 12, -6));
        var left = screenPoint.X;
        var top = screenPoint.Y;
        var workingArea = SystemParameters.WorkArea;

        if (left + editor.Width > workingArea.Right - 12)
        {
            left = keyButton.PointToScreen(new System.Windows.Point(-editor.Width - 12, -6)).X;
        }

        if (top + 360 > workingArea.Bottom - 12)
        {
            top = Math.Max(workingArea.Top + 12, workingArea.Bottom - 372);
        }

        editor.Left = Math.Max(workingArea.Left + 12, left);
        editor.Top = Math.Max(workingArea.Top + 12, top);
    }

    private void KeyEditorWindow_ProfileChanged(object? sender, KeyForgeProfile profile)
    {
        ApplyProfileFilter(keepSelection: profile);
        ProfileListBox.SelectedItem = profile;
        _selectedProfile = profile;
        RefreshProfileFields();
        LoadSelectedBinding();
        UpdateKeyboardVisuals();
        UpdateProfileArtwork();
        UpdateTrayMenu();
    }

    private async void KeyboardScaleSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isLoadingUi || KeyboardHost is null)
        {
            return;
        }

        _settings.KeyboardScale = Math.Clamp(e.NewValue, 0.72, 1.08);
        KeyboardScaleText.Text = $"Keyboard Size: {Math.Round(_settings.KeyboardScale * 100)}%";
        ApplyKeyboardScale();
        await _settingsRepository.SaveAsync(_settings);
    }

    private async void BackgroundSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isLoadingUi || ProfileArtworkImage is null)
        {
            return;
        }

        _settings.BackgroundOpacity = Math.Clamp(BackgroundOpacitySlider.Value, 0.15, 0.9);
        _settings.BackgroundBlur = Math.Clamp(BackgroundBlurSlider.Value, 0, 24);
        BackgroundOpacityTextBox.Text = _settings.BackgroundOpacity.ToString("0.##");
        BackgroundBlurTextBox.Text = _settings.BackgroundBlur.ToString("0.##");
        UpdateProfileArtwork();
        await _settingsRepository.SaveAsync(_settings);
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
            return;
        }

        DragMove();
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void MaximizeButton_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void CloseWindowButton_Click(object sender, RoutedEventArgs e) => Close();

    private void SettingsRailButton_Click(object sender, RoutedEventArgs e)
    {
        ShowSettingsOverlay();
    }

    private void AppearanceRailButton_Click(object sender, RoutedEventArgs e)
    {
        ShowSettingsOverlay();
        ThemeComboBox.Focus();
    }

    private void HelpRailButton_Click(object sender, RoutedEventArgs e)
    {
        WpfMessageBox.Show(
            this,
            "KeyForge watches the foreground executable and applies matching enabled profiles.\n\n" +
            "Use the status chips for active profile, target, foreground app, match state, and keyboard capture state.\n\n" +
            "Import / Export:\n" +
            "- Export saves the selected profile as a portable KeyForge JSON file.\n" +
            "- Import loads a KeyForge profile JSON into your profile list.\n" +
            "- Backgrounds and icons are stored as file paths, so send those assets too if another PC needs the same artwork.",
            "KeyForge Help",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void CloseSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        SettingsOverlay.Visibility = Visibility.Collapsed;
    }

    private void ShowSettingsOverlay()
    {
        RefreshDiagnosticsPanel();
        SettingsOverlay.Visibility = Visibility.Visible;
    }

    private bool NormalizeBackgroundDefaults()
    {
        var changed = false;
        if (Math.Abs(_settings.BackgroundOpacity - 0.58) < 0.001)
        {
            _settings.BackgroundOpacity = 0.78;
            changed = true;
        }

        if (Math.Abs(_settings.BackgroundBlur - 8) < 0.001)
        {
            _settings.BackgroundBlur = 2;
            changed = true;
        }

        return changed;
    }

    private void ApplyThemePreset(string? preset)
    {
        var normalized = string.IsNullOrWhiteSpace(preset) ? "Obsidian Gold Red" : preset;
        _settings.ThemePreset = normalized;

        var palette = normalized switch
        {
            "Electric Blue" => new ThemePalette(
                "#060A12", "#08101D", "#121B2A", "#17253A", "#0F1520",
                "#4BB3FF", "#123A5C", "#1D6BFF", "#1A2434", "#1C5B88"),
            "Violet Neon" => new ThemePalette(
                "#090812", "#0F0B1C", "#19152A", "#231C3A", "#12101C",
                "#B57CFF", "#3B205D", "#FF4DD8", "#211B30", "#4B2A71"),
            "High Contrast" => new ThemePalette(
                "#000000", "#050505", "#101010", "#1C1C1C", "#080808",
                "#FFFFFF", "#303030", "#FFEA00", "#151515", "#3A3A3A"),
            _ => new ThemePalette(
                "#07080D", "#090A10", "#141821", "#1A2030", "#111520",
                "#F2C230", "#3B2814", "#E6483E", "#1B202C", "#382019")
        };

        SetBrushResource("AppBackgroundBrush", palette.AppBackground);
        SetBrushResource("RailBrush", palette.Rail);
        SetBrushResource("PanelBrush", palette.Panel);
        SetBrushResource("PanelAltBrush", palette.PanelAlt);
        SetBrushResource("CardBrush", palette.Card);
        SetBrushResource("CardHoverBrush", palette.PanelAlt);
        SetBrushResource("AccentBrush", palette.Accent);
        SetBrushResource("AccentSoftBrush", palette.AccentSoft);
        SetBrushResource("AccentRedBrush", palette.AccentAlt);
        SetBrushResource("KeyboardKeyBrush", palette.Key);
        SetBrushResource("KeyboardKeyMappedBrush", palette.KeyMapped);
        SetBrushResource("KeyboardKeyMacroBrush", "#AA245B4D");
        SetBrushResource("BorderBrush", normalized == "High Contrast" ? "#FFFFFF" : "#2A3142");
        SetBrushResource("TextBrush", "#F4F7FB");
        SetBrushResource("TextMutedBrush", normalized == "High Contrast" ? "#D0D0D0" : "#9BA6B8");
        UpdateKeyboardVisuals();
    }

    private void ApplyKeyboardScale()
    {
        var selectedKey = _selectedKey;
        BuildKeyboard();
        _selectedKey = selectedKey;
        UpdateKeyboardVisuals();
        if (KeyboardScaleText is not null)
        {
            KeyboardScaleText.Text = $"Keyboard Size: {Math.Round(Math.Clamp(_settings.KeyboardScale, 0.72, 1.08) * 100)}%";
        }
    }

    private void SetBrushResource(string key, string color)
    {
        var brush = new SolidColorBrush((WpfColor)System.Windows.Media.ColorConverter.ConvertFromString(color));
        WpfApplication.Current.Resources[key] = brush;
        Resources[key] = brush;
    }

    private void UpdateProfileArtwork()
    {
        var path = _selectedProfile?.ArtworkPath;
        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
        {
            try
            {
                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
                image.UriSource = new Uri(path, UriKind.Absolute);
                image.EndInit();
                image.Freeze();
                ProfileArtworkImage.Source = image;
            }
            catch (Exception ex)
            {
                ProfileArtworkImage.Source = null;
                AppLog.Error($"Failed to load artwork '{path}'.", ex);
            }
        }
        else
        {
            ProfileArtworkImage.Source = null;
        }

        ProfileArtworkImage.Opacity = Math.Clamp(_settings.BackgroundOpacity, 0.15, 0.9);
        if (ProfileArtworkImage.Effect is BlurEffect blur)
        {
            blur.Radius = Math.Clamp(_settings.BackgroundBlur, 0, 24);
        }
    }

    private void RefreshDiagnosticsPanel()
    {
        DiagnosticsProfileText.Text = _selectedProfile is null
            ? "No profile selected"
            : $"Profile: {_selectedProfile.ProfileName}";
        DiagnosticsTargetText.Text = _selectedProfile is null
            ? "Target: none"
            : $"Target: {_selectedProfile.Target.Exe}";
        DiagnosticsWindowText.Text = _selectedProfile is null || string.IsNullOrWhiteSpace(_selectedProfile.Target.WindowTitleContains)
            ? "Window title match: none"
            : $"Window title match: {_selectedProfile.Target.WindowTitleContains}";
        DiagnosticsArtworkText.Text = _selectedProfile is null || string.IsNullOrWhiteSpace(_selectedProfile.ArtworkPath)
            ? "Artwork: none"
            : $"Artwork: {_selectedProfile.ArtworkPath}";
        DiagnosticsForegroundText.Text = string.IsNullOrWhiteSpace(_lastActiveWindow.ExecutableName)
            ? "Foreground: none"
            : $"Foreground: {_lastActiveWindow.ExecutableName} - {_lastActiveWindow.WindowTitle}";
        DiagnosticsMatchText.Text = $"Match: {MatchStateText.Text}";
        DiagnosticsHookText.Text = GetKeyboardCaptureState(_lastActiveWindow, _activeProfiles);
    }

    private string GetKeyboardCaptureState(ActiveWindowInfo activeWindow, IReadOnlyList<KeyForgeProfile> activeProfiles)
    {
        if (_remappingEngine?.IsPaused == true)
        {
            return "Keyboard Capture: paused";
        }

        if (_keyboardHook?.IsRunning != true)
        {
            return "Keyboard Capture: stopped";
        }

        if (activeWindow.IsElevated == true && !IsCurrentProcessElevated())
        {
            return "Keyboard Capture: needs admin for this foreground app";
        }

        return activeProfiles.Count == 0
            ? "Keyboard Capture: running, waiting for match"
            : "Keyboard Capture: running";
    }

    private static string FormatActiveProfileStack(IReadOnlyList<KeyForgeProfile> activeProfiles)
    {
        return activeProfiles.Count switch
        {
            0 => "None",
            1 => activeProfiles[0].ProfileName,
            _ => $"{activeProfiles[0].ProfileName} + {activeProfiles.Count - 1} overlay{(activeProfiles.Count == 2 ? string.Empty : "s")}"
        };
    }

    private static bool IsKeyForgeForeground(ActiveWindowInfo activeWindow)
    {
        return string.Equals(activeWindow.ExecutableName, "KeyForge.App.exe", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(activeWindow.ProcessName, "KeyForge.App", StringComparison.OrdinalIgnoreCase);
    }

    private void AddDiagnosticMessage(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        var line = $"{DateTime.Now:HH:mm:ss}  {message}";
        if (_diagnosticMessages.Count > 0 && string.Equals(_diagnosticMessages[0], line, StringComparison.Ordinal))
        {
            return;
        }

        _diagnosticMessages.Insert(0, line);
        while (_diagnosticMessages.Count > 60)
        {
            _diagnosticMessages.RemoveAt(_diagnosticMessages.Count - 1);
        }
    }

    private List<MacroStep> BuildComboSteps(string key)
    {
        var modifiers = new List<string>();
        AddModifierIfDown(modifiers, Key.LeftCtrl, "LeftCtrl");
        AddModifierIfDown(modifiers, Key.RightCtrl, "RightCtrl");
        AddModifierIfDown(modifiers, Key.LeftShift, "LeftShift");
        AddModifierIfDown(modifiers, Key.RightShift, "RightShift");
        AddModifierIfDown(modifiers, Key.LeftAlt, "LeftAlt");
        AddModifierIfDown(modifiers, Key.RightAlt, "RightAlt");
        AddModifierIfDown(modifiers, Key.LWin, "LeftWin");
        AddModifierIfDown(modifiers, Key.RWin, "RightWin");

        modifiers.RemoveAll(modifier => string.Equals(modifier, key, StringComparison.OrdinalIgnoreCase));

        if (KeyCatalog.IsModifier(key) && modifiers.Count == 0)
        {
            return [];
        }

        if (modifiers.Count == 0)
        {
            return [MacroStep.Press(key)];
        }

        var steps = new List<MacroStep>();
        steps.AddRange(modifiers.Select(MacroStep.KeyDown));
        steps.Add(MacroStep.Press(key));
        steps.AddRange(modifiers.AsEnumerable().Reverse().Select(MacroStep.KeyUp));
        return steps;
    }

    private static void AddModifierIfDown(List<string> modifiers, Key key, string keyForgeName)
    {
        if (Keyboard.IsKeyDown(key))
        {
            modifiers.Add(keyForgeName);
        }
    }

    private static string? WpfKeyToKeyForgeKey(WpfKeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        key = key == Key.ImeProcessed ? e.ImeProcessedKey : key;

        return key switch
        {
            Key.Escape => "Esc",
            Key.Oem3 => "Backquote",
            Key.D0 => "D0",
            Key.D1 => "D1",
            Key.D2 => "D2",
            Key.D3 => "D3",
            Key.D4 => "D4",
            Key.D5 => "D5",
            Key.D6 => "D6",
            Key.D7 => "D7",
            Key.D8 => "D8",
            Key.D9 => "D9",
            Key.OemMinus => "Minus",
            Key.OemPlus => "Equal",
            Key.Back => "Backspace",
            Key.Tab => "Tab",
            Key.OemOpenBrackets => "LeftBracket",
            Key.OemCloseBrackets => "RightBracket",
            Key.Oem5 => "Backslash",
            Key.CapsLock => "CapsLock",
            Key.OemSemicolon => "Semicolon",
            Key.OemQuotes => "Quote",
            Key.Enter => "Enter",
            Key.LeftShift => "LeftShift",
            Key.RightShift => "RightShift",
            Key.OemComma => "Comma",
            Key.OemPeriod => "Period",
            Key.OemQuestion => "Slash",
            Key.LeftCtrl => "LeftCtrl",
            Key.RightCtrl => "RightCtrl",
            Key.LWin => "LeftWin",
            Key.RWin => "RightWin",
            Key.LeftAlt => "LeftAlt",
            Key.RightAlt => "RightAlt",
            Key.Space => "Space",
            Key.Apps => "Menu",
            Key.PrintScreen => "PrintScreen",
            Key.Scroll => "ScrollLock",
            Key.Pause => "Pause",
            Key.Insert => "Insert",
            Key.Delete => "Delete",
            Key.Home => "Home",
            Key.End => "End",
            Key.PageUp => "PageUp",
            Key.PageDown => "PageDown",
            Key.Up => "Up",
            Key.Down => "Down",
            Key.Left => "Left",
            Key.Right => "Right",
            Key.NumLock => "NumLock",
            Key.Divide => "NumpadDivide",
            Key.Multiply => "NumpadMultiply",
            Key.Subtract => "NumpadMinus",
            Key.Add => "NumpadPlus",
            Key.Decimal => "NumpadDecimal",
            >= Key.A and <= Key.Z => key.ToString(),
            >= Key.F1 and <= Key.F12 => key.ToString(),
            >= Key.NumPad0 and <= Key.NumPad9 => $"Numpad{key - Key.NumPad0}",
            _ => null
        };
    }

    private string CreateUniqueProfileId(string seed)
    {
        var clean = string.Join('-', seed
            .ToLowerInvariant()
            .Split(Path.GetInvalidFileNameChars().Concat([' ', '.', '_']).ToArray(), StringSplitOptions.RemoveEmptyEntries));

        if (string.IsNullOrWhiteSpace(clean))
        {
            clean = "profile";
        }

        var id = clean;
        var suffix = 2;
        while (_profiles.Any(profile => string.Equals(profile.ProfileId, id, StringComparison.OrdinalIgnoreCase)))
        {
            id = $"{clean}-{suffix++}";
        }

        return id;
    }

    private void ReplaceProfileInCollection(KeyForgeProfile profile)
    {
        var existing = _profiles.FirstOrDefault(item =>
            string.Equals(item.ProfileId, profile.ProfileId, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            var index = _profiles.IndexOf(existing);
            _profiles[index] = profile;
        }
        else
        {
            _profiles.Add(profile);
        }
    }

    private static int ParseInt(string text, int fallback)
    {
        return int.TryParse(text, out var value) ? value : fallback;
    }

    private static double ParseDouble(string text, double fallback)
    {
        return double.TryParse(text, out var value) ? value : fallback;
    }

    private static bool IsCurrentProcessElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    private static string GetDisplayVersion()
    {
        var version = typeof(MainWindow).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        return string.IsNullOrWhiteSpace(version)
            ? typeof(MainWindow).Assembly.GetName().Version?.ToString(3) ?? "0.0.0"
            : version;
    }

    private static System.Drawing.Icon LoadTrayIcon()
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(Environment.ProcessPath) && File.Exists(Environment.ProcessPath))
            {
                return System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath) ?? System.Drawing.SystemIcons.Application;
            }
        }
        catch
        {
        }

        return System.Drawing.SystemIcons.Application;
    }

    private static string? NullIfWhiteSpace(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static void SetTextIfDifferent(WpfTextBox textBox, string value)
    {
        if (string.Equals(textBox.Text, value, StringComparison.Ordinal))
        {
            return;
        }

        textBox.Text = value;
    }

    private static T? FindVisualParent<T>(DependencyObject? child)
        where T : DependencyObject
    {
        while (child is not null)
        {
            if (child is T match)
            {
                return match;
            }

            child = VisualTreeHelper.GetParent(child);
        }

        return null;
    }

    private static KeyForgeProfile CloneProfile(KeyForgeProfile profile)
    {
        return new KeyForgeProfile
        {
            Version = profile.Version,
            ProfileId = profile.ProfileId,
            ProfileName = profile.ProfileName,
            Target = new ProfileTarget
            {
                Exe = profile.Target.Exe,
                ExecutablePath = profile.Target.ExecutablePath,
                WindowTitleContains = profile.Target.WindowTitleContains
            },
            ArtworkPath = profile.ArtworkPath,
            IconPath = profile.IconPath,
            AccentHint = profile.AccentHint,
            Mode = profile.Mode,
            Enabled = profile.Enabled,
            CreatedAt = profile.CreatedAt,
            ModifiedAt = profile.ModifiedAt,
            AppVersion = profile.AppVersion,
            Bindings = profile.Bindings.Select(binding => binding.Clone()).ToList()
        };
    }

    private enum CaptureMode
    {
        None,
        Simple,
        Combo
    }

    private sealed record ThemePalette(
        string AppBackground,
        string Rail,
        string Panel,
        string PanelAlt,
        string Card,
        string Accent,
        string AccentSoft,
        string AccentAlt,
        string Key,
        string KeyMapped);
}
