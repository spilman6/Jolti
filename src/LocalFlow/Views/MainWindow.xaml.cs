using System.ComponentModel;
using System.Windows;
using LocalFlow.ViewModels;
namespace LocalFlow.Views;
public partial class MainWindow : Window
{
    public MainWindow(MainViewModel viewModel) { InitializeComponent(); DataContext = viewModel; }
    private void OpenSettings(object sender, RoutedEventArgs e) => Tabs.SelectedIndex = 1;
    public event EventHandler? HiddenToTray;
    private void HideApp(object sender, RoutedEventArgs e) => Close();
    public void RestoreFromTray() { Show(); WindowState = WindowState.Normal; Activate(); }
    private void BrowseModel(object sender, RoutedEventArgs e)
    {
        // The dialog is view-only behavior; its selected path remains bindable view-model state.
        var dialog = new Microsoft.Win32.OpenFileDialog { Title = "Choose the verified AppData Whisper model", InitialDirectory = Infrastructure.VerifiedModel.DirectoryPath, Filter = "Whisper models (*.bin)|*.bin", CheckFileExists = true };
        if (dialog.ShowDialog(this) == true && DataContext is MainViewModel model) model.ModelPath = dialog.FileName;
    }
    protected override void OnClosing(CancelEventArgs e)
    {
        // The tray owns the application lifetime; closing this window only hides it.
        // Application.Shutdown (tray Exit) still closes all windows regardless of cancellation.
        base.OnClosing(e);
        e.Cancel = true;
        Hide();
        HiddenToTray?.Invoke(this, EventArgs.Empty);
    }
    public void ShowSettings() { Tabs.SelectedIndex = 1; RestoreFromTray(); }
}
