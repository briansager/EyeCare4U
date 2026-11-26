using EyeCare4U.App.ViewModels;
using EyeCare4U.Core;

namespace EyeCare4U.App.Tests;

public sealed class MonitoringViewModelTests
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
        public bool Available = true;
        public bool Locked;
        public long Input;
        public ActivityReading Read() => Available
            ? new(true,TimeSpan.FromTicks(clock.Ticks-Input),(ulong)(Input/TimeSpan.TicksPerMillisecond+86_400_000),Locked)
            : ActivityReading.Unavailable;
    }

    [Fact]
    public void PreStartActivityDoesNotConsumeReminderAndPauseSettlesOnlyBeforeUserAction()
    {
        var clock=new Clock(); var source=new Source(clock); var session=new ActivitySession(source,clock);
        var vm=new MainWindowViewModel(session);
        clock.Advance(3); vm.StartCommand.Execute(null);
        Assert.Equal("20:00",vm.RemainingText); Assert.Equal("00:00:03",vm.EstimatedTimeText);
        clock.Advance(2); vm.PauseCommand.Execute(null);
        Assert.Equal("19:58",vm.RemainingText); Assert.Equal("Paused by you",vm.Status);
        clock.Advance(2); vm.Refresh();
        Assert.Equal("19:58",vm.RemainingText); Assert.Equal("00:00:07",vm.EstimatedTimeText);
    }

    [Theory]
    [InlineData("29")] [InlineData("901")] [InlineData("1.5")] [InlineData("abc")] [InlineData("")]
    public void InvalidThresholdIsRetainedAndCannotStartUsingAnUnseenFallback(string text)
    {
        var clock=new Clock(); var session=new ActivitySession(new Source(clock),clock);
        var vm=new MainWindowViewModel(session) { IdleThresholdText=text };
        Assert.Equal(text,vm.IdleThresholdText);
        Assert.Equal(IdleThresholdInput.ErrorMessage,vm.ThresholdValidationError);
        Assert.Equal(120,session.IdleThresholdSeconds);
        Assert.Contains("120 seconds",vm.AppliedIdleThreshold);
        Assert.False(vm.StartCommand.CanExecute(null)); vm.StartCommand.Execute(null);
        Assert.Equal(TimerState.Ready,vm.State);
        vm.IdleThresholdText="30";
        Assert.Equal("",vm.ThresholdValidationError); Assert.True(vm.StartCommand.CanExecute(null));
        Assert.Equal(30,session.IdleThresholdSeconds);
        vm.StartCommand.Execute(null); vm.IdleThresholdText="900";
        Assert.Equal("30",vm.IdleThresholdText); Assert.Equal(30,session.IdleThresholdSeconds);
        vm.PauseCommand.Execute(null); vm.IdleThresholdText="900";
        Assert.Equal("30",vm.IdleThresholdText);
    }

    [Fact]
    public void StartAndResumeWhileBlockedAreArmedAndManualPauseSurvivesRecovery()
    {
        var clock=new Clock(); var source=new Source(clock) { Available=false };
        var session=new ActivitySession(source,clock); var vm=new MainWindowViewModel(session);
        vm.StartCommand.Execute(null);
        Assert.Equal(TimerState.Running,vm.State); Assert.Contains("Monitoring unavailable",vm.Status);
        Assert.True(vm.PauseCommand.CanExecute(null));
        clock.Advance(3); vm.Refresh(); Assert.Equal("20:00",vm.RemainingText);
        vm.PauseCommand.Execute(null); Assert.Equal("Paused by you",vm.Status);
        vm.ResumeCommand.Execute(null); Assert.StartsWith("Waiting",vm.Status);
        vm.PauseCommand.Execute(null);
        source.Available=true; source.Input=clock.Ticks; vm.Refresh();
        clock.Advance(2); vm.Refresh();
        Assert.Equal("Paused by you",vm.Status); Assert.Equal("20:00",vm.RemainingText);
        Assert.Equal("00:00:02",vm.EstimatedTimeText);
        vm.ResumeCommand.Execute(null); clock.Advance(1); vm.Refresh(); Assert.Equal("19:59",vm.RemainingText);
    }

    [Fact]
    public void MeaningfulHoldChangesNotifyWithoutRepeatingAnnouncementsEveryTick()
    {
        var clock=new Clock(); var source=new Source(clock); var session=new ActivitySession(source,clock);
        var vm=new MainWindowViewModel(session); vm.StartCommand.Execute(null);
        var statuses=0; var activities=0;
        vm.PropertyChanged+=(_,e)=> { if(e.PropertyName==nameof(vm.Status)) statuses++; if(e.PropertyName==nameof(vm.ActivityStatusText)) activities++; };
        for(var i=0;i<3;i++) { clock.Advance(1); vm.Refresh(); }
        Assert.Equal(0,statuses); Assert.Equal(0,activities);
        source.Locked=true; session.Handle(SessionSignal.SessionChanged); vm.Refresh();
        Assert.Contains("Session locked",vm.Status); Assert.Equal(1,statuses); Assert.Equal(1,activities);
        for(var i=0;i<3;i++) { clock.Advance(1); vm.Refresh(); }
        Assert.Equal(1,statuses); Assert.Equal(1,activities);
        source.Locked=false; session.Handle(SessionSignal.SessionChanged); vm.Refresh();
        Assert.Equal("Running",vm.Status); Assert.Equal(2,statuses); Assert.Equal(2,activities);
    }
}
