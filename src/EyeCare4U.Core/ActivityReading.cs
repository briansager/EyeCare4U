namespace EyeCare4U.Core;

// Only input timing and session lock state cross the platform boundary. No input contents.
public readonly record struct ActivityReading(bool Available, TimeSpan IdleFor, ulong InputTimestamp, bool Locked)
{
    public static ActivityReading Unavailable => default;
}

public interface IActivitySource
{
    ActivityReading Read();
}

public enum ActivityStatus { Unavailable, Active, Idle, Locked, PowerTransition }
public enum SessionSignal { SessionChanged, Suspending, Resumed }
