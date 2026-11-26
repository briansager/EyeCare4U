using EyeCare4U.Core;

namespace EyeCare4U.Core.Tests;

public sealed class ReminderTimerTests
{
    private sealed class ManualTime : TimeProvider
    {
        private long _timestamp;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => _timestamp;
        public DateTimeOffset WallClock { get; set; } = DateTimeOffset.UnixEpoch;
        public override DateTimeOffset GetUtcNow() => WallClock;
        public void Advance(TimeSpan duration) => _timestamp += duration.Ticks;
    }

    [Theory]
    [InlineData(1)] [InlineData(20)] [InlineData(30)] [InlineData(60)] [InlineData(240)]
    public void StartsOnlyOnRequestWithSelectedInterval(int minutes)
    {
        var clock = new ManualTime();
        var timer = new ReminderTimer(clock);
        Assert.True(timer.SetInterval(minutes));
        clock.Advance(TimeSpan.FromHours(1));
        Assert.Equal(TimerState.Ready, timer.State);
        Assert.Equal(TimeSpan.FromMinutes(minutes), timer.Remaining);
        Assert.True(timer.Start());
        Assert.Equal(TimerState.Running, timer.State);
        Assert.Equal(TimeSpan.FromMinutes(minutes), timer.Remaining);
    }

    [Fact]
    public void RemainingUsesElapsedMonotonicTimeWithoutTicksAndIgnoresWallClock()
    {
        var clock = new ManualTime();
        var timer = new ReminderTimer(clock, 30);
        timer.Start();
        clock.Advance(TimeSpan.FromSeconds(73.25));
        clock.WallClock = DateTimeOffset.UnixEpoch.AddDays(-7);
        Assert.Equal(TimeSpan.FromMinutes(30) - TimeSpan.FromSeconds(73.25), timer.Remaining);
        clock.WallClock = DateTimeOffset.UnixEpoch.AddYears(30);
        Assert.Equal(TimeSpan.FromMinutes(30) - TimeSpan.FromSeconds(73.25), timer.Remaining);
    }

    [Fact]
    public void PauseAndResumePreserveRemainingTimeAcrossLongPauseAndMultipleCycles()
    {
        var clock = new ManualTime();
        var timer = new ReminderTimer(clock, 1);
        timer.Start();
        clock.Advance(TimeSpan.FromSeconds(12.5));
        Assert.True(timer.Pause());
        clock.Advance(TimeSpan.FromDays(3));
        timer.Refresh();
        Assert.Equal(TimerState.Paused, timer.State);
        Assert.Equal(TimeSpan.FromSeconds(47.5), timer.Remaining);
        Assert.True(timer.Resume());
        clock.Advance(TimeSpan.FromSeconds(7.25));
        timer.Pause();
        Assert.Equal(TimeSpan.FromSeconds(40.25), timer.Remaining);
        clock.Advance(TimeSpan.FromHours(2));
        timer.Resume();
        clock.Advance(TimeSpan.FromSeconds(40.25));
        timer.Refresh();
        Assert.Equal(TimerState.Due, timer.State);
    }

    [Theory]
    [InlineData(TimerState.Running)] [InlineData(TimerState.Paused)] [InlineData(TimerState.Due)]
    public void ResetRestoresFullSelectedIntervalWithoutRestarting(TimerState initialState)
    {
        var clock = new ManualTime();
        var timer = new ReminderTimer(clock, 60);
        timer.Start();
        clock.Advance(TimeSpan.FromMinutes(10));
        if (initialState == TimerState.Paused) timer.Pause();
        if (initialState == TimerState.Due) { clock.Advance(TimeSpan.FromHours(2)); timer.Refresh(); }
        Assert.Equal(initialState, timer.State);
        timer.Reset();
        clock.Advance(TimeSpan.FromDays(1));
        timer.Refresh();
        Assert.Equal(TimerState.Ready, timer.State);
        Assert.Equal(TimeSpan.FromMinutes(60), timer.Remaining);
        Assert.True(timer.SetInterval(30));
    }

    [Theory]
    [InlineData(60)] [InlineData(65)] [InlineData(3600)]
    public void DeadlineAndOvershootBecomeDueOnceAndRequireExplicitResetAndStart(int elapsedSeconds)
    {
        var clock = new ManualTime();
        var timer = new ReminderTimer(clock, 1);
        var dueEvents = 0;
        timer.StateChanged += (_, _) => { if (timer.State == TimerState.Due) dueEvents++; };
        timer.Start();
        clock.Advance(TimeSpan.FromSeconds(elapsedSeconds));
        timer.Refresh();
        Assert.Equal(TimerState.Due, timer.State);
        Assert.Equal(TimeSpan.Zero, timer.Remaining);
        for (var i = 0; i < 10; i++)
        {
            clock.Advance(TimeSpan.FromMinutes(1)); timer.Refresh();
            Assert.False(timer.Start()); Assert.False(timer.Resume()); Assert.False(timer.Pause());
        }
        Assert.Equal(1, dueEvents);
        timer.Reset();
        clock.Advance(TimeSpan.FromHours(1)); timer.Refresh();
        Assert.Equal(TimerState.Ready, timer.State);
        timer.Start(); clock.Advance(TimeSpan.FromMinutes(1)); timer.Refresh();
        Assert.Equal(2, dueEvents);
    }

    [Fact]
    public void ImmediatelyBeforeDeadlineRemainsRunning()
    {
        var clock = new ManualTime(); var timer = new ReminderTimer(clock, 1);
        timer.Start(); clock.Advance(TimeSpan.FromMinutes(1) - TimeSpan.FromTicks(1));
        timer.Refresh();
        Assert.Equal(TimerState.Running, timer.State);
        Assert.Equal(TimeSpan.FromTicks(1), timer.Remaining);
    }

    [Fact]
    public void RepeatedAndInvalidTransitionsDoNotRestartOrConsumePausedTime()
    {
        var clock = new ManualTime(); var timer = new ReminderTimer(clock, 1);
        Assert.False(timer.Pause()); Assert.False(timer.Resume()); timer.Reset();
        timer.Start(); clock.Advance(TimeSpan.FromSeconds(15));
        Assert.False(timer.Start()); Assert.False(timer.Resume());
        Assert.Equal(TimeSpan.FromSeconds(45), timer.Remaining);
        timer.Pause(); clock.Advance(TimeSpan.FromHours(1));
        Assert.False(timer.Start()); Assert.False(timer.Pause());
        Assert.Equal(TimeSpan.FromSeconds(45), timer.Remaining);
    }

    [Theory]
    [InlineData(TimerState.Running)] [InlineData(TimerState.Paused)] [InlineData(TimerState.Due)]
    public void IntervalChangesAreRejectedOutsideReady(TimerState state)
    {
        var clock = new ManualTime(); var timer = new ReminderTimer(clock, 1);
        timer.Start();
        if (state == TimerState.Paused) timer.Pause();
        if (state == TimerState.Due) { clock.Advance(TimeSpan.FromMinutes(1)); timer.Refresh(); }
        Assert.False(timer.SetInterval(60));
        Assert.Equal(1, timer.IntervalMinutes);
        Assert.Equal(state, timer.State);
    }

    [Theory]
    [InlineData(-1)] [InlineData(0)] [InlineData(241)] [InlineData(int.MaxValue)]
    public void InvalidIntervalsThrowWithoutChangingConfiguration(int minutes)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ReminderTimer(intervalMinutes: minutes));
        var timer = new ReminderTimer(intervalMinutes: 30);
        Assert.Throws<ArgumentOutOfRangeException>(() => timer.SetInterval(minutes));
        Assert.Equal(30, timer.IntervalMinutes);
        Assert.Equal(TimeSpan.FromMinutes(30), timer.Remaining);
    }

    [Fact]
    public void PauseAtExpiredDeadlineBecomesDueInsteadOfPausingZero()
    {
        var clock = new ManualTime(); var timer = new ReminderTimer(clock, 1);
        timer.Start(); clock.Advance(TimeSpan.FromMinutes(1));
        Assert.False(timer.Pause());
        Assert.Equal(TimerState.Due, timer.State);
        Assert.Equal(TimeSpan.Zero, timer.Remaining);
    }
}
