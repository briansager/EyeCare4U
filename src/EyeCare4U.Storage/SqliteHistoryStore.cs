using System.Globalization;
using System.Text.Json;
using EyeCare4U.Core;
using Microsoft.Data.Sqlite;

namespace EyeCare4U.Storage;

/// <summary>FIFO worker queue; every SQLite operation, including initialization, runs off the caller thread.</summary>
public sealed class SqliteHistoryStore(string databasePath) : IHistoryStore
{
    private readonly object _queueLock = new();
    private Task _tail = Task.CompletedTask;
    public string DatabasePath { get; } = Path.GetFullPath(databasePath);
    private Task<T> Enqueue<T>(Func<T> work)
    {
        lock (_queueLock)
        {
            var next = _tail.ContinueWith(_ => work(), CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
            _tail = next.ContinueWith(_ => { }, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
            return next;
        }
    }
    private SqliteConnection Open()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(DatabasePath)!);
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        { DataSource=DatabasePath, ForeignKeys=true, Pooling=false, DefaultTimeout=2 }.ToString());
        try
        {
            connection.Open();
            using var transaction=connection.BeginTransaction();
            var version=Convert.ToInt32(Scalar(connection,transaction,"PRAGMA user_version;"),CultureInfo.InvariantCulture);
            if(version>1) throw new NotSupportedException("This database uses a newer schema. Existing data was preserved.");
            if(version==0)
            {
                var tables=Convert.ToInt32(Scalar(connection,transaction,"SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%';"),CultureInfo.InvariantCulture);
                if(tables!=0) throw new NotSupportedException("Unrecognized unversioned database. Existing data was preserved.");
                Execute(connection,transaction,"""
                    CREATE TABLE metadata(key TEXT PRIMARY KEY, value INTEGER NOT NULL);
                    INSERT INTO metadata VALUES('history_epoch',0);
                    CREATE TABLE preferences(id INTEGER PRIMARY KEY CHECK(id=1), json TEXT NOT NULL);
                    CREATE TABLE collections(id TEXT PRIMARY KEY, started_utc TEXT NOT NULL);
                    CREATE TABLE usage_totals(
                        id TEXT PRIMARY KEY, collection_id TEXT NOT NULL REFERENCES collections(id) ON DELETE CASCADE,
                        local_date TEXT NULL, offset_minutes INTEGER NULL, zone_id TEXT NOT NULL,
                        active_ticks INTEGER NOT NULL CHECK(active_ticks>=0), first_utc TEXT NOT NULL, last_utc TEXT NOT NULL);
                    CREATE INDEX ix_usage_date ON usage_totals(local_date);
                    CREATE TABLE events(
                        id TEXT PRIMARY KEY, collection_id TEXT NOT NULL REFERENCES collections(id) ON DELETE CASCADE,
                        occurrence_id TEXT NOT NULL, break_id TEXT NULL, kind TEXT NOT NULL, reason TEXT NOT NULL,
                        utc TEXT NOT NULL, local_date TEXT NOT NULL, offset_minutes INTEGER NOT NULL, zone_id TEXT NOT NULL, sequence INTEGER NOT NULL);
                    CREATE INDEX ix_events_recent ON events(utc DESC,sequence DESC,id);
                    CREATE INDEX ix_events_date_kind ON events(local_date,kind);
                    PRAGMA user_version=1;
                    """);
            }
            transaction.Commit();
            return connection;
        }
        catch { connection.Dispose(); throw; }
    }
    private static SqliteCommand Command(SqliteConnection connection, SqliteTransaction? transaction,string sql,params (string,object?)[] args)
    {
        var command=connection.CreateCommand(); command.Transaction=transaction; command.CommandText=sql;
        foreach(var (name,value) in args) command.Parameters.AddWithValue(name,value??DBNull.Value);
        return command;
    }
    private static object? Scalar(SqliteConnection c,SqliteTransaction? t,string sql,params (string,object?)[] args)
    { using var cmd=Command(c,t,sql,args); return cmd.ExecuteScalar(); }
    private static void Execute(SqliteConnection c,SqliteTransaction? t,string sql,params (string,object?)[] args)
    { using var cmd=Command(c,t,sql,args); cmd.ExecuteNonQuery(); }
    private static string Utc(DateTimeOffset value)=>value.ToUniversalTime().ToString("O",CultureInfo.InvariantCulture);
    private static string Day(DateOnly value)=>value.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture);

    public Task<StorageInitialization> InitializeAsync()=>Enqueue(()=>
    {
        using var connection=Open();
        var json=Scalar(connection,null,"SELECT json FROM preferences WHERE id=1;") as string;
        UserPreferences preferences;
        try { preferences=(json is null?null:JsonSerializer.Deserialize<UserPreferences>(json))?.Validated()??new(); }
        catch(JsonException) { preferences=new(); }
        var epoch=Convert.ToInt64(Scalar(connection,null,"SELECT value FROM metadata WHERE key='history_epoch';"),CultureInfo.InvariantCulture);
        return new StorageInitialization(preferences,epoch);
    });

    public Task<bool> SaveAsync(HistoryBatch batch,UserPreferences preferences)=>Enqueue(()=>
    {
        using var connection=Open(); using var transaction=connection.BeginTransaction();
        var epoch=Convert.ToInt64(Scalar(connection,transaction,"SELECT value FROM metadata WHERE key='history_epoch';"),CultureInfo.InvariantCulture);
        if(epoch!=batch.Epoch) return false; // A stale retry after deletion cannot resurrect history.
        Execute(connection,transaction,"INSERT INTO collections(id,started_utc) VALUES($id,$utc) ON CONFLICT(id) DO NOTHING;",
            ("$id",batch.CollectionId.ToString()),("$utc",Utc(batch.StartedUtc)));
        foreach(var row in batch.Usage)
            Execute(connection,transaction,"""
                INSERT INTO usage_totals VALUES($id,$collection,$date,$offset,$zone,$ticks,$first,$last)
                ON CONFLICT(id) DO UPDATE SET active_ticks=MAX(usage_totals.active_ticks,excluded.active_ticks),
                    first_utc=MIN(usage_totals.first_utc,excluded.first_utc),last_utc=MAX(usage_totals.last_utc,excluded.last_utc);
                """,("$id",row.Id.ToString()),("$collection",batch.CollectionId.ToString()),("$date",row.LocalDate),
                ("$offset",row.OffsetMinutes),("$zone",row.ZoneId),("$ticks",row.ActiveTicks),("$first",Utc(row.FirstUtc)),("$last",Utc(row.LastUtc)));
        foreach(var row in batch.Events)
            Execute(connection,transaction,"""
                INSERT INTO events VALUES($id,$collection,$occurrence,$break,$kind,$reason,$utc,$date,$offset,$zone,$sequence)
                ON CONFLICT(id) DO NOTHING;
                """,("$id",row.Id.ToString()),("$collection",batch.CollectionId.ToString()),("$occurrence",row.OccurrenceId.ToString()),
                ("$break",row.BreakId?.ToString()),("$kind",row.Kind.ToString()),("$reason",row.Reason),("$utc",Utc(row.Utc)),
                ("$date",row.LocalDate),("$offset",row.OffsetMinutes),("$zone",row.ZoneId),("$sequence",row.Sequence));
        Execute(connection,transaction,"INSERT INTO preferences(id,json) VALUES(1,$json) ON CONFLICT(id) DO UPDATE SET json=excluded.json;",
            ("$json",JsonSerializer.Serialize(preferences.Validated())));
        transaction.Commit(); return true;
    });

    public Task<long> ClearHistoryAsync()=>Enqueue(()=>
    {
        using var connection=Open(); using var transaction=connection.BeginTransaction();
        Execute(connection,transaction,"DELETE FROM events; DELETE FROM usage_totals; DELETE FROM collections; UPDATE metadata SET value=value+1 WHERE key='history_epoch';");
        var epoch=Convert.ToInt64(Scalar(connection,transaction,"SELECT value FROM metadata WHERE key='history_epoch';"),CultureInfo.InvariantCulture);
        transaction.Commit(); return epoch;
    });

    public Task<HistorySnapshot> QueryAsync(DateOnly today)=>Enqueue(()=>
    {
        using var connection=Open(); using var transaction=connection.BeginTransaction();
        var days=new List<DailyHistory>();
        // Exactly seven local calendar labels, with indexed per-date aggregates.
        for(var offset=0;offset<7;offset++)
        {
            var date=today.AddDays(-offset); var label=Day(date);
            using var usage=Command(connection,transaction,"SELECT COUNT(*),COALESCE(SUM(active_ticks),0) FROM usage_totals WHERE local_date=$date;",("$date",label));
            using var ur=usage.ExecuteReader(); ur.Read(); var observations=ur.GetInt64(0); var ticks=ur.GetInt64(1); ur.Close();
            using var events=Command(connection,transaction,"SELECT COUNT(*),COALESCE(SUM(CASE WHEN kind='BreakTimerFinished' THEN 1 ELSE 0 END),0) FROM events WHERE local_date=$date;",("$date",label));
            using var er=events.ExecuteReader(); er.Read(); var records=er.GetInt64(0); var finished=er.GetInt32(1);
            days.Add(new(date,observations==0?null:ticks,observations+records==0?null:finished));
        }
        var recent=new List<RecordedEvent>();
        using(var cmd=Command(connection,transaction,"SELECT id,occurrence_id,break_id,kind,reason,utc,local_date,offset_minutes,zone_id,sequence FROM events ORDER BY utc DESC,sequence DESC,id LIMIT 50;"))
        using(var reader=cmd.ExecuteReader())
            while(reader.Read()) recent.Add(new(Guid.Parse(reader.GetString(0)),Guid.Parse(reader.GetString(1)),reader.IsDBNull(2)?null:Guid.Parse(reader.GetString(2)),
                Enum.Parse<HistoryKind>(reader.GetString(3)),reader.GetString(4),DateTimeOffset.Parse(reader.GetString(5),CultureInfo.InvariantCulture),
                reader.GetString(6),reader.GetInt32(7),reader.GetString(8),reader.GetInt64(9)));
        var unattributed=Convert.ToInt64(Scalar(connection,transaction,"SELECT COALESCE(SUM(active_ticks),0) FROM usage_totals WHERE local_date IS NULL;"),CultureInfo.InvariantCulture);
        transaction.Commit(); return new HistorySnapshot(today,days,recent,unattributed);
    });
}
