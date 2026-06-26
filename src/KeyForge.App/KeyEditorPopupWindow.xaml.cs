using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using KeyForge.Core.Keyboard;
using KeyForge.Core.Models;
using KeyForge.Core.Services;
using KeyForge.Core.Validation;
using KeyForge.Input;
using KeyForge.Storage;
using CoreKeyBinding = KeyForge.Core.Models.KeyBinding;
using WpfMessageBox = System.Windows.MessageBox;

namespace KeyForge.App;

public partial class KeyEditorPopupWindow : Window
{
    private static readonly string[] BindingModes = ["Keybind", "Combo", "Macro", "Disabled"];

    private readonly AppSettings _settings;
    private readonly IProfileRepository _profileRepository;
    private readonly IMacroRunner? _macroRunner;
    private readonly ObservableCollection<MacroStep> _macroSteps = [];
    private KeyForgeProfile? _profile;
    private CoreKeyBinding? _editingBinding;
    private string? _selectedKey;
    private CaptureMode _captureMode;
    private bool _isLoadingUi;

    public KeyEditorPopupWindow(AppSettings settings, IProfileRepository profileRepository, IMacroRunner? macroRunner)
    {
        _settings = settings;
        _profileRepository = profileRepository;
        _macroRunner = macroRunner;

        InitializeComponent();
        BindingModeComboBox.ItemsSource = BindingModes;
        MacroActionComboBox.ItemsSource = Enum.GetValues<MacroStepAction>();
        MacroKeyComboBox.ItemsSource = KeyCatalog.All.OrderBy(key => key.Label).ToList();
        MacroStepsListBox.ItemsSource = _macroSteps;
    }

    public event EventHandler<KeyForgeProfile>? ProfileChanged;

    public void Load(KeyForgeProfile profile, string selectedKey)
    {
        _profile = profile;
        _selectedKey = KeyCatalog.Normalize(selectedKey);
        TitleText.Text = KeyCatalog.LabelFor(_selectedKey);
        LoadSelectedBinding();
    }

    private void LoadSelectedBinding()
    {
        _isLoadingUi = true;
        try
        {
            _macroSteps.Clear();
            if (_profile is null || _selectedKey is null)
            {
                return;
            }

            var existing = _profile.Bindings.FirstOrDefault(binding =>
                string.Equals(KeyCatalog.Normalize(binding.Input), _selectedKey, StringComparison.OrdinalIgnoreCase));
            _editingBinding = existing?.Clone() ?? new CoreKeyBinding
            {
                Input = _selectedKey,
                Type = BindingType.Simple,
                BlockOriginal = _settings.BlockOriginalKeyByDefault
            };

            CurrentBindingText.Text = BindingFormatter.Format(existing);
            BindingModeComboBox.SelectedItem = ModeFromBinding(_editingBinding.Type);
            BlockOriginalCheckBox.IsChecked = _editingBinding.BlockOriginal;
            RepeatWhileHeldCheckBox.IsChecked = _editingBinding.RepeatWhileHeld;

            foreach (var step in _editingBinding.Output.Select(step => step.Clone()))
            {
                _macroSteps.Add(step);
            }

            MacroActionComboBox.SelectedItem = MacroStepAction.Press;
            MacroKeyComboBox.SelectedValue = "A";
            CaptureStatusText.Text = "Ready";
            UpdateAdvancedVisibility();
            UpdateWarning();
        }
        finally
        {
            _isLoadingUi = false;
        }
    }

    private async void BindingModeComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_isLoadingUi || _editingBinding is null)
        {
            return;
        }

        _editingBinding.Type = TypeFromMode(BindingModeComboBox.SelectedItem as string);
        if (_editingBinding.Type == BindingType.Disabled)
        {
            _editingBinding.Output.Clear();
            _macroSteps.Clear();
        }
        else if (_editingBinding.Type == BindingType.Macro)
        {
            _editingBinding.Output = _macroSteps.Select(step => step.Clone()).ToList();
        }

        UpdateAdvancedVisibility();
        UpdatePreview();
        if (_editingBinding.Type == BindingType.Disabled)
        {
            await SaveCurrentBindingAsync("Disabled and saved.");
        }
        else
        {
            CaptureStatusText.Text = "Click the rebind box, then press a key.";
        }
    }

    private void CaptureButton_Click(object sender, RoutedEventArgs e)
    {
        if (_editingBinding is null)
        {
            return;
        }

        var type = TypeFromMode(BindingModeComboBox.SelectedItem as string);
        _captureMode = type == BindingType.Combo ? CaptureMode.Combo : CaptureMode.Simple;
        CaptureStatusText.Text = _captureMode == CaptureMode.Combo
            ? "Hold modifiers, then press the combo key. Saves automatically."
            : "Waiting for key press. Saves automatically.";
        Focus();
    }

    private async void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
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

        if (_captureMode == CaptureMode.Combo)
        {
            var steps = BuildComboSteps(key);
            if (steps.Count == 0)
            {
                return;
            }

            _editingBinding.Type = steps.Count == 1 ? BindingType.Simple : BindingType.Combo;
            _editingBinding.Output = steps;
            _macroSteps.Clear();
            BindingModeComboBox.SelectedItem = ModeFromBinding(_editingBinding.Type);
        }
        else if (TypeFromMode(BindingModeComboBox.SelectedItem as string) == BindingType.Macro)
        {
            var step = MacroStep.Press(key);
            _macroSteps.Add(step);
            _editingBinding.Type = BindingType.Macro;
            _editingBinding.Output = _macroSteps.Select(item => item.Clone()).ToList();
        }
        else
        {
            _editingBinding.Type = BindingType.Simple;
            _editingBinding.Output = [MacroStep.Press(key)];
            _macroSteps.Clear();
            BindingModeComboBox.SelectedItem = "Keybind";
        }

        _captureMode = CaptureMode.None;
        UpdatePreview();
        await SaveCurrentBindingAsync("Captured and saved.");
        e.Handled = true;
    }

    private async void AddStepButton_Click(object sender, RoutedEventArgs e)
    {
        if (_editingBinding is null)
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
        _editingBinding.Type = BindingType.Macro;
        _editingBinding.Output = _macroSteps.Select(item => item.Clone()).ToList();
        BindingModeComboBox.SelectedItem = "Macro";
        UpdatePreview();
        await SaveCurrentBindingAsync("Macro step saved.");
    }

    private async void RemoveStepButton_Click(object sender, RoutedEventArgs e)
    {
        if (MacroStepsListBox.SelectedItem is not MacroStep step || _editingBinding is null)
        {
            return;
        }

        _macroSteps.Remove(step);
        _editingBinding.Output = _macroSteps.Select(item => item.Clone()).ToList();
        UpdatePreview();
        await SaveCurrentBindingAsync("Macro step removed.");
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

    private async Task<bool> SaveCurrentBindingAsync(string status)
    {
        if (_profile is null || _selectedKey is null || _editingBinding is null)
        {
            return false;
        }

        _editingBinding.Input = _selectedKey;
        _editingBinding.Type = TypeFromMode(BindingModeComboBox.SelectedItem as string);
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

        var profileCopy = CloneProfile(_profile);
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
            return false;
        }

        ProfileValidator.ReplaceBinding(_profile, _editingBinding.Clone());
        await _profileRepository.SaveAsync(_profile);
        ProfileChanged?.Invoke(this, _profile);
        LoadSelectedBinding();
        CaptureStatusText.Text = status;
        return true;
    }

    private async void ClearButton_Click(object sender, RoutedEventArgs e)
    {
        if (_profile is null || _selectedKey is null)
        {
            return;
        }

        _profile.Bindings.RemoveAll(binding =>
            string.Equals(KeyCatalog.Normalize(binding.Input), _selectedKey, StringComparison.OrdinalIgnoreCase));
        _profile.ModifiedAt = DateTimeOffset.UtcNow;
        await _profileRepository.SaveAsync(_profile);
        ProfileChanged?.Invoke(this, _profile);
        LoadSelectedBinding();
        CaptureStatusText.Text = "Cleared and saved.";
    }

    private void AdvancedCheckBox_Changed(object sender, RoutedEventArgs e) => UpdateAdvancedVisibility();

    private async void AdvancedOption_Changed(object sender, RoutedEventArgs e)
    {
        if (_isLoadingUi || _editingBinding is null)
        {
            return;
        }

        _editingBinding.BlockOriginal = BlockOriginalCheckBox.IsChecked == true;
        _editingBinding.RepeatWhileHeld = RepeatWhileHeldCheckBox.IsChecked == true;
        UpdatePreview();
        await SaveCurrentBindingAsync("Option saved.");
    }

    private void UpdateAdvancedVisibility()
    {
        var isAdvanced = AdvancedCheckBox.IsChecked == true ||
                         string.Equals(BindingModeComboBox.SelectedItem as string, "Macro", StringComparison.OrdinalIgnoreCase);
        AdvancedPanel.Visibility = isAdvanced ? Visibility.Visible : Visibility.Collapsed;
        RepeatWhileHeldCheckBox.IsEnabled = string.Equals(BindingModeComboBox.SelectedItem as string, "Macro", StringComparison.OrdinalIgnoreCase);
    }

    private void UpdatePreview()
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
        UpdateWarning();
    }

    private void UpdateWarning()
    {
        var messages = new List<string>();
        if (_selectedKey is not null && KeyCatalog.IsRisky(_selectedKey))
        {
            messages.Add($"{KeyCatalog.LabelFor(_selectedKey)} can affect system or game shortcuts.");
        }

        if (string.Equals(BindingModeComboBox.SelectedItem as string, "Macro", StringComparison.OrdinalIgnoreCase) &&
            _settings.WarnWhenAntiCheatMayBeActive)
        {
            messages.Add("Some games prohibit macros.");
        }

        WarningTextBlock.Text = string.Join(Environment.NewLine, messages);
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => DragMove();

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Hide();

    private static BindingType TypeFromMode(string? mode)
    {
        return mode switch
        {
            "Disabled" => BindingType.Disabled,
            "Combo" => BindingType.Combo,
            "Macro" => BindingType.Macro,
            _ => BindingType.Simple
        };
    }

    private static string ModeFromBinding(BindingType type)
    {
        return type switch
        {
            BindingType.Disabled => "Disabled",
            BindingType.Combo => "Combo",
            BindingType.Macro => "Macro",
            _ => "Keybind"
        };
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

    private static string? WpfKeyToKeyForgeKey(System.Windows.Input.KeyEventArgs e)
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

    private static int ParseInt(string text, int fallback) => int.TryParse(text, out var value) ? value : fallback;

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
}
