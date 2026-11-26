using System.IO;
using EyeCare4U.App.Platform;
using EyeCare4U.App.ViewModels;
using EyeCare4U.Core;
using EyeCare4U.Storage;

namespace EyeCare4U.App.Tests;

public sealed class ReleaseCandidateTests
{
    private sealed class Clock : TimeProvider
    {
        public long Ticks;
        public override long TimestampFrequency=>TimeSpan.TicksPerSecond;
        public override long GetTimestamp()=>Ticks;
        public override DateTimeOffset GetUtcNow()=>new DateTimeOffset(2026,11,26,8,0,0,TimeSpan.Zero).AddTicks(Ticks);
        public override TimeZoneInfo LocalTimeZone=>TimeZoneInfo.Utc;
        public void Advance(int seconds)=>Ticks+=TimeSpan.FromSeconds(seconds).Ticks;
    }
    private sealed class Source(Clock clock) : IActivitySource
    {
        public ActivityReading Read()=>new(true,TimeSpan.Zero,(ulong)(clock.Ticks/10_000),false);
    }
    private sealed class Store : IHistoryStore
    {
        public bool QueryFails;
        public RecordedEvent[] Events=[];
        public UserPreferences Preferences=new() { CustomMinutes=7, SelectedInterval="Custom",SnoozeMinutes=10 };
        public Task<StorageInitialization> InitializeAsync()=>Task.FromResult(new StorageInitialization(Preferences,0));
        public Task<bool> SaveAsync(HistoryBatch batch,UserPreferences preferences)=>Task.FromResult(true);
        public Task<long> ClearHistoryAsync()=>Task.FromResult(1L);
        public Task<HistorySnapshot> QueryAsync(DateOnly today)=>QueryFails?Task.FromException<HistorySnapshot>(new IOException("test failure")):
            Task.FromResult(new HistorySnapshot(today,[new(today,0,0)],Events,0));
    }
    [Fact]
    public async Task UnreadableHistoryDoesNotReplaceSuccessfullyLoadedPreferences()
    {
        var store=new Store { QueryFails=true }; var service=new HistoryService(store,new HistoryCollector());
        Assert.Equal(store.Preferences,await service.InitializeAsync());
        Assert.Null(service.Snapshot); Assert.Contains("not being saved",service.Status);
    }
    [Fact]
    public async Task CheckpointOwnsUnexpectedTaskFailureAndAnnouncesOnlyHealthTransitions()
    {
        var service=new HistoryService(new Store(),new HistoryCollector()); await service.InitializeAsync();
        var fail=true;
        using var vm=new HistoryViewModel(service,()=>fail?throw new InvalidOperationException("private diagnostic must not escape"):new(),"test");
        var announcements=0; vm.PropertyChanged+=(_,e)=>{if(e.PropertyName==nameof(vm.Announcement))announcements++;};
        await vm.CheckpointAsync(); await vm.WaitForPendingAsync();
        Assert.False(vm.IsBusy); Assert.Contains("not being saved",vm.Status); Assert.DoesNotContain("private",vm.Status);
        await vm.CheckpointAsync(); Assert.Equal(1,announcements);
        fail=false; await vm.CheckpointAsync(); Assert.Equal("History saving recovered.",vm.Announcement);
        var row=vm.Days[0]; await vm.CheckpointAsync(); Assert.Equal(2,announcements); Assert.Same(row,vm.Days[0]);
    }
    [Fact]
    public async Task ConfirmedClearInPopulatedIsolatedSqliteKeepsLiveSessionAndPreferencesAcrossRestart()
    {
        var folder=Path.Combine(Path.GetTempPath(),"EyeCare4UTests",Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
        try
        {
            var clock=new Clock(); var collector=new HistoryCollector(clock,()=>TimeZoneInfo.Utc);
            var session=new ActivitySession(new Source(clock),clock); session.Observed+=collector.Observe;
            collector.Observe(session.LastObservation!);
            var timer=new MainWindowViewModel(session); timer.SelectedInterval="Custom"; timer.CustomMinutes="3";
            timer.StartCommand.Execute(null); clock.Advance(2); timer.Refresh();
            var path=Path.Combine(folder,"history.db"); var store=new SqliteHistoryStore(path);
            var service=new HistoryService(store,collector,clock); await service.InitializeAsync();
            using var history=new HistoryViewModel(service,timer.CapturePreferences,path);
            collector.Record(new(Guid.NewGuid(),Guid.NewGuid(),null,HistoryKind.ReminderDue,""));
            var old=collector.Snapshot(0); await history.CheckpointAsync();
            Assert.Single(history.Events); var remaining=timer.RemainingText; var estimate=session.EstimatedActiveTime;
            history.ConfirmClearCommand.Execute(null); Assert.Single(history.Events); // No confirmation yet.
            history.ClearHistoryCommand.Execute(null); Assert.True(history.ConfirmClearCommand.CanExecute(null));
            history.ConfirmClearCommand.Execute(null); await history.WaitForPendingAsync();
            Assert.Empty(history.Events); Assert.Equal("No recorded data",history.Days[0].ActiveTime);
            Assert.Equal(TimerState.Running,timer.State); Assert.Equal(remaining,timer.RemainingText); Assert.Equal(estimate,session.EstimatedActiveTime);
            Assert.False(await store.SaveAsync(old,timer.CapturePreferences()));
            Assert.Equal(timer.CapturePreferences(),(await new SqliteHistoryStore(path).InitializeAsync()).Preferences);
            clock.Advance(3); timer.Refresh(); collector.Record(new(Guid.NewGuid(),Guid.NewGuid(),null,HistoryKind.CycleReset,""));
            await history.CheckpointAsync(); await history.CheckpointAsync();
            var restarted=new SqliteHistoryStore(path); await restarted.InitializeAsync(); var saved=await restarted.QueryAsync(new(2026,11,26));
            Assert.Equal(TimeSpan.FromSeconds(3).Ticks,saved.Days[0].ActiveTicks);
            Assert.Equal(HistoryKind.CycleReset,Assert.Single(saved.Events).Kind);
            Assert.Equal(TimeSpan.FromSeconds(5),session.EstimatedActiveTime);
        }
        finally { Directory.Delete(folder,true); }
    }
    [Fact]
    public async Task HistoryDateAndRecordedOffsetStayGregorianUnderAnotherCalendarCulture()
    {
        var before=System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture=new("th-TH");
            var clock=new Clock(); var utc=clock.GetUtcNow();
            var store=new Store { Events=[new(Guid.NewGuid(),Guid.NewGuid(),null,HistoryKind.ReminderDue,"",utc,"2026-11-26",480,"Test zone",1)] };
            var service=new HistoryService(store,new HistoryCollector(clock,()=>TimeZoneInfo.Utc),clock); await service.InitializeAsync();
            using var vm=new HistoryViewModel(service,()=>new(),"test");
            Assert.Equal("2026-11-26",vm.TodayDate);
            Assert.Equal("2026-11-26 16:00:00 +08:00",Assert.Single(vm.Events).When);
        }
        finally { System.Globalization.CultureInfo.CurrentCulture=before; }
    }
    [Fact]
    public void IsolatedReviewCannotSelectNormalDataOrAnUnmarkedDirectory()
    {
        Assert.Equal(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"EyeCare4U"),AppDataLocation.Resolve([]));
        Assert.Throws<ArgumentException>(()=>AppDataLocation.Resolve(["--isolated-review",Path.GetTempPath()]));
        Assert.Throws<ArgumentException>(()=>AppDataLocation.Resolve(["--isolated-review","relative"]));
        var folder=Path.Combine(Path.GetTempPath(),"EyeCare4U-Review",Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
        try
        {
            Assert.Throws<ArgumentException>(()=>AppDataLocation.Resolve(["--isolated-review",folder]));
            File.WriteAllText(Path.Combine(folder,".eyecare4u-review"),"Isolated test data");
            Assert.Equal(folder,AppDataLocation.Resolve(["--isolated-review",folder]));
        }
        finally { Directory.Delete(folder,true); }
    }
}
