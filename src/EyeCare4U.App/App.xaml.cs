using System.Windows;
using System.Windows.Threading;
using EyeCare4U.App.Platform;
using EyeCare4U.App.ViewModels;
using EyeCare4U.Core;

namespace EyeCare4U.App;

public partial class App : System.Windows.Application
{
    private DispatcherTimer? _displayRefresh;
    private TrayIcon? _tray;
    private MainWindowViewModel? _viewModel;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _viewModel = new MainWindowViewModel(new ReminderTimer());
        var window = new MainWindow { DataContext = _viewModel };
        MainWindow = window;
        _tray = new TrayIcon(_viewModel, () =>
        {
            window.Show();
            if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
            window.Activate();
        }, Shutdown);
        _displayRefresh = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(250) };
        _displayRefresh.Tick += RefreshDisplay;
        _displayRefresh.Start();
        window.Show();
    }

    private void RefreshDisplay(object? sender, EventArgs e)
    {
        _viewModel!.Refresh();
        _tray!.Refresh();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_displayRefresh is not null)
        {
            _displayRefresh.Stop();
            _displayRefresh.Tick -= RefreshDisplay;
        }
        _tray?.Dispose();
        base.OnExit(e);
    }
}
