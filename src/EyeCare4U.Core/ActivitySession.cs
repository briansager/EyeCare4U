namespace EyeCare4U.Core;

/// <summary>
/// Single-threaded session accounting. The reminder's clock advances only by credited activity;
/// its explicit states retain user intent even while automatic conditions hold that clock.
/// </summary>
public sealed class ActivitySession
{
    public static readonly TimeSpan MaximumSampleGap = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan SampleTolerance = TimeSpan.FromMilliseconds(250);
    private readonly IActivitySource _source;
    private readonly TimeProvider _time;
    private readonly ActivityClock _activeClock = new();
    private ActivityReading? _previous;
    private long _previousTimestamp;
    private bool _suspended;
    private bool _resumePending;

    private sealed class ActivityClock : TimeProvider
    {
        public long Ticks { get; set; }
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => Ticks;
    }

    public ActivitySession(IActivitySource source, TimeProvider? timeProvider = null)
    {
        _source = source;
        _time = timeProvider ?? TimeProvider.System;
        Timer = new ReminderTimer(_activeClock);
        Poll(); // Launch baseline; never reconstruct activity before this sample.
    }

    public ReminderTimer Timer { get; }
    public ReminderTimer CreateActivityTimer() => new(_activeClock);
    public long ReliabilityVersion { get; private set; }
    public int IdleThresholdSeconds { get; private set; } = 120;
    public TimeSpan EstimatedActiveTime => TimeSpan.FromTicks(_activeClock.Ticks);
    public ActivityStatus Status { get; private set; } = ActivityStatus.Unavailable;
    public string MonitoringNote { get; private set; } = "";
    public string ResumeNotice { get; private set; } = "";
    public bool IsHeld => Timer.State == TimerState.Running && Status != ActivityStatus.Active;

    public bool SetIdleThreshold(int seconds)
    {
        if (seconds is < 30 or > 900) throw new ArgumentOutOfRangeException(nameof(seconds));
        if (Timer.State != TimerState.Ready) return false;
        Poll(); // Settle the old policy before applying the new one prospectively.
        IdleThresholdSeconds = seconds;
        _previous = null;
        Poll();
        return true;
    }

    public void Handle(SessionSignal signal)
    {
        if (signal == SessionSignal.Suspending)
        {
            if (_suspended) return;
            _suspended = true;
            Status = ActivityStatus.PowerTransition;
        }
        else if (signal == SessionSignal.Resumed)
        {
            if (!_suspended && _resumePending) return;
            _suspended = false;
            _resumePending = true;
            Status = ActivityStatus.Unavailable;
            ResumeNotice = "Resume handled: fresh activity and timing baselines; sleep time excluded.";
        }
        else Status = ActivityStatus.Unavailable;
        ReliabilityVersion++;
        // Discard the unresolved boundary interval; never credit time across lifecycle events.
        _previous = null;
    }

    public void Poll()
    {
        if (_suspended) return;
        var reading = _source.Read();
        var now = _time.GetTimestamp(); // Include sampling latency in gap detection.
        if (!reading.Available || reading.IdleFor < TimeSpan.Zero || reading.IdleFor > TimeSpan.FromDays(1))
        {
            Unavailable("Activity or session state could not be read. Retrying; unknown time excluded.");
            return;
        }

        if (_previous is { } previous)
        {
            var elapsed = _time.GetElapsedTime(_previousTimestamp, now);
            if (elapsed < TimeSpan.Zero || elapsed > MaximumSampleGap)
            {
                Unavailable("Monitoring gap over 5 seconds or clock discontinuity; gap excluded. Retrying.");
                return;
            }
            if (reading.InputTimestamp < previous.InputTimestamp ||
                (reading.InputTimestamp == previous.InputTimestamp &&
                 (reading.IdleFor - previous.IdleFor - elapsed).Duration() > SampleTolerance) ||
                (reading.InputTimestamp != previous.InputTimestamp && reading.IdleFor > elapsed + SampleTolerance))
            {
                Unavailable("Discontinuous input timing; unknown time excluded. Retrying.");
                return;
            }
            if (!previous.Locked && !reading.Locked)
            {
                var threshold = TimeSpan.FromSeconds(IdleThresholdSeconds);
                var oldEnd = Clamp(threshold - previous.IdleFor, elapsed);
                var credited = oldEnd;
                if (reading.InputTimestamp != previous.InputTimestamp)
                {
                    // Union of the old grace window and the window beginning at the latest input.
                    // Unobserved earlier inputs are not invented or reconstructed.
                    var start = Clamp(elapsed - reading.IdleFor, elapsed);
                    var end = Clamp(elapsed - reading.IdleFor + threshold, elapsed);
                    var overlapEnd = oldEnd > start ? oldEnd : start;
                    if (end > overlapEnd) credited += end - overlapEnd;
                }
                _activeClock.Ticks += credited.Ticks;
            }
        }
        if (_previous is { } last && last.Locked != reading.Locked) ReliabilityVersion++;
        _previous = reading;
        _previousTimestamp = now;
        _resumePending = false;
        Status = reading.Locked ? ActivityStatus.Locked :
            reading.IdleFor >= TimeSpan.FromSeconds(IdleThresholdSeconds) ? ActivityStatus.Idle : ActivityStatus.Active;
        MonitoringNote = "";
        Timer.Refresh();
    }

    private void Unavailable(string reason)
    {
        ReliabilityVersion++;
        _previous = null;
        Status = ActivityStatus.Unavailable;
        MonitoringNote = reason;
    }

    private static TimeSpan Clamp(TimeSpan value, TimeSpan upper) =>
        value < TimeSpan.Zero ? TimeSpan.Zero : value > upper ? upper : value;
}
