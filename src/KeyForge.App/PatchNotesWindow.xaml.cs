using System.Windows;
using System.Windows.Input;

namespace KeyForge.App;

public partial class PatchNotesWindow : Window
{
    public PatchNotesWindow(string version, string notes)
    {
        InitializeComponent();
        HeadingText.Text = $"KeyForge {version}";
        NotesTextBox.Text = string.IsNullOrWhiteSpace(notes)
            ? "No patch notes were included with this build."
            : notes.Trim();
    }

    public bool DownloadRequested { get; private set; }

    private void DownloadButton_Click(object sender, RoutedEventArgs e)
    {
        DownloadRequested = true;
        DialogResult = true;
        Close();
    }

    private void NotNowButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
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
}
