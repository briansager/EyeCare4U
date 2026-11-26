using EyeCare4U.App.ViewModels;
using EyeCare4U.Core;

namespace EyeCare4U.App.Tests;

public sealed class BreakViewModelTests
{
    private sealed class Clock : TimeProvider
    {
        public long Ticks;
        public override long TimestampFrequency=>TimeSpan.TicksPerSecond;
        public override long GetTimestamp()=>Ticks;
        public void Advance()=>Ticks+=TimeSpan.TicksPerSecond;
    }
    private sealed class Source(Clock clock) : IActivitySource
    {
        public bool Available=true;
        public ActivityReading Read()=>Available ? new(true,TimeSpan.Zero,(ulong)(clock.Ticks/10_000),false) : ActivityReading.Unavailable;
    }
    private sealed class Notifications : IReminderNotifications
    {
        public int Count;
        public void ShowDue()=>Count++;
        public void Clear() { }
        public void Dispose() { }
    }
    private sealed class Sounds : IReminderSounds
    {
        public readonly List<SoundCue> Cues=[];
        public void Play(SoundCue cue)=>Cues.Add(cue);
        public void Stop() { }
        public void Dispose() { }
    }
    [Fact]
    public void CommandsLabelsAndOptionsFollowTheSharedExperience()
    {
        var clock=new Clock(); var source=new Source(clock); var session=new ActivitySession(source,clock);
        var notifications=new Notifications(); var sounds=new Sounds();
        using var experience=new ReminderExperience(session,notifications,sounds,clock);
        var vm=new MainWindowViewModel(session); vm.AttachExperience(experience);
        vm.SelectedInterval="Custom"; vm.CustomMinutes="1";
        Assert.Equal(5,vm.SelectedSnooze); Assert.True(vm.NotificationsEnabled); Assert.False(vm.ReminderSoundEnabled); Assert.False(vm.BreakSoundsEnabled);
        Assert.False(vm.StartBreakCommand.CanExecute(null)); Assert.False(vm.SnoozeCommand.CanExecute(null));
        vm.StartCommand.Execute(null);
        for(var i=0;i<60;i++) { clock.Advance(); vm.Refresh(); }
        Assert.True(vm.StartBreakCommand.CanExecute(null)); Assert.True(vm.SnoozeCommand.CanExecute(null));
        Assert.Equal(1,notifications.Count);
        vm.SnoozeCommand.Execute(null);
        Assert.Equal("Snoozing",vm.StateName); Assert.StartsWith("Snooze remaining",vm.CountdownLabel);
        Assert.Equal("05:00",vm.RemainingText); Assert.False(vm.CanEditInterval); Assert.True(vm.PauseCommand.CanExecute(null));
        vm.PauseCommand.Execute(null); Assert.Equal("Snooze paused by you",vm.Status);
        source.Available=false; clock.Advance(); vm.Refresh(); source.Available=true; clock.Advance(); vm.Refresh();
        Assert.Equal("Snooze paused by you",vm.Status); Assert.Equal("05:00",vm.RemainingText);
        vm.ResetCommand.Execute(null);
        Assert.Equal("01:00",vm.RemainingText); Assert.True(vm.CanEditInterval); Assert.False(vm.StartBreakCommand.CanExecute(null));
    }

    [Fact]
    public void BreakResultAndStateNotificationsAreSignificantRatherThanPerSecond()
    {
        var clock=new Clock(); var session=new ActivitySession(new Source(clock),clock);
        using var experience=new ReminderExperience(session,new Notifications(),new Sounds(),clock);
        var vm=new MainWindowViewModel(session); vm.AttachExperience(experience);
        vm.SelectedInterval="Custom"; vm.CustomMinutes="1"; vm.StartCommand.Execute(null);
        for(var i=0;i<60;i++) { clock.Advance(); vm.Refresh(); }
        vm.StartBreakCommand.Execute(null);
        Assert.Equal("Break running",vm.StateName); Assert.Equal("Break remaining",vm.CountdownLabel);
        Assert.Contains("6 metres",vm.BreakGuidance); Assert.False(vm.PauseCommand.CanExecute(null)); Assert.True(vm.CancelBreakCommand.CanExecute(null));
        var statuses=0; var results=0;
        vm.PropertyChanged+=(_,e)=> { if(e.PropertyName==nameof(vm.Status)) statuses++; if(e.PropertyName==nameof(vm.ExperienceResult)) results++; };
        for(var i=0;i<19;i++) { clock.Advance(); vm.Refresh(); }
        Assert.Equal(0,statuses); Assert.Equal(0,results);
        clock.Advance(); vm.Refresh();
        Assert.Equal("Break timer finished.",vm.ExperienceResult); Assert.Equal("01:00",vm.RemainingText);
        Assert.Equal(1,statuses); Assert.Equal(1,results); Assert.True(vm.StartCommand.CanExecute(null));
        Assert.False(vm.CancelBreakCommand.CanExecute(null)); Assert.Equal("",vm.BreakGuidance);
    }

    [Fact]
    public void DisabledSettingsAndInvalidSnoozeChoiceDoNotProduceEffects()
    {
        var clock=new Clock(); var session=new ActivitySession(new Source(clock),clock);
        var notifications=new Notifications(); var sounds=new Sounds();
        using var experience=new ReminderExperience(session,notifications,sounds,clock);
        var vm=new MainWindowViewModel(session); vm.AttachExperience(experience);
        vm.NotificationsEnabled=false; vm.SelectedSnooze=2; Assert.Equal(5,vm.SelectedSnooze);
        vm.SelectedInterval="Custom"; vm.CustomMinutes="1"; vm.StartCommand.Execute(null);
        for(var i=0;i<60;i++) { clock.Advance(); vm.Refresh(); }
        vm.StartBreakCommand.Execute(null); vm.CancelBreakCommand.Execute(null);
        Assert.Equal(0,notifications.Count); Assert.Empty(sounds.Cues);
        Assert.Contains("cancelled",vm.ExperienceResult);
        Assert.Throws<InvalidOperationException>(()=>vm.AttachExperience(experience));
    }
}
