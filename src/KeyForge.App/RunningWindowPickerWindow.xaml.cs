using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using KeyForge.Core.Models;
using DrawingIcon = System.Drawing.Icon;

namespace KeyForge.App;

public partial class RunningWindowPickerWindow : Window
{
    public RunningWindowPickerWindow(IEnumerable<ActiveWindowInfo> windows)
    {
        InitializeComponent();
        WindowsListBox.ItemsSource = windows.Select(WindowPickerItem.From).ToList();
    }

    public ActiveWindowInfo? SelectedWindow { get; private set; }

    public bool MatchWindowTitle => MatchWindowTitleCheckBox.IsChecked == true;

    private void WindowsListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UseWindowButton.IsEnabled = WindowsListBox.SelectedItem is WindowPickerItem;
    }

    private void WindowsListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        AcceptSelection();
    }

    private void UseWindowButton_Click(object sender, RoutedEventArgs e)
    {
        AcceptSelection();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void AcceptSelection()
    {
        if (WindowsListBox.SelectedItem is not WindowPickerItem selected)
        {
            return;
        }

        SelectedWindow = selected.Info;
        DialogResult = true;
    }

    private sealed class WindowPickerItem
    {
        public required ActiveWindowInfo Info { get; init; }

        public string WindowTitle => Info.WindowTitle;

        public string ExecutableName => Info.ExecutableName;

        public int ProcessId => Info.ProcessId;

        public string ProcessLine => $"{ExecutableName}  PID {ProcessId}";

        public string? ExecutablePath => Info.ExecutablePath;

        public ImageSource? Icon { get; init; }

        public static WindowPickerItem From(ActiveWindowInfo info)
        {
            return new WindowPickerItem
            {
                Info = info,
                Icon = LoadIcon(info.ExecutablePath)
            };
        }

        private static ImageSource? LoadIcon(string? executablePath)
        {
            if (string.IsNullOrWhiteSpace(executablePath) || !System.IO.File.Exists(executablePath))
            {
                return null;
            }

            try
            {
                using var icon = DrawingIcon.ExtractAssociatedIcon(executablePath);
                if (icon is null)
                {
                    return null;
                }

                using var bitmap = icon.ToBitmap();
                using var stream = new System.IO.MemoryStream();
                bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
                stream.Position = 0;
                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.StreamSource = stream;
                image.EndInit();
                image.Freeze();
                return image;
            }
            catch
            {
                return null;
            }
        }
    }
}
