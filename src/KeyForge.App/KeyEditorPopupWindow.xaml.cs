using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using KeyForge.Core.Keyboard;
using KeyForge.Core.Models;
using KeyForge.Core.Services;
using KeyForge.Core.Validation;
using KeyForge.Input;
using KeyForge.Storage;
using CoreKeyBinding = KeyForge.Core.Models.KeyBinding;
using WpfDragEventArgs = System.Windows.DragEventArgs;
using WpfDragDropEffects = System.Windows.DragDropEffects;
using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;
using WpfMessageBox = System.Windows.MessageBox;
using WpfMouseEventArgs = System.Windows.Input.MouseEventArgs;
using WpfPoint = System.Windows.Point;
using WpfTextBox = System.Windows.Controls.TextBox;

namespace KeyForge.App;

public partial class KeyEditorPopupWindow : Window
{
    private static readonly string[] BindingModes = ["Keybind", "Combo", "Macro", "Disabled"];

    private readonly AppSettings _settings;
    private readonly IProfileRepository _profileRepository;
    private readonly IMacroRunner? _macroRunner;
    private readonly ObservableCollection<MacroStep> _macroSteps = [];
    private WpfPoint _dragStartPoint;
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
        MacroActions = Enum.GetValues<MacroStepAction>();
        MacroKeys = KeyCatalog.All
            .OrderBy(key => key.Label, StringComparer.OrdinalIgnoreCase)
            .Select(key => new KeyOption(key.Code, key.Label))
            .ToList();
        DelayPlacements = Enum.GetValues<MacroStepDelayPlacement>();

        InitializeComponent();
        BindingModeComboBox.ItemsSource = BindingModes;
        MacroActionComboBox.ItemsSource = MacroActions;
        MacroKeyComboBox.ItemsSource = MacroKeys;
        MacroDelayPlacementComboBox.ItemsSource = DelayPlacements;
        MacroStepsListBox.ItemsSource = _macroSteps;
    }

    public IReadOnlyList<MacroStepAction> MacroActions { get; }

    public IReadOnlyList<KeyOption> MacroKeys { get; }

    public IReadOnlyList<MacroStepDelayPlacement> DelayPlacements { get; }

    public event EventHandler<KeyForgeProfile>? ProfileChanged;

    public event EventHandler? LayoutSizeChanged;

    public void Load(KeyForgeProfile profile, string selectedKey)
    {
        _profile = profile;
        _selectedKey = KeyCatalog.Normalize(selectedKey);
        TitleText.Text = "Binding Editor";
        SelectedKeyText.Text = KeyCatalog.LabelFor(_selectedKey);
        LoadSelectedBinding();
        QueueLayoutSizeChanged();
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
                NormalizeStepForEditor(step);
                _macroSteps.Add(step);
            }

            MacroActionComboBox.SelectedItem = MacroStepAction.Press;
            MacroKeyComboBox.SelectedValue = "A";
            MacroDelayPlacementComboBox.SelectedItem = MacroStepDelayPlacement.After;
            MacroDelayTextBox.Text = "0";
            UpdateComposerVisibility();
            UpdateAdvancedVisibility();
            UpdateWarning();
        }
        finally
        {
            _isLoadingUi = false;
        }
    }

    private async void BindingModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
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
            await SaveCurrentBindingAsync(reloadAfterSave: false);
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
        CurrentBindingText.Text = _captureMode == CaptureMode.Combo ? "Listening for combo..." : "Listening for key...";
        Focus();
    }

    private async void Window_PreviewKeyDown(object sender, WpfKeyEventArgs e)
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
        await SaveCurrentBindingAsync(reloadAfterSave: true);
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

        _macroSteps.Add(CreateStepFromComposer());
        _editingBinding.Type = BindingType.Macro;
        _editingBinding.Output = _macroSteps.Select(item => item.Clone()).ToList();
        BindingModeComboBox.SelectedItem = "Macro";
        UpdatePreview();
        await SaveCurrentBindingAsync(reloadAfterSave: false);
        QueueLayoutSizeChanged();
    }

    private async void MacroStepRemoveButton_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not MacroStep step || _editingBinding is null)
        {
            return;
        }

        _macroSteps.Remove(step);
        _editingBinding.Output = _macroSteps.Select(item => item.Clone()).ToList();
        UpdatePreview();
        await SaveCurrentBindingAsync(reloadAfterSave: false);
        QueueLayoutSizeChanged();
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

    private async void MacroStepControl_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoadingUi || (sender as FrameworkElement)?.DataContext is not MacroStep step)
        {
            return;
        }

        NormalizeStepForEditor(step);
        UpdatePreview();
        await SaveCurrentBindingAsync(reloadAfterSave: false);
    }

    private async void MacroStepDelayTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is WpfTextBox textBox)
        {
            await ApplyMacroStepDelayAsync(textBox);
        }
    }

    private async void MacroStepDelayTextBox_KeyDown(object sender, WpfKeyEventArgs e)
    {
        if (e.Key != Key.Enter || sender is not WpfTextBox textBox)
        {
            return;
        }

        e.Handled = true;
        await ApplyMacroStepDelayAsync(textBox);
        Keyboard.ClearFocus();
    }

    private void MacroComposerAction_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoadingUi)
        {
            return;
        }

        UpdateComposerVisibility();
    }

    private void MacroStepsListBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStartPoint = e.GetPosition(null);
    }

    private void MacroStepsListBox_MouseMove(object sender, WpfMouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var current = e.GetPosition(null);
        if (Math.Abs(current.X - _dragStartPoint.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(current.Y - _dragStartPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        var item = FindVisualParent<ListBoxItem>(e.OriginalSource as DependencyObject);
        if (item?.DataContext is MacroStep step)
        {
            DragDrop.DoDragDrop(item, step, WpfDragDropEffects.Move);
        }
    }

    private async void MacroStepsListBox_Drop(object sender, WpfDragEventArgs e)
    {
        if (!e.Data.GetDataPresent(typeof(MacroStep)) ||
            e.Data.GetData(typeof(MacroStep)) is not MacroStep draggedStep)
        {
            return;
        }

        var targetItem = FindVisualParent<ListBoxItem>(e.OriginalSource as DependencyObject);
        var targetStep = targetItem?.DataContext as MacroStep;
        var oldIndex = _macroSteps.IndexOf(draggedStep);
        if (oldIndex < 0)
        {
            return;
        }

        var newIndex = targetStep is null ? _macroSteps.Count - 1 : _macroSteps.IndexOf(targetStep);
        if (newIndex < 0 || newIndex == oldIndex)
        {
            return;
        }

        _macroSteps.Move(oldIndex, newIndex);
        if (_editingBinding is not null)
        {
            _editingBinding.Output = _macroSteps.Select(item => item.Clone()).ToList();
            UpdatePreview();
            await SaveCurrentBindingAsync(reloadAfterSave: false);
        }
    }

    private async Task<bool> SaveCurrentBindingAsync(bool reloadAfterSave)
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
            foreach (var step in _macroSteps)
            {
                NormalizeStepForEditor(step);
            }

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
        if (reloadAfterSave)
        {
            LoadSelectedBinding();
        }
        else
        {
            CurrentBindingText.Text = BindingFormatter.Format(_editingBinding);
            UpdateWarning();
        }

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
        await SaveCurrentBindingAsync(reloadAfterSave: false);
    }

    private void UpdateAdvancedVisibility()
    {
        var isAdvanced = AdvancedCheckBox.IsChecked == true ||
                         string.Equals(BindingModeComboBox.SelectedItem as string, "Macro", StringComparison.OrdinalIgnoreCase);
        AdvancedPanel.Visibility = isAdvanced ? Visibility.Visible : Visibility.Collapsed;
        RepeatWhileHeldCheckBox.IsEnabled = string.Equals(BindingModeComboBox.SelectedItem as string, "Macro", StringComparison.OrdinalIgnoreCase);
        QueueLayoutSizeChanged();
    }

    private void UpdateComposerVisibility()
    {
        var isWait = MacroActionComboBox.SelectedItem is MacroStepAction.Wait;
        MacroKeyComboBox.Visibility = isWait ? Visibility.Collapsed : Visibility.Visible;
        MacroDelayPlacementComboBox.Visibility = isWait ? Visibility.Collapsed : Visibility.Visible;
        MacroWaitOnlyText.Visibility = isWait ? Visibility.Visible : Visibility.Collapsed;
        if (isWait && ParseInt(MacroDelayTextBox.Text, 0) <= 0)
        {
            MacroDelayTextBox.Text = Math.Max(_settings.MacroMinimumDelayMs, 50).ToString();
        }
        else if (!isWait && string.IsNullOrWhiteSpace(MacroDelayTextBox.Text))
        {
            MacroDelayTextBox.Text = "0";
        }
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

    private MacroStep CreateStepFromComposer()
    {
        var action = MacroActionComboBox.SelectedItem is MacroStepAction selectedAction
            ? selectedAction
            : MacroStepAction.Press;

        if (action == MacroStepAction.Wait)
        {
            return MacroStep.Wait(ParseRequiredDelay(MacroDelayTextBox.Text));
        }

        var key = MacroKeyComboBox.SelectedValue as string ?? "A";
        var step = action switch
        {
            MacroStepAction.KeyDown => MacroStep.KeyDown(key),
            MacroStepAction.KeyUp => MacroStep.KeyUp(key),
            _ => MacroStep.Press(key)
        };
        step.DelayPlacement = MacroDelayPlacementComboBox.SelectedItem is MacroStepDelayPlacement placement
            ? placement
            : MacroStepDelayPlacement.After;
        step.DelayMs = ParseOptionalDelay(MacroDelayTextBox.Text);
        NormalizeStepForEditor(step);
        return step;
    }

    private async Task ApplyMacroStepDelayAsync(WpfTextBox textBox)
    {
        if (_isLoadingUi || textBox.DataContext is not MacroStep step)
        {
            return;
        }

        step.DelayMs = step.Action == MacroStepAction.Wait
            ? ParseRequiredDelay(textBox.Text)
            : ParseOptionalDelay(textBox.Text);
        NormalizeStepForEditor(step);
        textBox.Text = step.DelayMs?.ToString() ?? string.Empty;
        UpdatePreview();
        await SaveCurrentBindingAsync(reloadAfterSave: false);
    }

    private void NormalizeStepForEditor(MacroStep step)
    {
        if (step.Action == MacroStepAction.Wait)
        {
            step.Key = null;
            step.DelayPlacement = MacroStepDelayPlacement.After;
            step.DelayMs = ParseRequiredDelay(step.DelayMs?.ToString() ?? string.Empty);
            return;
        }

        step.Key = string.IsNullOrWhiteSpace(step.Key) ? "A" : KeyCatalog.Normalize(step.Key);
        if (step.DelayMs is not > 0)
        {
            step.DelayMs = null;
        }
        else
        {
            step.DelayMs = Math.Clamp(step.DelayMs.Value, _settings.MacroMinimumDelayMs, ProfileValidator.MaxMacroDelayMs);
        }
    }

    private int? ParseOptionalDelay(string text)
    {
        var delay = ParseInt(text, 0);
        return delay <= 0 ? null : Math.Clamp(delay, _settings.MacroMinimumDelayMs, ProfileValidator.MaxMacroDelayMs);
    }

    private int ParseRequiredDelay(string text)
    {
        var delay = ParseInt(text, Math.Max(_settings.MacroMinimumDelayMs, 50));
        if (delay <= 0)
        {
            delay = Math.Max(_settings.MacroMinimumDelayMs, 50);
        }

        return Math.Clamp(delay, _settings.MacroMinimumDelayMs, ProfileValidator.MaxMacroDelayMs);
    }

    private void QueueLayoutSizeChanged()
    {
        Dispatcher.BeginInvoke(
            new Action(() => LayoutSizeChanged?.Invoke(this, EventArgs.Empty)),
            DispatcherPriority.Loaded);
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

    private static T? FindVisualParent<T>(DependencyObject? child)
        where T : DependencyObject
    {
        while (child is not null)
        {
            if (child is T parent)
            {
                return parent;
            }

            child = VisualTreeHelper.GetParent(child);
        }

        return null;
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

    public sealed record KeyOption(string Code, string Label);

    private enum CaptureMode
    {
        None,
        Simple,
        Combo
    }
}
