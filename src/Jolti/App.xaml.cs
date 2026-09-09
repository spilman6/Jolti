using System.Drawing;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Jolti.Core;
using Jolti.Infrastructure;
using Jolti.Services;
using Jolti.ViewModels;
using Jolti.Views;
using Forms = System.Windows.Forms;
namespace Jolti;
public partial class App : Application
{
    private ServiceProvider? _services;
    private Forms.NotifyIcon? _tray;
    private Icon? _trayIcon;
    private Mutex? _instance;
    private bool _ownsMutex;
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _instance = new Mutex(true, "Jolti.Desktop.SingleInstance", out _ownsMutex);
        if (!_ownsMutex) { MessageBox.Show("Jolti is already running. Open it from the system tray.", "Jolti"); Shutdown(); return; }
        var services = new ServiceCollection();
        services.AddSingleton<LocalStorage>();
        services.AddSingleton<ISettingsStore>(s => s.GetRequiredService<LocalStorage>());
        services.AddSingleton<IHistoryRepository>(s => s.GetRequiredService<LocalStorage>());
        services.AddSingleton<IDictionaryRepository>(s => s.GetRequiredService<LocalStorage>());
        services.AddSingleton<IAudioRecorder, AudioRecorder>();
        services.AddSingleton<IRecordingSoundService, RecordingSoundService>();
        services.AddSingleton<ITranscriptionService, WhisperTranscriptionService>();
        services.AddSingleton<ITextCleanupService, TextCleanupService>();
        services.AddSingleton<ITextPaster, TextPaster>();
        services.AddSingleton<IHotkeyService, HotkeyService>();
        services.AddSingleton<MainViewModel>(); services.AddSingleton<MainWindow>(); services.AddSingleton<RecordingIndicator>();
        _services = services.BuildServiceProvider();
        var model = _services.GetRequiredService<MainViewModel>();
        var window = _services.GetRequiredService<MainWindow>(); MainWindow = window;
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Start/Stop Dictation", null, (_, _) => Dispatcher.Invoke(model.Toggle));
        menu.Items.Add("Settings", null, (_, _) => Dispatcher.Invoke(window.ShowSettings));
        menu.Items.Add("Open Jolti", null, (_, _) => Dispatcher.Invoke(window.RestoreFromTray));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => Dispatcher.Invoke(Shutdown));
        var iconResource = GetResourceStream(new Uri("pack://application:,,,/Jolti;component/Assets/Jolti.ico"));
        using (var iconStream = iconResource.Stream)
        using (var sourceIcon = new Icon(iconStream, 32, 32))
            _trayIcon = (Icon)sourceIcon.Clone();
        _tray = new Forms.NotifyIcon { Icon = _trayIcon, Text = "Jolti · Idle", Visible = true, ContextMenuStrip = menu };
        _tray.MouseClick += (_, args) =>
        {
            if (args.Button == Forms.MouseButtons.Left) Dispatcher.Invoke(window.RestoreFromTray);
        };
        var explainedTray = false;
        window.HiddenToTray += (_, _) =>
        {
            _tray.Visible = true;
            if (explainedTray) return;
            explainedTray = true;
            _tray.ShowBalloonTip(4000, "Jolti is still running",
                "Use your hotkey to dictate. Find Jolti in the tray or hidden icons (^). Right-click and choose Exit to quit.",
                Forms.ToolTipIcon.Info);
        };
        model.PropertyChanged += (_, args) => { if (args.PropertyName == nameof(model.Status) && _tray != null) _tray.Text = "Jolti · " + model.Status; };
        window.Show(); _services.GetRequiredService<RecordingIndicator>().Show(); model.Initialize();
    }
    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose(); _tray = null;
        _trayIcon?.Dispose(); _trayIcon = null;
        _services?.Dispose();
        if (_ownsMutex) _instance?.ReleaseMutex();
        _instance?.Dispose(); base.OnExit(e);
    }
}
