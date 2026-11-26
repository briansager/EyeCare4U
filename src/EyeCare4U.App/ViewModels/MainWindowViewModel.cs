using System.ComponentModel;
using System.Runtime.CompilerServices;
using EyeCare4U.Core;

namespace EyeCare4U.App.ViewModels;

public sealed class MainWindowViewModel : INotifyPropertyChanged
{
    private readonly ReminderTimer _timer;
    private readonly ActivitySession? _session;
    private string _idleThresholdText = "120";
    private string _lastStatus = "";
    private string _lastActivityStatus = "";
    private string _lastEstimatedTime = "";
    private string _lastMonitoringNote = "";
    private string _lastResumeNotice = "";
    private string _selectedInterval = "20";
    private string _customMinutes = "20";
    private TimerState _lastState = TimerState.Ready;
    private string _lastRemaining = "";

    public MainWindowViewModel(ReminderTimer timer)
    {
        _timer = timer;
        StartCommand = new(() => Run(() => _timer.Start()), () => CanEditInterval && ValidationError.Length == 0 && ThresholdValidationError.Length == 0);
        PauseCommand = new(() => Run(() => _timer.Pause()), () => State == TimerState.Running);
        ResumeCommand = new(() => Run(() => _timer.Resume()), () => State == TimerState.Paused);
        ResetCommand = new(() => Run(() => { _timer.Reset(); ApplyInterval(); }), () => State != TimerState.Ready);
        Refresh();
    }

    public MainWindowViewModel(ActivitySession session) : this(session.Timer)
    {
        _session = session;
        _idleThresholdText = session.IdleThresholdSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
        Refresh();
    }

    // Settle monitoring before changing user intent so pre-Start / post-Pause time cannot leak.
    private void Run(Action action) { _session?.Poll(); action(); Refresh(); }

    public string EstimatedTimeText
    {
        get
        {
            var span = _session?.EstimatedActiveTime ?? TimeSpan.Zero;
            return $"{(long)span.TotalHours:00}:{span.Minutes:00}:{span.Seconds:00}";
        }
    }
    public string ActivityStatusText => _session?.Status switch
    {
        ActivityStatus.Active => "Active — estimated from recent input.",
        ActivityStatus.Idle => "Waiting for activity.",
        ActivityStatus.Locked => "Session locked.",
        ActivityStatus.PowerTransition => "Monitoring held for a power transition.",
        _ => "Monitoring unavailable."
    };
    public string MonitoringNote => _session?.MonitoringNote ?? "";
    public string ResumeNotice => _session?.ResumeNotice ?? "";
    public string ThresholdValidationError => IdleThresholdInput.TryParse(IdleThresholdText, out _) ? "" : IdleThresholdInput.ErrorMessage;
    public string AppliedIdleThreshold => $"Applied idle threshold: {_session?.IdleThresholdSeconds ?? 120} seconds. Session only.";
    public string IdleThresholdText
    {
        get => _idleThresholdText;
        set
        {
            if (!CanEditInterval || value == _idleThresholdText) return;
            _idleThresholdText = value;
            Changed(); Changed(nameof(ThresholdValidationError));
            if (IdleThresholdInput.TryParse(value, out var seconds)) _session?.SetIdleThreshold(seconds);
            Changed(nameof(AppliedIdleThreshold)); Refresh();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public RelayCommand StartCommand { get; }
    public RelayCommand PauseCommand { get; }
    public RelayCommand ResumeCommand { get; }
    public RelayCommand ResetCommand { get; }
    public IReadOnlyList<string> IntervalOptions { get; } = ["20", "30", "60", "Custom"];
    public TimerState State => _timer.State;
    public bool CanEditInterval => State == TimerState.Ready;
    public bool IsCustom => SelectedInterval == "Custom";
    public string Status => State switch
    {
        TimerState.Ready => "Ready · Timer not started",
        TimerState.Running => _session?.IsHeld == true ? HoldStatus : "Running",
        TimerState.Paused => "Paused by you",
        TimerState.Due => "Time for an eye break",
        _ => throw new InvalidOperationException()
    };
    private string HoldStatus => _session?.Status == ActivityStatus.Idle ? "Waiting for activity" : $"Waiting — {ActivityStatusText}";
    public string StateName => State.ToString();
    public string RemainingText
    {
        get
        {
            if (CanEditInterval && ValidationError.Length > 0) return "--:--";
            // Ceiling avoids displaying 00:00 before the actual deadline.
            var seconds = (int)Math.Ceiling(_timer.Remaining.TotalSeconds);
            return $"{seconds / 60:00}:{seconds % 60:00}";
        }
    }
    public string ValidationError => !IntervalInput.TryParse(IsCustom ? CustomMinutes : SelectedInterval, out _)
        ? IntervalInput.ErrorMessage : "";
    public string SelectedInterval
    {
        get => _selectedInterval;
        set
        {
            if (!CanEditInterval || !IntervalOptions.Contains(value) || value == _selectedInterval) return;
            _selectedInterval = value;
            Changed(); Changed(nameof(IsCustom));
            ApplyInterval(); Refresh();
        }
    }
    public string CustomMinutes
    {
        get => _customMinutes;
        set
        {
            if (!CanEditInterval || value == _customMinutes) return;
            _customMinutes = value;
            Changed(); ApplyInterval(); Refresh();
        }
    }

    private void ApplyInterval()
    {
        if (CanEditInterval && IntervalInput.TryParse(IsCustom ? CustomMinutes : SelectedInterval, out var minutes))
            _timer.SetInterval(minutes);
        Changed(nameof(ValidationError));
    }

    public void Refresh()
    {
        _session?.Poll();
        _timer.Refresh();
        if (_lastState != State)
        {
            _lastState = State;
            Changed(nameof(State)); Changed(nameof(StateName)); Changed(nameof(CanEditInterval));
        }
        if (_lastStatus != Status) { _lastStatus = Status; Changed(nameof(Status)); }
        if (_lastActivityStatus != ActivityStatusText) { _lastActivityStatus = ActivityStatusText; Changed(nameof(ActivityStatusText)); }
        if (_lastEstimatedTime != EstimatedTimeText) { _lastEstimatedTime = EstimatedTimeText; Changed(nameof(EstimatedTimeText)); }
        if (_lastMonitoringNote != MonitoringNote) { _lastMonitoringNote = MonitoringNote; Changed(nameof(MonitoringNote)); }
        if (_lastResumeNotice != ResumeNotice) { _lastResumeNotice = ResumeNotice; Changed(nameof(ResumeNotice)); }
        var remaining = RemainingText;
        if (_lastRemaining != remaining)
        {
            _lastRemaining = remaining;
            Changed(nameof(RemainingText));
        }
        StartCommand.NotifyCanExecuteChanged(); PauseCommand.NotifyCanExecuteChanged();
        ResumeCommand.NotifyCanExecuteChanged(); ResetCommand.NotifyCanExecuteChanged();
    }

    private void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
}
