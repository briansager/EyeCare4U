using System.ComponentModel;
using System.Runtime.CompilerServices;
using EyeCare4U.Core;

namespace EyeCare4U.App.ViewModels;

public sealed class MainWindowViewModel : INotifyPropertyChanged
{
    private readonly ReminderTimer _timer;
    private readonly ActivitySession? _session;
    private ReminderExperience? _experience;
    private string _lastExperienceKey = "";
    private string _lastResult = "";
    private string _lastDelivery = "";
    private int _selectedSnooze = 5;
    private string _idleThresholdText = "120";
    private string _lastStatus = "";
    private string _lastActivityStatus = "";
    private string _lastEstimatedTime = "";
    private string _lastMonitoringNote = "";
    private string _lastResumeNotice = "";
    private string _selectedInterval = "20";
    private string _customMinutes = "20";
    private int _lastValidCustomMinutes = 20;
    public HistoryViewModel? History { get; private set; }
    public void AttachHistory(HistoryViewModel history) { History=history; Changed(nameof(History)); }
    private TimerState _lastState = TimerState.Ready;
    private string _lastRemaining = "";

    public MainWindowViewModel(ReminderTimer timer)
    {
        _timer = timer;
        StartCommand = new(() => Run(() => { if (_experience is not null) _experience.Start(); else _timer.Start(); }), () => CanEditInterval && ValidationError.Length == 0 && ThresholdValidationError.Length == 0);
        PauseCommand = new(() => Run(() => { if (_experience is not null) _experience.Pause(); else _timer.Pause(); }), () => _experience?.CanPause ?? State == TimerState.Running);
        ResumeCommand = new(() => Run(() => { if (_experience is not null) _experience.Resume(); else _timer.Resume(); }), () => _experience?.CanResume ?? State == TimerState.Paused);
        ResetCommand = new(() => Run(() => { if (_experience is not null) _experience.Reset(); else _timer.Reset(); ApplyInterval(); }), () => _experience?.CanReset ?? State != TimerState.Ready);
        StartBreakCommand = new(() => Run(() => _experience!.StartBreak()), () => _experience?.CanStartBreak == true);
        CancelBreakCommand = new(() => Run(() => _experience!.CancelBreak()), () => _experience?.CanCancelBreak == true);
        SnoozeCommand = new(() => Run(() => _experience!.Snooze(SelectedSnooze)), () => CanSnooze);
        Refresh();
    }

    public MainWindowViewModel(ActivitySession session) : this(session.Timer)
    {
        _session = session;
        _idleThresholdText = session.IdleThresholdSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
        Refresh();
    }

    public UserPreferences CapturePreferences()=>new UserPreferences
    {
        SelectedInterval=SelectedInterval,CustomMinutes=_lastValidCustomMinutes,
        IdleThresholdSeconds=_session?.IdleThresholdSeconds??120,NotificationsEnabled=NotificationsEnabled,
        ReminderSoundEnabled=ReminderSoundEnabled,BreakSoundsEnabled=BreakSoundsEnabled,SnoozeMinutes=SelectedSnooze
    }.Validated();
    public void RestorePreferences(UserPreferences stored)
    {
        if(!CanEditInterval) throw new InvalidOperationException("Preferences load only into Ready.");
        var p=stored.Validated();
        CustomMinutes=p.CustomMinutes.ToString(System.Globalization.CultureInfo.InvariantCulture);
        SelectedInterval=p.SelectedInterval;
        IdleThresholdText=p.IdleThresholdSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
        NotificationsEnabled=p.NotificationsEnabled; ReminderSoundEnabled=p.ReminderSoundEnabled;
        BreakSoundsEnabled=p.BreakSoundsEnabled; SelectedSnooze=p.SnoozeMinutes;
        Refresh();
    }
    public void AttachExperience(ReminderExperience experience)
    {
        if (_experience is not null) throw new InvalidOperationException("An experience is already attached.");
        _experience = experience; Refresh();
    }
    public RelayCommand StartBreakCommand { get; }
    public RelayCommand CancelBreakCommand { get; }
    public RelayCommand SnoozeCommand { get; }
    public IReadOnlyList<int> SnoozeOptions { get; } = [1, 5, 10];
    public bool CanSnooze => _experience?.CanResolveDue == true;
    public int SelectedSnooze
    {
        get => _selectedSnooze;
        set { if (value is not (1 or 5 or 10)) return; _selectedSnooze = value; Changed(); }
    }
    public bool NotificationsEnabled
    {
        get => _experience?.NotificationsEnabled ?? true;
        set { if (_experience is null) return; _experience.NotificationsEnabled = value; Changed(); }
    }
    public bool ReminderSoundEnabled
    {
        get => _experience?.ReminderSoundEnabled ?? false;
        set { if (_experience is null) return; _experience.ReminderSoundEnabled = value; Changed(); }
    }
    public bool BreakSoundsEnabled
    {
        get => _experience?.BreakSoundsEnabled ?? false;
        set { if (_experience is null) return; _experience.BreakSoundsEnabled = value; Changed(); }
    }
    public string ExperienceResult => _experience?.Result ?? "";
    public string DeliveryNote => _experience?.DeliveryNote ?? "";
    public string BreakGuidance => _experience?.Mode == ReminderMode.Break
        ? "Look away from the screen at something about 6 metres away." : "";
    public string CountdownLabel => _experience?.Mode switch
    {
        ReminderMode.Break => "Break remaining",
        ReminderMode.Snooze => "Snooze remaining — estimated active time",
        _ => "Remaining interval time"
    };
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
    public string AppliedIdleThreshold => $"Applied idle threshold: {_session?.IdleThresholdSeconds ?? 120} seconds. Saved at checkpoints.";
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
    public bool CanEditInterval => _experience?.CanEditInterval ?? State == TimerState.Ready;
    public bool IsCustom => SelectedInterval == "Custom";
    public string Status => _experience?.Mode == ReminderMode.Break ? "Eye break timer running" :
        _experience?.Mode == ReminderMode.Snooze ? (_experience.SnoozeState == TimerState.Paused ? "Snooze paused by you" :
            _session?.Status == ActivityStatus.Active ? "Snoozing — estimated active time" : $"Snooze waiting — {ActivityStatusText}") : State switch
    {
        TimerState.Ready => "Ready · Timer not started",
        TimerState.Running => _session?.IsHeld == true ? HoldStatus : "Running",
        TimerState.Paused => "Paused by you",
        TimerState.Due => "Time for an eye break",
        _ => throw new InvalidOperationException()
    };
    private string HoldStatus => _session?.Status == ActivityStatus.Idle ? "Waiting for activity" : $"Waiting — {ActivityStatusText}";
    public string StateName => _experience?.Phase ?? State.ToString();
    public string RemainingText
    {
        get
        {
            if (CanEditInterval && ValidationError.Length > 0) return "--:--";
            // Ceiling avoids displaying 00:00 before the actual deadline.
            var seconds = (int)Math.Ceiling((_experience?.Remaining ?? _timer.Remaining).TotalSeconds);
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
            if(IntervalInput.TryParse(value,out var valid)) _lastValidCustomMinutes=valid;
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
        if (_experience is not null) _experience.Refresh(); else _session?.Poll();
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
        var key = $"{_experience?.Mode}/{_experience?.BreakState}/{_experience?.SnoozeState}/{CanSnooze}";
        if (_lastExperienceKey != key)
        {
            _lastExperienceKey = key; Changed(nameof(StateName)); Changed(nameof(CountdownLabel));
            Changed(nameof(CanSnooze)); Changed(nameof(BreakGuidance)); Changed(nameof(CanEditInterval));
        }
        if (_lastResult != ExperienceResult) { _lastResult = ExperienceResult; Changed(nameof(ExperienceResult)); }
        if (_lastDelivery != DeliveryNote) { _lastDelivery = DeliveryNote; Changed(nameof(DeliveryNote)); }
        var remaining = RemainingText;
        if (_lastRemaining != remaining)
        {
            _lastRemaining = remaining;
            Changed(nameof(RemainingText));
        }
        StartCommand.NotifyCanExecuteChanged(); PauseCommand.NotifyCanExecuteChanged();
        ResumeCommand.NotifyCanExecuteChanged(); ResetCommand.NotifyCanExecuteChanged();
        StartBreakCommand.NotifyCanExecuteChanged(); CancelBreakCommand.NotifyCanExecuteChanged(); SnoozeCommand.NotifyCanExecuteChanged();
    }

    private void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
}
