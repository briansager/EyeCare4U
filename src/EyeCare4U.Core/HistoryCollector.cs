namespace EyeCare4U.Core;

/// <summary>UI-thread collector of accepted estimator windows, not a second estimator.</summary>
public sealed class HistoryCollector
{
    private readonly TimeProvider _time;
    private readonly Func<TimeZoneInfo> _zoneProvider;
    private readonly Dictionary<string, UsageTotal> _usage = [];
    private readonly List<RecordedEvent> _events = [];
    private ActivityObservation? _anchor;
    private TimeZoneInfo? _zone;
    private long _sequence;
    private bool _mappingInvalid;
    private long _boundaryTimestamp;
    private long? _lastObservationTimestamp;

    public HistoryCollector(TimeProvider? time = null, Func<TimeZoneInfo>? zoneProvider = null)
    {
        _time = time ?? TimeProvider.System;
        _zoneProvider = zoneProvider ?? (() => TimeZoneInfo.Local);
        StartedUtc = _time.GetUtcNow();
        _boundaryTimestamp = _time.GetTimestamp();
        _anchor = new(_boundaryTimestamp, StartedUtc, TimeSpan.Zero, []);
        _zone = _zoneProvider();
    }
    public Guid CollectionId { get; private set; } = Guid.NewGuid();
    public DateTimeOffset StartedUtc { get; private set; }
    public int DroppedEvents { get; private set; }
    public void InvalidateMapping() => _mappingInvalid = true;

    public void Observe(ActivityObservation observation)
    {
        if (observation.Timestamp < _boundaryTimestamp ||
            (_lastObservationTimestamp is { } last && observation.Timestamp <= last)) return;
        _lastObservationTimestamp = observation.Timestamp;
        var zone = _zoneProvider();
        var sinceAnchor = _anchor is null ? TimeSpan.Zero : _time.GetElapsedTime(_anchor.Timestamp, observation.Timestamp);
        var reliable = !_mappingInvalid && _anchor is { } previous && _zone is not null &&
            zone.Id == _zone.Id && zone.HasSameRules(_zone) && observation.Span > TimeSpan.Zero &&
            sinceAnchor >= TimeSpan.Zero && sinceAnchor <= observation.Span + TimeSpan.FromMilliseconds(1) &&
            (observation.Utc - previous.Utc - sinceAnchor).Duration() <= TimeSpan.FromMilliseconds(250);
        // A reliable observation with zero credited activity is different from an unobserved day.
        var local = TimeZoneInfo.ConvertTime(observation.Utc, zone);
        Add(local.Date.ToString("yyyy-MM-dd",System.Globalization.CultureInfo.InvariantCulture), (int)local.Offset.TotalMinutes, zone.Id, 0, observation.Utc, observation.Utc);
        var earliest=observation.Span-_time.GetElapsedTime(_boundaryTimestamp,observation.Timestamp);
        foreach (var candidate in observation.Windows)
        {
            var start=candidate.Start>earliest?candidate.Start:earliest;
            if(start<TimeSpan.Zero) start=TimeSpan.Zero;
            var end=candidate.Start+candidate.Duration;
            if(end>observation.Span) end=observation.Span;
            if(end<=start) continue;
            var window=new ActivityWindow(start,end-start);
            if (!reliable || _anchor is null)
                Add(null, null, zone.Id, window.Duration.Ticks, observation.Utc, observation.Utc);
            else Split(_anchor.Utc + sinceAnchor - observation.Span + window.Start, window.Duration, zone);
        }
        _anchor = observation;
        _zone = zone;
        _mappingInvalid = false;
    }

    private static string Key(DateTimeOffset utc, TimeZoneInfo zone)
    {
        var local = TimeZoneInfo.ConvertTime(utc, zone);
        return local.Date.ToString("yyyy-MM-dd",System.Globalization.CultureInfo.InvariantCulture) + "|" + local.Offset.TotalMinutes.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }
    private void Split(DateTimeOffset start, TimeSpan duration, TimeZoneInfo zone)
    {
        var remaining = duration.Ticks;
        while (remaining > 0)
        {
            var length = remaining;
            if (Key(start,zone) != Key(start.AddTicks(remaining-1),zone))
            {
                long low=1, high=remaining;
                while (low<high)
                {
                    var middle=low+(high-low)/2;
                    if (Key(start.AddTicks(middle),zone)==Key(start,zone)) low=middle+1; else high=middle;
                }
                length=low;
            }
            var local=TimeZoneInfo.ConvertTime(start,zone);
            Add(local.Date.ToString("yyyy-MM-dd",System.Globalization.CultureInfo.InvariantCulture),(int)local.Offset.TotalMinutes,zone.Id,length,start,start.AddTicks(length));
            start=start.AddTicks(length); remaining-=length;
        }
    }
    private void Add(string? date,int? offset,string zone,long ticks,DateTimeOffset first,DateTimeOffset last)
    {
        var key=$"{date ?? "unknown"}|{offset}|{zone}";
        if (_usage.TryGetValue(key,out var row))
            _usage[key]=row with { ActiveTicks=checked(row.ActiveTicks+ticks), FirstUtc=first<row.FirstUtc?first:row.FirstUtc, LastUtc=last>row.LastUtc?last:row.LastUtc };
        else _usage[key]=new(Guid.NewGuid(),date,offset,zone,ticks,first,last);
    }
    public void Record(ReminderLifecycle lifecycle)
    {
        var utc=_time.GetUtcNow(); var zone=_zoneProvider(); var local=TimeZoneInfo.ConvertTime(utc,zone);
        if (_events.Any(e=>e.Id==lifecycle.Id)) return;
        if (_events.Count>=5000) { _events.RemoveAt(0); DroppedEvents++; }
        _events.Add(new(lifecycle.Id,lifecycle.OccurrenceId,lifecycle.BreakId,lifecycle.Kind,lifecycle.Reason,utc,
            local.Date.ToString("yyyy-MM-dd",System.Globalization.CultureInfo.InvariantCulture),(int)local.Offset.TotalMinutes,zone.Id,++_sequence));
    }
    public HistoryBatch Snapshot(long epoch) => new(epoch,CollectionId,StartedUtc,_usage.Values.ToArray(),_events.ToArray());
    public void Acknowledge(HistoryBatch batch)
    {
        if(batch.CollectionId!=CollectionId) return;
        var ids=batch.Events.Select(e=>e.Id).ToHashSet(); _events.RemoveAll(e=>ids.Contains(e.Id));
    }
    public void ClearBoundary()
    {
        _lastObservationTimestamp=null;
        _usage.Clear(); _events.Clear(); _anchor=null; _zone=null; _mappingInvalid=false;
        CollectionId=Guid.NewGuid(); StartedUtc=_time.GetUtcNow(); _sequence=0; DroppedEvents=0;
        _boundaryTimestamp=_time.GetTimestamp(); _anchor=new(_boundaryTimestamp,StartedUtc,TimeSpan.Zero,[]); _zone=_zoneProvider();
    }
}
