namespace EyeCare4U.Core;

/// <summary>Detects counter regressions and sleep not accompanied by a delivered notification.</summary>
public sealed class PowerTickTracker
{
    private ulong? _uptime;
    private ulong _awake;
    public void Reset() => _uptime = null;
    public bool TryRead(ulong uptimeMilliseconds, ulong awake100Nanoseconds)
    {
        var before = _uptime;
        var awakeBefore = _awake;
        _uptime = uptimeMilliseconds;
        _awake = awake100Nanoseconds;
        if (before is null) return true;
        if (uptimeMilliseconds < before.Value || awake100Nanoseconds < awakeBefore) return false;
        var elapsed = uptimeMilliseconds - before.Value;
        var awakeElapsed = (awake100Nanoseconds - awakeBefore) / 10_000;
        return elapsed >= awakeElapsed ? elapsed - awakeElapsed <= 100 : awakeElapsed - elapsed <= 100;
    }
}
