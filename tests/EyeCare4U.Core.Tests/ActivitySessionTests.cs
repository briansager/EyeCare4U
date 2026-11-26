using EyeCare4U.Core;

namespace EyeCare4U.Core.Tests;

public sealed class ActivitySessionTests
{
    private sealed class Clock : TimeProvider
    {
        public long Ticks;
        public DateTimeOffset WallClock = DateTimeOffset.UnixEpoch;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => Ticks;
        public override DateTimeOffset GetUtcNow() => WallClock;
        public void Advance(double seconds) => Ticks += TimeSpan.FromSeconds(seconds).Ticks;
    }
    private sealed class Source(Clock clock) : IActivitySource
    {
        public long LastInput;
        public bool Available = true;
        public bool Locked;
        public ActivityReading? Override;
        public ActivityReading Read() => Override ?? (Available
            ? new(true, TimeSpan.FromTicks(clock.Ticks - LastInput), (ulong)(LastInput / TimeSpan.TicksPerMillisecond + 86_400_000), Locked)
            : ActivityReading.Unavailable);
    }
    private sealed class Setup
    {
        public Clock Clock { get; } = new();
        public Source Source { get; }
        public ActivitySession Session { get; }
        public ReminderTimer Timer => Session.Timer;
        public Setup(double initialIdle = 0, bool locked = false)
        {
            Source = new(Clock) { LastInput = -TimeSpan.FromSeconds(initialIdle).Ticks, Locked = locked };
            Session = new(Source, Clock);
            Timer.SetInterval(1);
        }
        public void Step(double seconds = 1, bool input = false)
        {
            Clock.Advance(seconds);
            if (input) Source.LastInput = Clock.Ticks;
            Session.Poll();
        }
        public void Signal(SessionSignal signal) => Session.Handle(signal);
    }

    [Fact]
    public void LaunchDoesNotBackfillAndUsageIsIndependentOfReminderState()
    {
        var s = new Setup(initialIdle: 10);
        Assert.Equal(TimeSpan.Zero, s.Session.EstimatedActiveTime);
        Assert.Equal(TimerState.Ready, s.Timer.State);
        s.Step(2);
        Assert.Equal(TimeSpan.FromSeconds(2), s.Session.EstimatedActiveTime);
        s.Timer.Start(); s.Step(2); s.Timer.Pause(); s.Step(2);
        Assert.Equal(TimeSpan.FromSeconds(6), s.Session.EstimatedActiveTime);
        Assert.Equal(TimeSpan.FromSeconds(58), s.Timer.Remaining);
        s.Timer.Reset();
        for (var i=0; i<60; i++) { if(i==0) s.Timer.Start(); s.Step(input:true); }
        Assert.Equal(TimerState.Due, s.Timer.State);
        var before = s.Session.EstimatedActiveTime;
        s.Step(2);
        Assert.Equal(before + TimeSpan.FromSeconds(2), s.Session.EstimatedActiveTime);
        Assert.Equal(TimerState.Due, s.Timer.State);
    }

    [Theory]
    [InlineData(119, 1, 1)] [InlineData(119.5, 1, .5)] [InlineData(120, 1, 0)] [InlineData(121, 1, 0)]
    public void CountsOnlyGraceBeforeIdleCutoff(double idle, double elapsed, double credited)
    {
        var s = new Setup(idle); s.Timer.Start(); s.Step(elapsed);
        Assert.Equal(TimeSpan.FromSeconds(credited), s.Session.EstimatedActiveTime);
        Assert.Equal(TimeSpan.FromSeconds(60-credited), s.Timer.Remaining);
        Assert.Equal(ActivityStatus.Idle, s.Session.Status);
    }

    [Fact]
    public void NewInputCountsOnlyKnownWindowsOnEitherSideOfIdleGap()
    {
        var s = new Setup(119); s.Timer.Start();
        s.Clock.Advance(4); s.Source.LastInput = s.Clock.Ticks; // Old cutoff at t=1; new input at t=4.
        s.Clock.Advance(1); s.Session.Poll();
        Assert.Equal(TimeSpan.FromSeconds(2), s.Session.EstimatedActiveTime);
        Assert.Equal(TimeSpan.FromSeconds(58), s.Timer.Remaining);
        Assert.Equal(ActivityStatus.Active, s.Session.Status);
    }

    [Fact]
    public void RecoveryFromIdleDoesNotCreditWholePreviousPollingInterval()
    {
        var s = new Setup(150); s.Timer.Start();
        s.Clock.Advance(3); s.Source.LastInput = s.Clock.Ticks;
        s.Clock.Advance(1); s.Session.Poll();
        Assert.Equal(TimeSpan.FromSeconds(1), s.Session.EstimatedActiveTime);
        Assert.Equal(TimeSpan.FromSeconds(59), s.Timer.Remaining);
    }

    [Fact]
    public void StartWhileAlreadyIdleArmsButDoesNotConsumeUntilInput()
    {
        var s = new Setup(500); s.Timer.Start(); s.Step(4);
        Assert.True(s.Session.IsHeld); Assert.Equal(TimerState.Running, s.Timer.State);
        Assert.Equal(TimeSpan.FromMinutes(1), s.Timer.Remaining);
        s.Step(input:true); Assert.False(s.Session.IsHeld);
        Assert.Equal(TimeSpan.FromMinutes(1), s.Timer.Remaining);
        s.Step(); Assert.Equal(TimeSpan.FromSeconds(59), s.Timer.Remaining);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void StartupLockedAndLockTransitionsExcludeBoundaryAndLockedTime(bool lockedAtLaunch)
    {
        var s = new Setup(0, lockedAtLaunch); s.Timer.Start();
        s.Source.Locked = true; s.Signal(SessionSignal.SessionChanged); s.Session.Poll();
        s.Step(3);
        Assert.Equal(ActivityStatus.Locked,s.Session.Status);
        Assert.Equal(TimeSpan.Zero,s.Session.EstimatedActiveTime);
        s.Clock.Advance(.5); s.Source.Locked = false; s.Signal(SessionSignal.SessionChanged); s.Session.Poll();
        s.Step(2);
        Assert.Equal(TimeSpan.FromSeconds(2),s.Session.EstimatedActiveTime);
        Assert.Equal(TimeSpan.FromSeconds(58),s.Timer.Remaining);
    }

    [Fact]
    public void MissedLockNotificationsStillDiscardTransitionIntervals()
    {
        var s = new Setup(); s.Step();
        s.Source.Locked=true; s.Step(); s.Step(3);
        s.Source.Locked=false; s.Step(); s.Step();
        Assert.Equal(TimeSpan.FromSeconds(2),s.Session.EstimatedActiveTime);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void SleepIsExcludedAndManualPauseSurvivesWake(bool paused)
    {
        var s=new Setup(); s.Timer.Start(); s.Step(); if(paused) s.Timer.Pause();
        s.Signal(SessionSignal.Suspending); s.Signal(SessionSignal.Suspending);
        s.Clock.Advance(10_000); s.Session.Poll();
        s.Signal(SessionSignal.Resumed); s.Signal(SessionSignal.Resumed); s.Session.Poll();
        Assert.Equal(TimeSpan.FromSeconds(1),s.Session.EstimatedActiveTime);
        Assert.Equal(TimeSpan.FromSeconds(59),s.Timer.Remaining);
        Assert.Equal(paused ? TimerState.Paused : TimerState.Running,s.Timer.State);
        Assert.Contains("sleep time excluded",s.Session.ResumeNotice);
        s.Step(input:true); s.Step();
        Assert.Equal(paused ? TimeSpan.FromSeconds(59) : TimeSpan.FromSeconds(58),s.Timer.Remaining);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void OverlappingLockIdleSleepAndUnavailableDoNotResumePrematurely(bool unlockBeforeWake)
    {
        var s=new Setup(); s.Timer.Start(); s.Step();
        s.Source.Locked=true; s.Signal(SessionSignal.SessionChanged); s.Session.Poll();
        s.Signal(SessionSignal.Suspending); s.Clock.Advance(500);
        if(unlockBeforeWake) { s.Source.Locked=false; s.Signal(SessionSignal.SessionChanged); }
        s.Signal(SessionSignal.Resumed); s.Session.Poll();
        if(!unlockBeforeWake) { Assert.Equal(ActivityStatus.Locked,s.Session.Status); s.Source.Locked=false; s.Signal(SessionSignal.SessionChanged); s.Session.Poll(); }
        Assert.Equal(ActivityStatus.Idle,s.Session.Status); Assert.True(s.Session.IsHeld);
        s.Source.Available=false; s.Step(); Assert.Equal(ActivityStatus.Unavailable,s.Session.Status);
        s.Source.Available=true; s.Step(); Assert.Equal(ActivityStatus.Idle,s.Session.Status);
        Assert.Equal(TimeSpan.FromSeconds(59),s.Timer.Remaining);
        s.Step(input:true); s.Step();
        Assert.Equal(TimeSpan.FromSeconds(58),s.Timer.Remaining);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void ManualPauseAlwaysWinsAutomaticRecovery(bool resumeWhileBlocked)
    {
        var s=new Setup(); s.Timer.Start(); s.Step(); s.Timer.Pause();
        s.Source.Available=false; s.Step();
        if(resumeWhileBlocked) { Assert.True(s.Timer.Resume()); Assert.True(s.Session.IsHeld); s.Timer.Pause(); }
        s.Source.Locked=true; s.Signal(SessionSignal.SessionChanged); s.Session.Poll();
        s.Signal(SessionSignal.Suspending); s.Clock.Advance(1000);
        s.Signal(SessionSignal.Resumed); s.Session.Poll();
        s.Source.Available=true; s.Source.Locked=false; s.Source.LastInput=s.Clock.Ticks;
        s.Signal(SessionSignal.SessionChanged); s.Session.Poll(); s.Step();
        Assert.Equal(TimerState.Paused,s.Timer.State);
        Assert.Equal(TimeSpan.FromSeconds(59),s.Timer.Remaining);
        Assert.False(s.Session.IsHeld); // Manual pause, not armed automatic hold.
    }

    [Fact]
    public void FailureRecoveryNeverBackfillsUnknownTime()
    {
        var s=new Setup(); s.Timer.Start(); s.Step(2);
        s.Source.Available=false; s.Step(2); s.Step(2);
        Assert.Equal(ActivityStatus.Unavailable,s.Session.Status);
        s.Source.Available=true; s.Step(2); // Fresh baseline only.
        Assert.Equal(TimeSpan.FromSeconds(2),s.Session.EstimatedActiveTime);
        s.Step(2); Assert.Equal(TimeSpan.FromSeconds(4),s.Session.EstimatedActiveTime);
        Assert.Equal(TimeSpan.FromSeconds(56),s.Timer.Remaining);
    }

    [Theory]
    [InlineData(5,5)] [InlineData(5.001,0)] [InlineData(1000,0)]
    public void GapFallbackDistinguishesDelayedRefreshAndUnknownGap(double elapsed,double credited)
    {
        var s=new Setup(); s.Timer.Start(); s.Step(elapsed);
        Assert.Equal(TimeSpan.FromSeconds(credited),s.Session.EstimatedActiveTime);
        if(elapsed>5)
        {
            Assert.Equal(ActivityStatus.Unavailable,s.Session.Status);
            s.Step(input:true); Assert.Equal(ActivityStatus.Active,s.Session.Status);
            Assert.Equal(TimeSpan.Zero,s.Session.EstimatedActiveTime);
            s.Step(); Assert.Equal(TimeSpan.FromSeconds(1),s.Session.EstimatedActiveTime);
        }
    }

    [Fact]
    public void DuplicatePollsAndSignalsDoNotCountTwice()
    {
        var s=new Setup(); s.Step();
        for(var i=0;i<5;i++) s.Session.Poll();
        s.Source.Locked=true;
        for(var i=0;i<5;i++) { s.Signal(SessionSignal.SessionChanged); s.Session.Poll(); }
        s.Source.Locked=false; s.Signal(SessionSignal.SessionChanged); s.Session.Poll(); s.Step();
        Assert.Equal(TimeSpan.FromSeconds(2),s.Session.EstimatedActiveTime);
    }

    [Fact]
    public void ResetAndDueRetainExplicitSemanticsWhileBlocked()
    {
        var s=new Setup(); s.Timer.Start(); for(var i=0;i<60;i++) s.Step(input:true);
        Assert.Equal(TimerState.Due,s.Timer.State);
        s.Source.Available=false; s.Step(); Assert.Equal(TimerState.Due,s.Timer.State);
        s.Timer.Reset(); Assert.Equal(TimerState.Ready,s.Timer.State);
        Assert.Equal(TimeSpan.FromMinutes(1),s.Timer.Remaining);
        s.Timer.Start(); s.Step(2); Assert.True(s.Session.IsHeld);
        s.Timer.Reset(); s.Source.Available=true; s.Step(); s.Step();
        Assert.Equal(TimerState.Ready,s.Timer.State); Assert.Equal(TimeSpan.FromMinutes(1),s.Timer.Remaining);
    }

    [Fact]
    public void ThresholdChangesAreProspectiveAndAllowedOnlyInReady()
    {
        var s=new Setup(100); s.Step(2);
        Assert.True(s.Session.SetIdleThreshold(30)); Assert.Equal(ActivityStatus.Idle,s.Session.Status);
        s.Step(2); Assert.Equal(TimeSpan.FromSeconds(2),s.Session.EstimatedActiveTime);
        Assert.True(s.Session.SetIdleThreshold(900)); s.Step(2);
        Assert.Equal(TimeSpan.FromSeconds(4),s.Session.EstimatedActiveTime);
        s.Timer.Start(); Assert.False(s.Session.SetIdleThreshold(120));
        s.Timer.Pause(); Assert.False(s.Session.SetIdleThreshold(120));
        Assert.Equal(900,s.Session.IdleThresholdSeconds);
    }

    [Theory]
    [InlineData(29)] [InlineData(901)] [InlineData(-1)]
    public void InvalidThresholdCannotChangeTotalsOrConfiguration(int seconds)
    {
        var s=new Setup(); s.Step();
        Assert.Throws<ArgumentOutOfRangeException>(()=>s.Session.SetIdleThreshold(seconds));
        Assert.Equal(120,s.Session.IdleThresholdSeconds); Assert.Equal(TimeSpan.FromSeconds(1),s.Session.EstimatedActiveTime);
    }

    [Fact]
    public void WallClockChangesDoNotChangeAccounting()
    {
        var s=new Setup(); s.Timer.Start(); s.Step();
        s.Clock.WallClock=s.Clock.WallClock.AddYears(-20); s.Step();
        s.Clock.WallClock=s.Clock.WallClock.AddYears(50); s.Step();
        Assert.Equal(TimeSpan.FromSeconds(3),s.Session.EstimatedActiveTime);
        Assert.Equal(TimeSpan.FromSeconds(57),s.Timer.Remaining);
    }

    [Fact]
    public void BadSampleOrRegressedMonotonicClockCannotCreateNegativeOrInventedTime()
    {
        var s=new Setup(); s.Step();
        s.Source.Override=new(true,TimeSpan.FromSeconds(-1),1,false); s.Step();
        Assert.Equal(ActivityStatus.Unavailable,s.Session.Status);
        s.Source.Override=null; s.Step(); s.Clock.Advance(-1); s.Session.Poll();
        Assert.Equal(ActivityStatus.Unavailable,s.Session.Status);
        Assert.Equal(TimeSpan.FromSeconds(1),s.Session.EstimatedActiveTime);
    }

    [Fact]
    public void DiscontinuousInputSampleDropsWholeUncertainInterval()
    {
        var s=new Setup(); s.Step();
        s.Source.Override=new(true,TimeSpan.Zero,1,false); s.Step();
        Assert.Equal(ActivityStatus.Unavailable,s.Session.Status);
        Assert.Equal(TimeSpan.FromSeconds(1),s.Session.EstimatedActiveTime);
    }
    [Fact]
    public void LargeIdleValueIsRejectedWithoutArithmeticOverflow()
    {
        var s=new Setup(); s.Step();
        s.Source.Override=new(true,TimeSpan.MaxValue,1,false); s.Step();
        Assert.Equal(ActivityStatus.Unavailable,s.Session.Status);
        Assert.Equal(TimeSpan.FromSeconds(1),s.Session.EstimatedActiveTime);
    }

    [Fact]
    public void MonitoringLaunchedAtLaterTimestampDoesNotFabricateEarlierUsage()
    {
        var clock=new Clock(); clock.Advance(10_000);
        var source=new Source(clock) { LastInput=clock.Ticks-TimeSpan.FromSeconds(20).Ticks };
        var session=new ActivitySession(source,clock);
        Assert.Equal(TimeSpan.Zero,session.EstimatedActiveTime);
        clock.Advance(1); session.Poll();
        Assert.Equal(TimeSpan.FromSeconds(1),session.EstimatedActiveTime);
    }

    [Fact]
    public void UnannouncedResumeSignalDropsUnresolvedIntervalWithoutImmediateDue()
    {
        var s=new Setup(); s.Timer.Start(); s.Step();
        s.Clock.Advance(3600); // Simulate a missed suspend event followed by a delivered resume.
        s.Signal(SessionSignal.Resumed); s.Session.Poll();
        Assert.Equal(TimerState.Running,s.Timer.State);
        Assert.Equal(TimeSpan.FromSeconds(59),s.Timer.Remaining);
        Assert.Equal(TimeSpan.FromSeconds(1),s.Session.EstimatedActiveTime);
    }    private sealed class SlowSource(Clock clock) : IActivitySource
    {
        public double Delay;
        public ActivityReading Read()
        {
            clock.Advance(Delay);
            return new(true,TimeSpan.FromTicks(clock.Ticks),86_400_000,false);
        }
    }

    [Fact]
    public void NativeSamplingDelayIsIncludedInTheUnreliableGap()
    {
        var clock=new Clock(); var source=new SlowSource(clock);
        var session=new ActivitySession(source,clock); session.Timer.Start();
        source.Delay=6; session.Poll();
        Assert.Equal(ActivityStatus.Unavailable,session.Status);
        Assert.Equal(TimeSpan.Zero,session.EstimatedActiveTime);
        Assert.Equal(TimeSpan.FromMinutes(20),session.Timer.Remaining);
    }}
