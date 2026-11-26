using System.Windows;
using System.Windows.Threading;
using System.Windows.Interop;
using EyeCare4U.App.Platform;
using EyeCare4U.App.ViewModels;
using EyeCare4U.Core;

namespace EyeCare4U.App;

public partial class App : System.Windows.Application
{
    private DispatcherTimer? _displayRefresh;
    private TrayIcon? _tray;
    private MainWindowViewModel? _viewModel;
    private WindowsActivitySource? _activitySource;
    private ActivitySession? _session;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var window = new MainWindow();
        MainWindow = window;
        _activitySource = new WindowsActivitySource(new WindowInteropHelper(window).EnsureHandle());
        _session = new ActivitySession(_activitySource);
        _viewModel = new MainWindowViewModel(_session);
        window.DataContext = _viewModel;
        _activitySource.Signal += OnSessionSignal;
        window.Closing += (_, _) => Cleanup();
        _tray = new TrayIcon(_viewModel, () =>
        {
            window.Show();
            if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
            window.Activate();
        }, Shutdown);
        _displayRefresh = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
        _displayRefresh.Tick += RefreshDisplay;
        _displayRefresh.Start();
        window.Show();
    }

    private void RefreshDisplay(object? sender, EventArgs e)
    {
        _viewModel!.Refresh();
        _tray!.Refresh();
    }

    private void OnSessionSignal(SessionSignal signal)
    {
        // Native window hooks run on this dispatcher; keep the boundary explicit for future adapters.
        if (!Dispatcher.CheckAccess()) { Dispatcher.Invoke(() => OnSessionSignal(signal)); return; }
        _session!.Handle(signal);
        _viewModel!.Refresh();
        _tray?.Refresh();
    }

    private void Cleanup()
    {
        if (_displayRefresh is not null)
        {
            _displayRefresh.Stop();
            _displayRefresh.Tick -= RefreshDisplay;
        }
        if (_activitySource is not null)
        {
            _activitySource.Signal -= OnSessionSignal;
            _activitySource.Dispose();
        }
        _tray?.Dispose();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Cleanup();
        base.OnExit(e);
    }
}
