namespace EyeCare4U.Core;

public enum ReminderMode { Interval, Snooze, Break }
public enum BreakState { None, Running, Finished, Cancelled, Interrupted }
public enum SoundCue { Reminder, BreakStart, BreakEnd }

public interface IReminderNotifications : IDisposable
{
    void ShowDue();
    void Clear();
}
public interface IReminderSounds : IDisposable
{
    void Play(SoundCue cue);
    void Stop();
}

/// <summary>Single-threaded, polled reminder experience. No scheduled completion callbacks.</summary>
public sealed class ReminderExperience : IDisposable
{
    private readonly ActivitySession _session;
    private readonly TimeProvider _time;
    private readonly IReminderNotifications _notifications;
    private readonly IReminderSounds _sounds;
    private readonly ReminderTimer _snooze;
    private bool _normalWasDue;
    private bool _alertAttempted;
    private bool _wasAvailable;
    private bool _disposed;
    private long _breakStart;
    private long _breakLastPoll;
    private long _breakReliability;

    public ReminderExperience(ActivitySession session, IReminderNotifications notifications,
        IReminderSounds sounds, TimeProvider? timeProvider = null)
    {
        _session = session;
        _notifications = notifications;
        _sounds = sounds;
        _time = timeProvider ?? TimeProvider.System;
        _snooze = session.CreateActivityTimer();
        _wasAvailable = SessionAvailable;
    }

    public ReminderMode Mode { get; private set; }
    public BreakState BreakState { get; private set; }
    public long DueOccurrence { get; private set; }
    private bool _notificationsEnabled = true;
    private bool _reminderSoundEnabled;
    private bool _breakSoundsEnabled;
    public bool NotificationsEnabled
    {
        get => _notificationsEnabled;
        set { if (_disposed || value == _notificationsEnabled) return; _notificationsEnabled = value; if (!value) Effect(_notifications.Clear, "Pending notification could not be cleared."); }
    }
    public bool ReminderSoundEnabled
    {
        get => _reminderSoundEnabled;
        set { if (_disposed || value == _reminderSoundEnabled) return; _reminderSoundEnabled = value; if (!value) Effect(_sounds.Stop, "Sound could not be stopped."); }
    }
    public bool BreakSoundsEnabled
    {
        get => _breakSoundsEnabled;
        set { if (_disposed || value == _breakSoundsEnabled) return; _breakSoundsEnabled = value; if (!value) Effect(_sounds.Stop, "Sound could not be stopped."); }
    }
    public string Result { get; private set; } = "";
    public string DeliveryNote { get; private set; } = "";
    public bool SessionAvailable => _session.Status is ActivityStatus.Active or ActivityStatus.Idle;
    public bool CanResolveDue => !_disposed && Mode == ReminderMode.Interval && _session.Timer.State == TimerState.Due;
    public bool CanStartBreak => CanResolveDue && SessionAvailable;
    public bool CanCancelBreak => !_disposed && Mode == ReminderMode.Break;
    public bool CanEditInterval => !_disposed && Mode == ReminderMode.Interval && _session.Timer.State == TimerState.Ready;
    public bool CanPause => !_disposed && (Mode == ReminderMode.Snooze ? _snooze.State == TimerState.Running : Mode == ReminderMode.Interval && _session.Timer.State == TimerState.Running);
    public bool CanResume => !_disposed && (Mode == ReminderMode.Snooze ? _snooze.State == TimerState.Paused : Mode == ReminderMode.Interval && _session.Timer.State == TimerState.Paused);
    public bool CanReset => !_disposed && (Mode != ReminderMode.Interval || _session.Timer.State != TimerState.Ready);
    public TimerState SnoozeState => _snooze.State;
    public TimeSpan Remaining => Mode switch
    {
        ReminderMode.Snooze => _snooze.Remaining,
        ReminderMode.Break => TimeSpan.FromSeconds(Math.Max(0, 20 - _time.GetElapsedTime(_breakStart, _time.GetTimestamp()).TotalSeconds)),
        _ => _session.Timer.Remaining
    };
    public string Phase => Mode switch
    {
        ReminderMode.Break => "Break running",
        ReminderMode.Snooze => _snooze.State == TimerState.Paused ? "Snooze paused by you" : "Snoozing",
        _ => _session.Timer.State.ToString()
    };

    public void Refresh() => Synchronize(true);

    private void Synchronize(bool deliver)
    {
        if (_disposed) return;
        _session.Poll();
        var available = SessionAvailable;
        if (!available && _wasAvailable) StopEffects();
        _wasAvailable = available;
        if (Mode == ReminderMode.Break)
        {
            var now = _time.GetTimestamp();
            var gap = _time.GetElapsedTime(_breakLastPoll, now);
            if (!available || _breakReliability != _session.ReliabilityVersion || gap < TimeSpan.Zero || gap > ActivitySession.MaximumSampleGap)
            {
                BreakState = BreakState.Interrupted;
                Mode = ReminderMode.Interval;
                var reason = gap < TimeSpan.Zero || gap > ActivitySession.MaximumSampleGap ? "unexplained timing gap" :
                    _session.Status == ActivityStatus.Locked ? "session locked" :
                    _session.Status == ActivityStatus.PowerTransition ? "power transition" :
                    _session.Status == ActivityStatus.Unavailable ? "monitoring unavailable" : "session or monitoring baseline changed";
                Result = $"Break interrupted: {reason}. Reminder remains due.";
                StopEffects();
            }
            else if (_time.GetElapsedTime(_breakStart, now) >= TimeSpan.FromSeconds(20))
            {
                BreakState = BreakState.Finished;
                Mode = ReminderMode.Interval;
                _session.Timer.Reset();
                _normalWasDue = false;
                Result = "Break timer finished.";
                if (BreakSoundsEnabled) Effect(() => _sounds.Play(SoundCue.BreakEnd), "Break end sound could not be played.");
            }
            _breakLastPoll = now;
        }
        if (Mode == ReminderMode.Snooze)
        {
            _snooze.Refresh();
            if (_snooze.State == TimerState.Due)
            {
                Mode = ReminderMode.Interval;
                NewOccurrence();
            }
        }
        var due = _session.Timer.State == TimerState.Due;
        if (due && !_normalWasDue && Mode == ReminderMode.Interval) NewOccurrence();
        _normalWasDue = due;
        if (deliver && CanResolveDue && !_alertAttempted && _session.Status == ActivityStatus.Active)
        {
            _alertAttempted = true; // Even failed or disabled effects must not loop/replay.
            if (NotificationsEnabled) Effect(_notifications.ShowDue, "System notification could not be requested. Use the window or tray.");
            if (ReminderSoundEnabled) Effect(() => _sounds.Play(SoundCue.Reminder), "Reminder sound could not be played.");
        }
    }

    private void NewOccurrence() { DueOccurrence++; _alertAttempted = false; DeliveryNote = ""; }
    public bool Start()
    {
        if (!CanEditInterval) return false;
        Synchronize(false);
        Result = ""; BreakState = BreakState.None;
        return _session.Timer.Start();
    }
    public bool Pause() { if (!CanPause) return false; Synchronize(false); return Mode == ReminderMode.Snooze ? _snooze.Pause() : _session.Timer.Pause(); }
    public bool Resume() { if (!CanResume) return false; Synchronize(false); return Mode == ReminderMode.Snooze ? _snooze.Resume() : _session.Timer.Resume(); }
    public bool StartBreak()
    {
        Synchronize(false);
        if (!CanStartBreak) return false;
        StopEffects(); _alertAttempted = true;
        _snooze.Reset();
        Mode = ReminderMode.Break; BreakState = BreakState.Running; Result = "";
        _breakStart = _breakLastPoll = _time.GetTimestamp();
        _breakReliability = _session.ReliabilityVersion;
        if (BreakSoundsEnabled) Effect(() => _sounds.Play(SoundCue.BreakStart), "Break start sound could not be played.");
        return true;
    }
    public bool CancelBreak()
    {
        if (!CanCancelBreak) return false;
        StopEffects(); Mode = ReminderMode.Interval; BreakState = BreakState.Cancelled;
        Result = "Break cancelled. Reminder remains due.";
        return true;
    }
    public bool Snooze(int minutes)
    {
        if (minutes is not (1 or 5 or 10)) return false;
        Synchronize(false);
        if (!CanResolveDue) return false;
        StopEffects(); _alertAttempted = true; Result = ""; BreakState = BreakState.None;
        _snooze.Reset(); _snooze.SetInterval(minutes); _snooze.Start(); Mode = ReminderMode.Snooze;
        return true;
    }
    public void Reset()
    {
        if (_disposed) return;
        StopEffects(); Mode = ReminderMode.Interval; BreakState = BreakState.None; Result = ""; DeliveryNote = "";
        _snooze.Reset(); _session.Timer.Reset(); _normalWasDue = false; _alertAttempted = true;
    }
    private void Effect(Action action, string failure)
    {
        try { action(); } catch (Exception) { DeliveryNote = failure; }
    }
    private void StopEffects()
    {
        Effect(_notifications.Clear, "Pending system notification could not be cleared.");
        Effect(_sounds.Stop, "Sound could not be stopped.");
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; StopEffects();
        Effect(_notifications.Dispose, "Notification cleanup failed.");
        Effect(_sounds.Dispose, "Sound cleanup failed.");
    }
}
