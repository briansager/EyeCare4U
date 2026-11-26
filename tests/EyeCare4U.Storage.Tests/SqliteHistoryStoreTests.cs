using EyeCare4U.Core;
using Microsoft.Data.Sqlite;

namespace EyeCare4U.Storage.Tests;

public sealed class SqliteHistoryStoreTests
{
    private sealed class TempDatabase : IDisposable
    {
        public string Folder { get; }=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"EyeCare4UTests",Guid.NewGuid().ToString("N"));
        public string Path=>System.IO.Path.Combine(Folder,"history.db");
        public TempDatabase()=>Directory.CreateDirectory(Folder);
        public void Dispose()=>Directory.Delete(Folder,true);
        public void Sql(string sql)
        {
            using var connection=new SqliteConnection($"Data Source={Path};Pooling=False;Foreign Keys=True"); connection.Open();
            using var command=connection.CreateCommand(); command.CommandText=sql; command.ExecuteNonQuery();
        }
    }
    private static readonly DateOnly Today=new(2026,11,26);
    private static readonly DateTimeOffset Now=new(2026,11,26,8,0,0,TimeSpan.Zero);
    private static HistoryBatch Batch(long ticks=10_000_000,long epoch=0)=>new(epoch,Guid.NewGuid(),Now,
        [new(Guid.NewGuid(),"2026-11-26",480,"Test zone",ticks,Now,Now)],[]);

    [Fact]
    public async Task FirstRunAndRepeatedInitializationCreateVersionedSchemaWithDefaults()
    {
        using var temp=new TempDatabase(); var store=new SqliteHistoryStore(temp.Path);
        var first=await store.InitializeAsync(); var second=await store.InitializeAsync();
        Assert.Equal(0,first.Epoch); Assert.Equal(new UserPreferences(),second.Preferences);
        var snapshot=await store.QueryAsync(Today); Assert.Equal(7,snapshot.Days.Count); Assert.Empty(snapshot.Events);
        Assert.All(snapshot.Days,row=> { Assert.Null(row.ActiveTicks); Assert.Null(row.BreakTimersFinished); });
        temp.Sql("INSERT INTO collections VALUES('check','2026-11-26T00:00:00Z');");
        Assert.Throws<SqliteException>(()=>temp.Sql("INSERT INTO usage_totals VALUES('orphan','missing','2026-11-26',0,'UTC',1,'x','x');"));
    }
    [Fact]
    public async Task PreferencesRoundTripWithoutLiveTimerState()
    {
        using var temp=new TempDatabase(); var store=new SqliteHistoryStore(temp.Path); await store.InitializeAsync();
        var preferences=new UserPreferences { SelectedInterval="Custom",CustomMinutes=7,IdleThresholdSeconds=90,NotificationsEnabled=false,ReminderSoundEnabled=true,BreakSoundsEnabled=true,SnoozeMinutes=10 };
        await store.SaveAsync(Batch(),preferences);
        var restarted=await new SqliteHistoryStore(temp.Path).InitializeAsync(); Assert.Equal(preferences,restarted.Preferences);
        Assert.Equal(TimerState.Ready,new ReminderTimer().State);
    }
    [Theory]
    [InlineData("{bad",20,120,true)]
    [InlineData("{\"SelectedInterval\":\"bad\",\"CustomMinutes\":999,\"IdleThresholdSeconds\":29,\"SnoozeMinutes\":2,\"NotificationsEnabled\":false}",20,120,false)]
    [InlineData("{\"CustomMinutes\":8}",8,120,true)]
    public async Task InvalidOrMissingStoredFieldsFallBackWithoutCrashing(string json,int custom,int idle,bool notifications)
    {
        using var temp=new TempDatabase(); var store=new SqliteHistoryStore(temp.Path); await store.InitializeAsync();
        using(var connection=new SqliteConnection($"Data Source={temp.Path};Pooling=False"))
        {
            connection.Open(); using var command=connection.CreateCommand(); command.CommandText="INSERT INTO preferences VALUES(1,$json);";
            command.Parameters.AddWithValue("$json",json); command.ExecuteNonQuery();
        }
        var result=await store.InitializeAsync();
        Assert.Equal(custom,result.Preferences.CustomMinutes); Assert.Equal(idle,result.Preferences.IdleThresholdSeconds);
        Assert.Equal(notifications,result.Preferences.NotificationsEnabled); Assert.Equal("20",result.Preferences.SelectedInterval);
    }
    [Fact]
    public async Task CumulativeRetryOutOfOrderAndRestartCannotDuplicateTicks()
    {
        using var temp=new TempDatabase(); var store=new SqliteHistoryStore(temp.Path); await store.InitializeAsync();
        var first=Batch(TimeSpan.FromSeconds(10).Ticks); await store.SaveAsync(first,new()); await store.SaveAsync(first,new());
        var newer=first with { Usage=[first.Usage[0] with { ActiveTicks=TimeSpan.FromSeconds(12).Ticks }] };
        await store.SaveAsync(newer,new()); await store.SaveAsync(first,new());
        var restarted=new SqliteHistoryStore(temp.Path); await restarted.InitializeAsync();
        await restarted.SaveAsync(Batch(TimeSpan.FromSeconds(2).Ticks),new());
        var snapshot=await restarted.QueryAsync(Today);
        Assert.Equal(TimeSpan.FromSeconds(14).Ticks,snapshot.Days[0].ActiveTicks);
    }
    [Fact]
    public async Task EventsDeduplicateAndOnlyFinishedOutcomesCountWithFiftyRowBound()
    {
        using var temp=new TempDatabase(); var store=new SqliteHistoryStore(temp.Path); await store.InitializeAsync();
        var batch=Batch(0); var occurrence=Guid.NewGuid(); var breakId=Guid.NewGuid();
        var events=Enumerable.Range(0,70).Select(i=>new RecordedEvent(Guid.NewGuid(),occurrence,breakId,
            i==0?HistoryKind.BreakTimerFinished:i%2==0?HistoryKind.BreakCancelled:HistoryKind.BreakInterrupted,"reason",Now.AddSeconds(i),"2026-11-26",480,"Test zone",i)).ToArray();
        batch=batch with { Events=events }; await store.SaveAsync(batch,new()); await store.SaveAsync(batch,new());
        var snapshot=await store.QueryAsync(Today);
        Assert.Equal(50,snapshot.Events.Count); Assert.Equal(events[69].Id,snapshot.Events[0].Id);
        Assert.Equal(1,snapshot.Days[0].BreakTimersFinished); Assert.Equal(50,snapshot.Events.Select(e=>e.Id).Distinct().Count());
    }
    [Fact]
    public async Task ObservedZeroDiffersFromNoDataAndUnknownAttributionIsSeparate()
    {
        using var temp=new TempDatabase(); var store=new SqliteHistoryStore(temp.Path); await store.InitializeAsync();
        var batch=Batch(0); batch=batch with { Usage=[..batch.Usage,new(Guid.NewGuid(),null,null,"Changed zone",TimeSpan.FromSeconds(3).Ticks,Now,Now)] };
        await store.SaveAsync(batch,new()); var snapshot=await store.QueryAsync(Today);
        Assert.Equal(0,snapshot.Days[0].ActiveTicks); Assert.Equal(0,snapshot.Days[0].BreakTimersFinished);
        Assert.Null(snapshot.Days[1].ActiveTicks); Assert.Null(snapshot.Days[1].BreakTimersFinished);
        Assert.Equal(TimeSpan.FromSeconds(3).Ticks,snapshot.UnattributedTicks);
    }
    [Fact]
    public async Task QueuedWritesClearAndOldRetriesSerializeWithoutResurrectionAndKeepPreferences()
    {
        using var temp=new TempDatabase(); var store=new SqliteHistoryStore(temp.Path); await store.InitializeAsync();
        var preferences=new UserPreferences { CustomMinutes=17 }; var batch=Batch();
        var write=store.SaveAsync(batch,preferences); var deletion=store.ClearHistoryAsync(); var stale=store.SaveAsync(batch,preferences);
        Assert.True(await write); var epoch=await deletion; Assert.False(await stale);
        Assert.Equal(1,epoch); Assert.Equal(preferences,(await store.InitializeAsync()).Preferences);
        Assert.Null((await store.QueryAsync(Today)).Days[0].ActiveTicks);
        Assert.True(await store.SaveAsync(Batch(epoch:epoch),preferences));
        Assert.Equal(10_000_000,(await store.QueryAsync(Today)).Days[0].ActiveTicks);
    }
    [Fact]
    public async Task NewerSchemaAndCorruptFilesArePreserved()
    {
        using var temp=new TempDatabase(); temp.Sql("CREATE TABLE marker(value TEXT); INSERT INTO marker VALUES('keep'); PRAGMA user_version=99;");
        var store=new SqliteHistoryStore(temp.Path);
        await Assert.ThrowsAsync<NotSupportedException>(()=>store.InitializeAsync());
        await Assert.ThrowsAsync<NotSupportedException>(()=>store.SaveAsync(Batch(),new()));
        using(var connection=new SqliteConnection($"Data Source={temp.Path};Pooling=False"))
        { connection.Open(); using var command=connection.CreateCommand(); command.CommandText="SELECT value FROM marker"; Assert.Equal("keep",command.ExecuteScalar()); }
        File.Delete(temp.Path); var corrupt=new byte[]{1,5,7,9,11,13}; await File.WriteAllBytesAsync(temp.Path,corrupt);
        await Assert.ThrowsAsync<SqliteException>(()=>new SqliteHistoryStore(temp.Path).InitializeAsync());
        Assert.Equal(corrupt,await File.ReadAllBytesAsync(temp.Path));
    }
}
