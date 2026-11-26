using EyeCare4U.Core;

namespace EyeCare4U.Core.Tests;

public sealed class ReminderExperienceTests
{
    private sealed class Clock : TimeProvider
    {
        public long Ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => Ticks;
        public void Advance(double seconds) => Ticks += TimeSpan.FromSeconds(seconds).Ticks;
    }
    private sealed class Source(Clock clock) : IActivitySource
    {
        public bool Available = true, Locked;
        public long Input;
        public ActivityReading Read() => Available ? new(true,TimeSpan.FromTicks(clock.Ticks-Input),(ulong)(Input/10_000+86_400_000),Locked) : ActivityReading.Unavailable;
    }
    private sealed class Notifications : IReminderNotifications
    {
        public int Shows, Clears, Disposals;
        public bool Throw, Visible;
        public void ShowDue() { Shows++; if(Throw) throw new InvalidOperationException(); Visible=true; }
        public void Clear() { Clears++; Visible=false; if(Throw) throw new InvalidOperationException(); }
        public void Dismiss() => Visible=false; // Shell dismissal has no state-changing callback.
        public void Dispose() { Disposals++; if(Throw) throw new InvalidOperationException(); }
    }
    private sealed class Sounds : IReminderSounds
    {
        public readonly List<SoundCue> Plays=[];
        public int Stops, Disposals;
        public bool Throw;
        public void Play(SoundCue cue) { Plays.Add(cue); if(Throw) throw new InvalidOperationException(); }
        public void Stop() { Stops++; if(Throw) throw new InvalidOperationException(); }
        public void Dispose() { Disposals++; if(Throw) throw new InvalidOperationException(); }
    }
    private sealed class Setup : IDisposable
    {
        public Clock Clock { get; }=new();
        public Source Source { get; }
        public ActivitySession Session { get; }
        public Notifications Notifications { get; }=new();
        public Sounds Sounds { get; }=new();
        public ReminderExperience Experience { get; }
        public Setup()
        {
            Source=new(Clock); Session=new(Source,Clock); Session.Timer.SetInterval(1);
            Experience=new(Session,Notifications,Sounds,Clock);
        }
        public void Step(double seconds=1,bool input=true)
        {
            Clock.Advance(seconds); if(input) Source.Input=Clock.Ticks; Experience.Refresh();
        }
        public void Due()
        {
            Experience.Start(); for(var i=0;i<60;i++) Step();
            Assert.Equal(TimerState.Due,Session.Timer.State);
        }
        public void FinishBreak() { for(var i=0;i<20;i++) Step(input:false); }
        public void Dispose()=>Experience.Dispose();
    }

    [Fact]
    public void NotificationOccursOnceAndDismissalDoesNotResolveDue()
    {
        using var s=new Setup(); s.Due();
        Assert.Equal(1,s.Notifications.Shows); Assert.Equal(1,s.Experience.DueOccurrence);
        s.Notifications.Dismiss();
        for(var i=0;i<10;i++) s.Step();
        Assert.Equal(1,s.Notifications.Shows); Assert.False(s.Notifications.Visible);
        Assert.Equal(TimerState.Due,s.Session.Timer.State);
        s.Source.Locked=true; s.Session.Handle(SessionSignal.SessionChanged); s.Experience.Refresh();
        s.Source.Locked=false; s.Session.Handle(SessionSignal.SessionChanged); s.Experience.Refresh();
        Assert.Equal(1,s.Notifications.Shows);
    }

    [Theory]
    [InlineData("idle")] [InlineData("locked")] [InlineData("unavailable")] [InlineData("suspended")]
    public void DueDeliveryIsDeferredUntilActiveAndNeverReplayed(string blocker)
    {
        using var s=new Setup();
        // Advance the existing activity timer to Due without pumping delivery yet.
        s.Session.Timer.Start();
        for(var i=0;i<60;i++) { s.Clock.Advance(1); s.Source.Input=s.Clock.Ticks; s.Session.Poll(); }
        if(blocker=="idle") { s.Clock.Advance(121); s.Session.Poll(); s.Session.Poll(); }
        if(blocker=="locked") s.Source.Locked=true;
        if(blocker=="unavailable") s.Source.Available=false;
        if(blocker=="suspended") s.Session.Handle(SessionSignal.Suspending);
        s.Experience.Refresh(); Assert.Equal(0,s.Notifications.Shows);
        s.Source.Locked=false; s.Source.Available=true;
        if(blocker=="suspended") s.Session.Handle(SessionSignal.Resumed);
        s.Step(); s.Step();
        Assert.Equal(1,s.Notifications.Shows);
        for(var i=0;i<5;i++) s.Step(); Assert.Equal(1,s.Notifications.Shows);
    }

    [Fact]
    public void BreakFinishesOnceDespiteIdleWithoutManufacturingUsageOrRestartingInterval()
    {
        using var s=new Setup(); s.Session.SetIdleThreshold(30); s.Due();
        // Enter idle before starting the break. Activity time must remain frozen during it.
        s.Clock.Advance(31); s.Experience.Refresh(); s.Experience.Refresh();
        Assert.Equal(ActivityStatus.Idle,s.Session.Status);
        var usage=s.Session.EstimatedActiveTime;
        Assert.True(s.Experience.StartBreak()); Assert.False(s.Experience.StartBreak());
        s.Experience.BreakSoundsEnabled=true;
        for(var i=0;i<19;i++) s.Step(input:false);
        Assert.Equal(BreakState.Running,s.Experience.BreakState);
        Assert.Equal(TimerState.Due,s.Session.Timer.State); // Main interval is stopped throughout.
        s.Step(input:false);
        Assert.Equal(BreakState.Finished,s.Experience.BreakState);
        Assert.Equal("Break timer finished.",s.Experience.Result);
        Assert.Equal(TimerState.Ready,s.Session.Timer.State);
        Assert.Equal(TimeSpan.FromMinutes(1),s.Session.Timer.Remaining);
        Assert.Equal(usage,s.Session.EstimatedActiveTime);
        for(var i=0;i<30;i++) s.Step(input:false);
        Assert.Single(s.Sounds.Plays,SoundCue.BreakEnd);
        Assert.Equal(TimerState.Ready,s.Session.Timer.State);
    }

    [Fact]
    public void CancellationReturnsDueAndNeverCompletesOrCreatesAnotherAlert()
    {
        using var s=new Setup(); s.Due(); s.Experience.BreakSoundsEnabled=true;
        s.Experience.StartBreak(); s.Step(4); Assert.True(s.Experience.CancelBreak());
        Assert.False(s.Experience.CancelBreak());
        for(var i=0;i<30;i++) s.Step();
        Assert.Equal(BreakState.Cancelled,s.Experience.BreakState);
        Assert.Equal(TimerState.Due,s.Session.Timer.State);
        Assert.DoesNotContain("finished",s.Experience.Result);
        Assert.Equal(1,s.Notifications.Shows); Assert.DoesNotContain(SoundCue.BreakEnd,s.Sounds.Plays);
    }

    [Theory]
    [InlineData("lock")] [InlineData("suspend")] [InlineData("failure")] [InlineData("gap")] [InlineData("recovered-between-polls")]
    public void UncertainBreakCannotCompleteOrProduceDuplicateRecoveryAlert(string reason)
    {
        using var s=new Setup(); s.Due(); s.Experience.BreakSoundsEnabled=true; s.Experience.StartBreak(); s.Step(4);
        if(reason=="lock") s.Source.Locked=true;
        if(reason=="suspend") s.Session.Handle(SessionSignal.Suspending);
        if(reason=="failure") s.Source.Available=false;
        if(reason=="gap") s.Clock.Advance(30);
        if(reason=="recovered-between-polls") { s.Source.Available=false; s.Session.Poll(); s.Source.Available=true; s.Session.Poll(); }
        s.Experience.Refresh();
        Assert.Equal(BreakState.Interrupted,s.Experience.BreakState);
        s.Source.Locked=false; s.Source.Available=true;
        if(reason=="suspend") s.Session.Handle(SessionSignal.Resumed);
        for(var i=0;i<30;i++) s.Step();
        Assert.Equal(TimerState.Due,s.Session.Timer.State); Assert.Contains("interrupted",s.Experience.Result);
        Assert.Equal(1,s.Notifications.Shows); Assert.DoesNotContain(SoundCue.BreakEnd,s.Sounds.Plays);
    }

    [Theory]
    [InlineData(1)] [InlineData(5)] [InlineData(10)]
    public void SnoozeUsesOnlyActiveTimeAndReturnsDueExactlyOnce(int minutes)
    {
        using var s=new Setup(); s.Due(); Assert.True(s.Experience.Snooze(minutes));
        Assert.False(s.Experience.StartBreak()); Assert.False(s.Experience.Snooze(minutes));
        Assert.Equal(TimerState.Due,s.Session.Timer.State);
        s.Step(2); var remaining=s.Experience.Remaining;
        s.Source.Available=false; s.Step(4); s.Step(4); Assert.Equal(remaining,s.Experience.Remaining);
        s.Source.Available=true; s.Step(); // Recovery baseline only.
        Assert.Equal(remaining,s.Experience.Remaining);
        for(var i=0;i<minutes*60-3;i++) s.Step();
        Assert.Equal(ReminderMode.Snooze,s.Experience.Mode);
        s.Step(); Assert.Equal(ReminderMode.Interval,s.Experience.Mode);
        Assert.Equal(TimerState.Due,s.Session.Timer.State);
        Assert.Equal(2,s.Notifications.Shows); Assert.Equal(2,s.Experience.DueOccurrence);
        for(var i=0;i<10;i++) s.Step(); Assert.Equal(2,s.Notifications.Shows);
    }

    [Theory]
    [InlineData("idle")] [InlineData("lock")] [InlineData("sleep")]
    public void SnoozeManualPauseSurvivesAutomaticTransitions(string blocker)
    {
        using var s=new Setup(); s.Session.SetIdleThreshold(30); s.Due(); s.Experience.Snooze(1); s.Step(2);
        s.Experience.Pause(); var remaining=s.Experience.Remaining;
        if(blocker=="lock") s.Source.Locked=true;
        if(blocker=="sleep") s.Session.Handle(SessionSignal.Suspending);
        s.Clock.Advance(100); s.Experience.Refresh();
        s.Source.Locked=false;
        if(blocker=="sleep") s.Session.Handle(SessionSignal.Resumed);
        s.Step(); s.Step();
        Assert.Equal(TimerState.Paused,s.Experience.SnoozeState); Assert.Equal(remaining,s.Experience.Remaining);
        s.Experience.Resume(); s.Step(); Assert.True(s.Experience.Remaining<remaining);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void ResetInvalidatesBreakAndSnoozeWithoutLateCompletion(bool breakMode)
    {
        using var s=new Setup(); s.Due();
        if(breakMode) s.Experience.StartBreak(); else s.Experience.Snooze(1);
        s.Experience.Reset();
        for(var i=0;i<70;i++) s.Step();
        Assert.Equal(ReminderMode.Interval,s.Experience.Mode); Assert.Equal(BreakState.None,s.Experience.BreakState);
        Assert.Equal(TimerState.Ready,s.Session.Timer.State); Assert.Equal("",s.Experience.Result);
        Assert.Equal(1,s.Notifications.Shows); Assert.Empty(s.Sounds.Plays);
    }

    [Fact]
    public void SettingsDefaultSilentAndDisabledEffectsDoNotReplayWhenEnabledLater()
    {
        using var s=new Setup();
        Assert.True(s.Experience.NotificationsEnabled); Assert.False(s.Experience.ReminderSoundEnabled); Assert.False(s.Experience.BreakSoundsEnabled);
        s.Experience.NotificationsEnabled=false; s.Due();
        Assert.Equal(0,s.Notifications.Shows); Assert.Empty(s.Sounds.Plays);
        s.Experience.NotificationsEnabled=true; s.Experience.ReminderSoundEnabled=true; s.Step();
        Assert.Equal(0,s.Notifications.Shows); Assert.Empty(s.Sounds.Plays);
        s.Experience.StartBreak(); s.FinishBreak(); Assert.Empty(s.Sounds.Plays);
    }

    [Fact]
    public void EnabledSoundsHaveOneAttemptPerEligibleTransition()
    {
        using var s=new Setup(); s.Experience.ReminderSoundEnabled=true; s.Experience.BreakSoundsEnabled=true;
        s.Due(); for(var i=0;i<5;i++) s.Step();
        s.Experience.StartBreak(); s.Experience.StartBreak(); s.FinishBreak(); s.Step();
        Assert.Equal(new[]{SoundCue.Reminder,SoundCue.BreakStart,SoundCue.BreakEnd},s.Sounds.Plays);
    }

    [Fact]
    public void EffectAndCleanupFailuresAreContainedWithoutRetryStormOrStateCorruption()
    {
        using var s=new Setup(); s.Notifications.Throw=true; s.Sounds.Throw=true;
        s.Experience.ReminderSoundEnabled=true; s.Experience.BreakSoundsEnabled=true; s.Due();
        for(var i=0;i<5;i++) s.Step();
        Assert.Equal(1,s.Notifications.Shows); Assert.Single(s.Sounds.Plays);
        Assert.NotEmpty(s.Experience.DeliveryNote);
        Assert.True(s.Experience.StartBreak()); s.FinishBreak();
        Assert.Equal(TimerState.Ready,s.Session.Timer.State); Assert.Equal("Break timer finished.",s.Experience.Result);
        s.Experience.Dispose(); s.Experience.Dispose();
        Assert.Equal(1,s.Notifications.Disposals); Assert.Equal(1,s.Sounds.Disposals);
    }

    [Fact]
    public void ShutdownMakesStaleRefreshAndCommandsInert()
    {
        using var s=new Setup(); s.Due(); s.Experience.StartBreak(); s.Experience.Dispose();
        var sounds=s.Sounds.Plays.Count; var shows=s.Notifications.Shows;
        for(var i=0;i<30;i++) s.Step();
        Assert.False(s.Experience.CancelBreak()); Assert.False(s.Experience.StartBreak());
        Assert.False(s.Experience.Snooze(5)); Assert.False(s.Experience.Start());
        s.Experience.Reset();
        Assert.NotEqual(BreakState.Finished,s.Experience.BreakState);
        Assert.Equal(sounds,s.Sounds.Plays.Count); Assert.Equal(shows,s.Notifications.Shows);
        Assert.Equal(1,s.Notifications.Disposals); Assert.Equal(1,s.Sounds.Disposals);
    }

    [Theory]
    [InlineData(0)] [InlineData(2)] [InlineData(11)]
    public void InvalidOrPrematureActionsDoNotStartAnything(int minutes)
    {
        using var s=new Setup(); Assert.False(s.Experience.Snooze(minutes));
        Assert.False(s.Experience.StartBreak()); Assert.False(s.Experience.CancelBreak());
        Assert.False(s.Experience.Pause()); Assert.False(s.Experience.Resume());
        Assert.Equal(TimerState.Ready,s.Session.Timer.State); Assert.Empty(s.Sounds.Plays); Assert.Equal(0,s.Notifications.Shows);
    }
    [Fact]
    public void SnoozeResumeWhileBlockedArmsWithoutConsumingAndResetClearsPendingAlert()
    {
        using var s=new Setup(); s.Due(); s.Experience.Snooze(1); s.Experience.Pause();
        s.Source.Available=false; s.Step(); Assert.True(s.Experience.Resume());
        s.Step(4); Assert.Equal(TimeSpan.FromMinutes(1),s.Experience.Remaining);
        s.Experience.Reset(); s.Source.Available=true;
        for(var i=0;i<65;i++) s.Step();
        Assert.Equal(TimerState.Ready,s.Session.Timer.State); Assert.Equal(1,s.Notifications.Shows);
    }

    [Fact]
    public void OptionChangesStopOutstandingEffectsWithoutReplayingTheOccurrence()
    {
        using var s=new Setup(); s.Experience.ReminderSoundEnabled=true; s.Due();
        var clears=s.Notifications.Clears; var stops=s.Sounds.Stops;
        s.Experience.NotificationsEnabled=false; s.Experience.ReminderSoundEnabled=false;
        Assert.True(s.Notifications.Clears>clears); Assert.True(s.Sounds.Stops>stops);
        s.Experience.NotificationsEnabled=true; s.Experience.ReminderSoundEnabled=true; s.Step();
        Assert.Equal(1,s.Notifications.Shows); Assert.Single(s.Sounds.Plays);
    }}
