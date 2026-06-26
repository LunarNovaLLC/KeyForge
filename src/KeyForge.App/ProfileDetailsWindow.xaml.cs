using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using KeyForge.Core.Models;
using KeyForge.Storage;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;

namespace KeyForge.App;

public partial class ProfileDetailsWindow : Window
{
    private readonly KeyForgeProfile _profile;
    private readonly IProfileRepository _profileRepository;
    private bool _isLoadingUi;

    public ProfileDetailsWindow(
        KeyForgeProfile profile,
        AppSettings settings,
        IProfileRepository profileRepository,
        KeyForge.Input.IMacroRunner? macroRunner)
    {
        _profile = profile;
        _profileRepository = profileRepository;

        InitializeComponent();
        ModeComboBox.ItemsSource = Enum.GetValues<ProfileMode>();
        RefreshProfileFields();
    }

    public event EventHandler<KeyForgeProfile>? ProfileChanged;

    public string ProfileId => _profile.ProfileId;

    public void RefreshProfile() => RefreshProfileFields();

    private async void ProfileField_Changed(object sender, EventArgs e)
    {
        if (_isLoadingUi)
        {
            return;
        }

        _profile.ProfileName = ProfileNameTextBox.Text.Trim();
        _profile.Target.Exe = TargetExeTextBox.Text.Trim();
        _profile.Target.WindowTitleContains = NullIfWhiteSpace(WindowTitleTextBox.Text);
        _profile.Mode = ModeComboBox.SelectedItem is ProfileMode mode ? mode : ProfileMode.Auto;
        _profile.Enabled = ProfileEnabledCheckBox.IsChecked == true;
        _profile.ModifiedAt = DateTimeOffset.UtcNow;
        await SaveProfileAsync("Profile saved.");
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

        var exeName = Path.GetFileName(dialog.FileName);
        _profile.Target.Exe = exeName;
        _profile.Target.ExecutablePath = dialog.FileName;
        _profile.Target.WindowTitleContains = null;
        if (_profile.ProfileName is "New Profile" or "")
        {
            _profile.ProfileName = Path.GetFileNameWithoutExtension(exeName);
        }

        _profile.IconPath = ProfileAssetService.TryExtractIcon(_profile, dialog.FileName) ?? _profile.IconPath;
        await SaveProfileAsync("Target executable saved.");
    }

    private async void ChooseArtworkButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Choose profile keyboard background",
            Filter = "Images (*.png;*.jpg;*.jpeg;*.bmp;*.webp)|*.png;*.jpg;*.jpeg;*.bmp;*.webp|All files (*.*)|*.*"
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        _profile.ArtworkPath = ProfileAssetService.CopyArtwork(_profile, dialog.FileName);
        await SaveProfileAsync("Keyboard background saved.");
    }

    private void RefreshProfileFields()
    {
        _isLoadingUi = true;
        try
        {
            WindowTitleText.Text = _profile.ProfileName;
            SetTextIfDifferent(ProfileNameTextBox, _profile.ProfileName);
            SetTextIfDifferent(TargetExeTextBox, _profile.Target.Exe);
            SetTextIfDifferent(ExecutablePathTextBox, _profile.Target.ExecutablePath ?? string.Empty);
            SetTextIfDifferent(WindowTitleTextBox, _profile.Target.WindowTitleContains ?? string.Empty);
            ModeComboBox.SelectedItem = _profile.Mode;
            ProfileEnabledCheckBox.IsChecked = _profile.Enabled;
            IconPathText.Text = string.IsNullOrWhiteSpace(_profile.IconPath)
                ? "No icon selected"
                : $"Icon: {Path.GetFileName(_profile.IconPath)}";
            ArtworkPathText.Text = string.IsNullOrWhiteSpace(_profile.ArtworkPath)
                ? "No keyboard background selected"
                : $"Background: {Path.GetFileName(_profile.ArtworkPath)}";
            BindingsCountText.Text = $"{_profile.Bindings.Count} binding{(_profile.Bindings.Count == 1 ? string.Empty : "s")}";
            LoadImage(ProfileIconImage, _profile.IconPath);
            LoadImage(ArtworkPreviewImage, _profile.ArtworkPath);
        }
        finally
        {
            _isLoadingUi = false;
        }
    }

    private async Task SaveProfileAsync(string status)
    {
        await _profileRepository.SaveAsync(_profile);
        RefreshProfileFields();
        StatusText.Text = status;
        ProfileChanged?.Invoke(this, _profile);
    }

    private static void LoadImage(System.Windows.Controls.Image image, string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            image.Source = null;
            return;
        }

        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            bitmap.UriSource = new Uri(path, UriKind.Absolute);
            bitmap.EndInit();
            bitmap.Freeze();
            image.Source = bitmap;
        }
        catch (Exception ex)
        {
            image.Source = null;
            AppLog.Error($"Failed to load profile image '{path}'.", ex);
        }
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

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private static string? NullIfWhiteSpace(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static void SetTextIfDifferent(System.Windows.Controls.TextBox textBox, string value)
    {
        if (!string.Equals(textBox.Text, value, StringComparison.Ordinal))
        {
            textBox.Text = value;
        }
    }
}
