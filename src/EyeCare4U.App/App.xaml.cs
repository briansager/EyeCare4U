using System.ComponentModel;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using EyeCare4U.App.Platform;
using EyeCare4U.App.ViewModels;
using EyeCare4U.Core;
using EyeCare4U.Storage;

namespace EyeCare4U.App;

public partial class App : System.Windows.Application
{
    private DispatcherTimer? _displayRefresh;
    private TrayIcon? _tray;
    private MainWindowViewModel? _viewModel;
    private WindowsActivitySource? _activitySource;
    private ActivitySession? _session;
    private ReminderExperience? _experience;
    private SingleInstanceGuard? _instance;
    private HistoryCollector? _collector;
    private HistoryViewModel? _history;
    private bool _closing;
    private bool _allowClose;
    private long _lastCheckpoint;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try { await StartAsync(e.Args); }
        catch(Exception)
        {
            // No exception messages/paths or activity are written to logs.
            System.Windows.MessageBox.Show("EyeCare4U could not start safely. Check the launch arguments and extracted application files. No monitoring will continue.",
                "EyeCare4U startup failed",MessageBoxButton.OK,MessageBoxImage.Error);
            Cleanup(); Shutdown(1);
        }
    }
    private async Task StartAsync(string[] arguments)
    {
        var directory=AppDataLocation.Resolve(arguments);
        _instance=new SingleInstanceGuard();
        if(!_instance.IsOwner) { Shutdown(); return; }
        var path=System.IO.Path.Combine(directory,"history.db");
        _collector=new HistoryCollector();
        var service=new HistoryService(new SqliteHistoryStore(path),_collector);
        var preferences=await service.InitializeAsync(); // Worker queue; never SQLite on dispatcher.
        var window=new MainWindow(); MainWindow=window;
        if(arguments.Length>0) window.Title="EyeCare4U — isolated review";
        _activitySource=new WindowsActivitySource(new WindowInteropHelper(window).EnsureHandle());
        _session=new ActivitySession(_activitySource,idleThresholdSeconds:preferences.IdleThresholdSeconds);
        _viewModel=new MainWindowViewModel(_session); window.DataContext=_viewModel;
        _tray=new TrayIcon(_viewModel,()=>
        {
            window.Show(); if(window.WindowState==WindowState.Minimized) window.WindowState=WindowState.Normal; window.Activate();
        },()=>window.Close()); // Both Close and tray Exit use the async orderly flush path.
        _experience=new ReminderExperience(_session,_tray.Notifications,new WindowsReminderSounds());
        _viewModel.AttachExperience(_experience); _viewModel.RestorePreferences(preferences);
        _session.Observed+=_collector.Observe;
        if(_session.LastObservation is { } baseline) _collector.Observe(baseline with { Span=TimeSpan.Zero, Windows=[] });
        _experience.Lifecycle+=_collector.Record;
        _activitySource.Signal+=OnSessionSignal;
        _activitySource.TimeMappingChanged+=OnTimeMappingChanged;
        _history=new HistoryViewModel(service,_viewModel.CapturePreferences,path); _viewModel.AttachHistory(_history);
        window.Closing+=OnWindowClosing;
        _lastCheckpoint=TimeProvider.System.GetTimestamp();
        _displayRefresh=new DispatcherTimer(DispatcherPriority.Background){Interval=TimeSpan.FromSeconds(1)};
        _displayRefresh.Tick+=RefreshDisplay; _displayRefresh.Start(); window.Show();
        _ = _history.CheckpointAsync();
    }
    private void RefreshDisplay(object? sender,EventArgs e)
    {
        var checkpoint=TimeProvider.System.GetElapsedTime(_lastCheckpoint)>=TimeSpan.FromSeconds(15);
        if(checkpoint) TimeZoneInfo.ClearCachedData();
        _viewModel!.Refresh(); _tray!.Refresh();
        if(checkpoint) { _lastCheckpoint=TimeProvider.System.GetTimestamp(); _ = _history!.CheckpointAsync(); }
    }
    private void OnTimeMappingChanged() { TimeZoneInfo.ClearCachedData(); _collector?.InvalidateMapping(); }
    private void OnSessionSignal(SessionSignal signal)
    {
        if(!Dispatcher.CheckAccess()) { _ = Dispatcher.InvokeAsync(()=>OnSessionSignal(signal)); return; }
        if(_closing) return;
        _session!.Handle(signal); _viewModel!.Refresh(); _tray?.Refresh();
        if(!_closing && _history is not null) _ = _history.CheckpointAsync();
    }
    private async void OnWindowClosing(object? sender,CancelEventArgs e)
    {
        if(_allowClose) { Cleanup(); return; }
        e.Cancel=true; if(_closing) return; _closing=true;
        _displayRefresh?.Stop();
        _viewModel?.Refresh(); // Final accepted sample and lifecycle transitions.
        StopMonitoring();
        if(sender is Window window) window.IsEnabled=false;
        try
        {
            if(_history is not null)
            {
                await _history.WaitForPendingAsync();
                await _history.CheckpointAsync();
                if(_history.Status.Contains("not being saved",StringComparison.OrdinalIgnoreCase))
                    System.Windows.MessageBox.Show("The final history snapshot could not be saved. Recent activity, events or preference edits may be lost. Existing saved data was preserved.",
                        "EyeCare4U history unavailable",MessageBoxButton.OK,MessageBoxImage.Warning);
            }
        }
        catch(Exception)
        {
            System.Windows.MessageBox.Show("The final history operation failed. Recent unsaved data may be lost. EyeCare4U will close and stop monitoring.",
                "EyeCare4U shutdown",MessageBoxButton.OK,MessageBoxImage.Warning);
        }
        finally { _allowClose=true; MainWindow.Close(); }
    }
    private void StopMonitoring()
    {
        _experience?.Dispose();
        if(_activitySource is not null)
        {
            _activitySource.Signal-=OnSessionSignal; _activitySource.TimeMappingChanged-=OnTimeMappingChanged;
            _activitySource.Dispose();
        }
        if(_session is not null && _collector is not null) _session.Observed-=_collector.Observe;
        if(_experience is not null && _collector is not null) _experience.Lifecycle-=_collector.Record;
    }
    private void Cleanup()
    {
        if(_displayRefresh is not null) { _displayRefresh.Stop(); _displayRefresh.Tick-=RefreshDisplay; }
        StopMonitoring(); _history?.Dispose(); _tray?.Dispose();
    }
    protected override void OnExit(ExitEventArgs e)
    {
        Cleanup(); _instance?.Dispose(); base.OnExit(e);
    }
}
