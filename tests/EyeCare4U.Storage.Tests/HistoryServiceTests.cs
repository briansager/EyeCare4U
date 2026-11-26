using EyeCare4U.Core;

namespace EyeCare4U.Storage.Tests;

public sealed class HistoryServiceTests
{
    private sealed class Clock : TimeProvider
    {
        public long Ticks;
        public DateTimeOffset Utc=new(2026,11,26,8,0,0,TimeSpan.Zero);
        public override long TimestampFrequency=>TimeSpan.TicksPerSecond;
        public override long GetTimestamp()=>Ticks;
        public override DateTimeOffset GetUtcNow()=>Utc;
        public override TimeZoneInfo LocalTimeZone=>TimeZoneInfo.Utc;
        public void Advance(double seconds) { Ticks+=TimeSpan.FromSeconds(seconds).Ticks; Utc=Utc.AddSeconds(seconds); }
    }
    private sealed class SlowReturnStore(IHistoryStore inner) : IHistoryStore
    {
        public TaskCompletionSource Written { get; }=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; }=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Delay=true;
        public Task<StorageInitialization> InitializeAsync()=>inner.InitializeAsync();
        public Task<HistorySnapshot> QueryAsync(DateOnly today)=>inner.QueryAsync(today);
        public Task<long> ClearHistoryAsync()=>inner.ClearHistoryAsync();
        public async Task<bool> SaveAsync(HistoryBatch batch,UserPreferences preferences)
        {
            var result=await inner.SaveAsync(batch,preferences);
            if(Delay) { Written.TrySetResult(); await Release.Task; }
            return result;
        }
    }
    [Fact]
    public async Task ClearDuringPendingCheckpointCannotRestoreDeletedSnapshotOrMemoryTotals()
    {
        var folder=Path.Combine(Path.GetTempPath(),"EyeCare4UTests",Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
        try
        {
            var clock=new Clock(); var collector=new HistoryCollector(clock,()=>TimeZoneInfo.Utc);
            var store=new SlowReturnStore(new SqliteHistoryStore(Path.Combine(folder,"history.db")));
            var service=new HistoryService(store,collector,clock); await service.InitializeAsync();
            collector.Observe(new(0,clock.Utc,TimeSpan.Zero,[]));
            clock.Advance(2); collector.Observe(new(clock.Ticks,clock.Utc,TimeSpan.FromSeconds(2),[new(TimeSpan.Zero,TimeSpan.FromSeconds(2))]));
            var before=service.CheckpointAsync(new()); await store.Written.Task;
            await service.ClearAsync();
            Assert.Null(service.Snapshot!.Days[0].ActiveTicks);
            store.Release.SetResult(); await before;
            Assert.Null(service.Snapshot.Days[0].ActiveTicks);
            Assert.Empty(collector.Snapshot(1).Usage);
            store.Delay=false; clock.Advance(1);
            collector.Observe(new(clock.Ticks,clock.Utc,TimeSpan.FromSeconds(1),[new(TimeSpan.Zero,TimeSpan.FromSeconds(1))]));
            await service.CheckpointAsync(new());
            Assert.Equal(TimeSpan.FromSeconds(1).Ticks,service.Snapshot!.Days[0].ActiveTicks);
        }
        finally { Directory.Delete(folder,true); }
    }
    [Fact]
    public async Task UnavailableStorageRecoversWithoutDuplicateRetryOrFalseDurability()
    {
        var folder=Path.Combine(Path.GetTempPath(),"EyeCare4UTests",Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
        try
        {
            var blocked=Path.Combine(folder,"blocked"); await File.WriteAllTextAsync(blocked,"temporary test obstruction");
            var clock=new Clock(); var collector=new HistoryCollector(clock,()=>TimeZoneInfo.Utc);
            var service=new HistoryService(new SqliteHistoryStore(Path.Combine(blocked,"history.db")),collector,clock);
            Assert.Equal(new UserPreferences(),await service.InitializeAsync()); Assert.Contains("not being saved",service.Status);
            Assert.Null(service.Snapshot); Assert.Null(service.LastSavedUtc);
            collector.Observe(new(0,clock.Utc,TimeSpan.Zero,[])); clock.Advance(2);
            collector.Observe(new(clock.Ticks,clock.Utc,TimeSpan.FromSeconds(2),[new(TimeSpan.Zero,TimeSpan.FromSeconds(2))]));
            await service.CheckpointAsync(new()); Assert.Null(service.LastSavedUtc);
            File.Delete(blocked); Directory.CreateDirectory(blocked);
            await service.CheckpointAsync(new()); await service.CheckpointAsync(new());
            Assert.Equal(TimeSpan.FromSeconds(2).Ticks,service.Snapshot!.Days[0].ActiveTicks); Assert.NotNull(service.LastSavedUtc);
        }
        finally { Directory.Delete(folder,true); }
    }
    [Fact]
    public async Task OrderlyFinalCheckpointPersistsLastAcceptedSampleAndPendingEvent()
    {
        var folder=Path.Combine(Path.GetTempPath(),"EyeCare4UTests",Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
        try
        {
            var clock=new Clock(); var collector=new HistoryCollector(clock,()=>TimeZoneInfo.Utc); var path=Path.Combine(folder,"history.db");
            var service=new HistoryService(new SqliteHistoryStore(path),collector,clock); await service.InitializeAsync();
            collector.Observe(new(0,clock.Utc,TimeSpan.Zero,[])); await service.CheckpointAsync(new());
            clock.Advance(.75); collector.Observe(new(clock.Ticks,clock.Utc,TimeSpan.FromSeconds(.75),[new(TimeSpan.Zero,TimeSpan.FromSeconds(.75))]));
            collector.Record(new(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),HistoryKind.BreakTimerFinished,""));
            await service.CheckpointAsync(new UserPreferences { SnoozeMinutes=10 }); // Same awaited final-checkpoint API as Close/Exit.
            var restarted=new SqliteHistoryStore(path); Assert.Equal(10,(await restarted.InitializeAsync()).Preferences.SnoozeMinutes);
            var history=await restarted.QueryAsync(new(2026,11,26));
            Assert.Equal(TimeSpan.FromSeconds(.75).Ticks,history.Days[0].ActiveTicks); Assert.Equal(1,history.Days[0].BreakTimersFinished);
        }
        finally { Directory.Delete(folder,true); }
    }
}
