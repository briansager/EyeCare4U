using EyeCare4U.App.Platform;
using EyeCare4U.App.ViewModels;
using EyeCare4U.Core;
using EyeCare4U.Storage;

namespace EyeCare4U.App.Tests;

public sealed class PersistenceViewModelTests
{
    private sealed class Clock : TimeProvider
    {
        public long Ticks;
        public override long TimestampFrequency=>TimeSpan.TicksPerSecond;
        public override long GetTimestamp()=>Ticks;
    }
    private sealed class Source(Clock clock) : IActivitySource
    {
        public ActivityReading Read()=>new(true,TimeSpan.FromSeconds(60)+TimeSpan.FromTicks(clock.Ticks),1,false);
    }
    private sealed class Notifications : IReminderNotifications { public void ShowDue(){} public void Clear(){} public void Dispose(){} }
    private sealed class Sounds : IReminderSounds { public void Play(SoundCue cue){} public void Stop(){} public void Dispose(){} }
    [Fact]
    public void PreferencesRestoreValidSettingsIntoReadyWithFreshMonitoringAndNoCountdownResume()
    {
        var clock=new Clock(); var session=new ActivitySession(new Source(clock),clock,idleThresholdSeconds:30);
        using var experience=new ReminderExperience(session,new Notifications(),new Sounds(),clock);
        var vm=new MainWindowViewModel(session); vm.AttachExperience(experience);
        var preferences=new UserPreferences { SelectedInterval="Custom",CustomMinutes=3,IdleThresholdSeconds=30,NotificationsEnabled=false,ReminderSoundEnabled=true,BreakSoundsEnabled=true,SnoozeMinutes=10 };
        vm.RestorePreferences(preferences); clock.Ticks+=TimeSpan.TicksPerSecond; vm.Refresh();
        Assert.Equal(preferences,vm.CapturePreferences()); Assert.Equal(TimerState.Ready,vm.State);
        Assert.Equal("03:00",vm.RemainingText); Assert.Equal(ReminderMode.Interval,experience.Mode);
        Assert.Equal(TimeSpan.Zero,session.EstimatedActiveTime); Assert.Equal(ActivityStatus.Idle,session.Status);
        vm.CustomMinutes="bad"; vm.IdleThresholdText="bad";
        Assert.Equal(3,vm.CapturePreferences().CustomMinutes); Assert.Equal(30,vm.CapturePreferences().IdleThresholdSeconds);
    }
    [Fact]
    public void SingleInstanceGuardRejectsSecondOwnerAndReleasesCleanly()
    {
        var name="Local\\EyeCare4U-test-"+Guid.NewGuid().ToString("N");
        using(var first=new SingleInstanceGuard(name))
        {
            Assert.True(first.IsOwner); using var second=new SingleInstanceGuard(name); Assert.False(second.IsOwner);
        }
        using var next=new SingleInstanceGuard(name); Assert.True(next.IsOwner);
    }
    private sealed class Store : IHistoryStore
    {
        public int Clears;
        public HistorySnapshot Snapshot { get; set; }=new(new(2026,11,26),[new(new(2026,11,26),0,0)],[] ,0);
        public Task<StorageInitialization> InitializeAsync()=>Task.FromResult(new StorageInitialization(new(),0));
        public Task<bool> SaveAsync(HistoryBatch batch,UserPreferences preferences)=>Task.FromResult(true);
        public Task<long> ClearHistoryAsync() { Clears++; Snapshot=Snapshot with { Days=[new(new(2026,11,26),null,null)] }; return Task.FromResult(1L); }
        public Task<HistorySnapshot> QueryAsync(DateOnly today)=>Task.FromResult(Snapshot);
    }
    [Fact]
    public async Task HistoryRefreshPreservesRowsAndClearRequiresItsInAppConfirmation()
    {
        var store=new Store(); var service=new HistoryService(store,new HistoryCollector()); await service.InitializeAsync();
        using var vm=new HistoryViewModel(service,()=>new(),"test path");
        var row=vm.Days[0]; await vm.CheckpointAsync(); Assert.Same(row,vm.Days[0]);
        vm.ClearHistoryCommand.Execute(null); Assert.True(vm.ClearConfirmationVisible); Assert.Equal(0,store.Clears);
        vm.CancelClearCommand.Execute(null); Assert.False(vm.ClearConfirmationVisible); Assert.Equal(0,store.Clears);
        vm.ClearHistoryCommand.Execute(null); vm.ConfirmClearCommand.Execute(null); await vm.WaitForPendingAsync();
        Assert.Equal(1,store.Clears); Assert.False(vm.ClearConfirmationVisible); Assert.Same(row,vm.Days[0]);
        Assert.Equal("No recorded data",vm.Days[0].ActiveTime);
    }
}
