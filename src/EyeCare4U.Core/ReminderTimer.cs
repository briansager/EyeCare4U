namespace EyeCare4U.Core;

/// <summary>
/// A single-threaded countdown. The caller refreshes state; no scheduler or UI dependency.
/// TimeProvider.System timestamps use the monotonic performance counter, not wall-clock time.
/// </summary>
public sealed class ReminderTimer
{
    private readonly TimeProvider _time;
    private TimeSpan _remainingAtStart;
    private long _startedAt;

    public ReminderTimer(TimeProvider? timeProvider = null, int intervalMinutes = 20)
    {
        ValidateInterval(intervalMinutes);
        _time = timeProvider ?? TimeProvider.System;
        IntervalMinutes = intervalMinutes;
        _remainingAtStart = TimeSpan.FromMinutes(intervalMinutes);
    }

    public TimerState State { get; private set; } = TimerState.Ready;
    public int IntervalMinutes { get; private set; }
    public event EventHandler? StateChanged;

    public TimeSpan Remaining
    {
        get
        {
            if (State != TimerState.Running) return _remainingAtStart;
            var remaining = _remainingAtStart - _time.GetElapsedTime(_startedAt, _time.GetTimestamp());
            return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
        }
    }

    public bool SetInterval(int minutes)
    {
        ValidateInterval(minutes);
        if (State != TimerState.Ready) return false;
        IntervalMinutes = minutes;
        _remainingAtStart = TimeSpan.FromMinutes(minutes);
        return true;
    }

    public bool Start()
    {
        Refresh();
        if (State != TimerState.Ready) return false;
        _startedAt = _time.GetTimestamp();
        ChangeState(TimerState.Running);
        return true;
    }

    public bool Pause()
    {
        Refresh();
        if (State != TimerState.Running) return false;
        _remainingAtStart = Remaining;
        ChangeState(TimerState.Paused);
        return true;
    }

    public bool Resume()
    {
        if (State != TimerState.Paused) return false;
        _startedAt = _time.GetTimestamp();
        ChangeState(TimerState.Running);
        return true;
    }

    public void Reset()
    {
        _remainingAtStart = TimeSpan.FromMinutes(IntervalMinutes);
        ChangeState(TimerState.Ready);
    }

    public void Refresh()
    {
        if (State != TimerState.Running || Remaining > TimeSpan.Zero) return;
        _remainingAtStart = TimeSpan.Zero;
        ChangeState(TimerState.Due);
    }

    private void ChangeState(TimerState state)
    {
        if (State == state) return;
        State = state;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private static void ValidateInterval(int minutes)
    {
        if (minutes is < 1 or > 240)
            throw new ArgumentOutOfRangeException(nameof(minutes), "Interval must be 1 through 240 whole minutes.");
    }
}
