using EyeCare4U.Core;

namespace EyeCare4U.Storage;

/// <summary>Called on the UI context. Snapshots are immutable before crossing into the store's worker queue.</summary>
public sealed class HistoryService(IHistoryStore store,HistoryCollector collector,TimeProvider? timeProvider=null)
{
    private readonly TimeProvider _time=timeProvider??TimeProvider.System;
    private long _epoch;
    private long _generation;
    private bool _initialized;
    private bool _clearing;
    public HistoryCollector Collector { get; }=collector;
    public string Status { get; private set; }="Loading local history…";
    public HistorySnapshot? Snapshot { get; private set; }
    public DateTimeOffset? LastSavedUtc { get; private set; }
    public event Action? Changed;

    public async Task<UserPreferences> InitializeAsync()
    {
        try
        {
            var initialized=await store.InitializeAsync(); _epoch=initialized.Epoch; _initialized=true;
            // History can be unreadable even though preferences were loaded successfully.
            // Never replace those valid preferences with defaults because a later query failed.
            try
            {
                Snapshot=await store.QueryAsync(DateOnly.FromDateTime(_time.GetLocalNow().Date));
                Status="Local history ready. Recent activity and preference edits wait for the next checkpoint.";
            }
            catch(Exception ex) { Status=Failure(ex); }
            return initialized.Preferences;
        }
        catch(Exception ex) { Status=Failure(ex); return new(); }
        finally { Changed?.Invoke(); }
    }
    public async Task CheckpointAsync(UserPreferences preferences)
    {
        if(_clearing) return;
        var generation=_generation;
        var batch=Collector.Snapshot(_epoch);
        var capturedUtc=_time.GetUtcNow();
        try
        {
            if(!_initialized)
            {
                var initialized=await store.InitializeAsync();
                if(generation!=_generation) return;
                _epoch=initialized.Epoch; _initialized=true; batch=batch with { Epoch=_epoch };
            }
            if(!await store.SaveAsync(batch,preferences)) throw new InvalidOperationException("History collection boundary changed.");
            if(generation!=_generation) return;
            Collector.Acknowledge(batch); var savedUtc=capturedUtc;
            var snapshot=await store.QueryAsync(DateOnly.FromDateTime(_time.GetLocalNow().Date));
            if(generation!=_generation) return;
            Snapshot=snapshot; LastSavedUtc=savedUtc;
            Status=$"Saved snapshot captured {LastSavedUtc.Value.ToLocalTime():HH:mm:ss}. Recent activity/preferences await the next ~15-second checkpoint.";
            if(Collector.DroppedEvents>0) Status+=" Oldest unsaved events exceeded the 5,000-event memory limit.";
        }
        catch(Exception ex)
        {
            if(generation!=_generation) return;
            Status=Failure(ex);
        }
        finally { Changed?.Invoke(); }
    }
    private static string Failure(Exception error)=>error is NotSupportedException
        ? "History is not being saved. "+error.Message
        : "History is not being saved. Storage unavailable or unreadable; existing data preserved. Retrying at checkpoints.";

    public async Task ClearAsync()
    {
        if(_clearing) return;
        _clearing=true; _generation++;
        Collector.ClearBoundary(); // Old in-memory history is never put back, even after an error/retry.
        Status="Clearing saved history; preferences and current timer/session estimate are kept."; Changed?.Invoke();
        var deleted=false;
        try
        {
            _epoch=await store.ClearHistoryAsync(); deleted=true; Snapshot=null; _initialized=true;
            Snapshot=await store.QueryAsync(DateOnly.FromDateTime(_time.GetLocalNow().Date));
            Status="History cleared. New collection starts at this boundary; preferences and session estimate are kept.";
            LastSavedUtc=null;
        }
        catch { Status=deleted ? "History cleared, but the dashboard could not reload. History is not being saved until storage recovers." : "History could not be cleared. Existing stored data was preserved; new collection continues from the request boundary."; }
        finally { _clearing=false; Changed?.Invoke(); }
    }
}
