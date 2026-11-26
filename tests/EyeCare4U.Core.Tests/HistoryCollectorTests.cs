using System.Globalization;
using EyeCare4U.Core;

namespace EyeCare4U.Core.Tests;

public sealed class HistoryCollectorTests
{
    private sealed class Clock : TimeProvider
    {
        public long Ticks;
        public DateTimeOffset Utc=new(2026,11,25,15,59,58,TimeSpan.Zero);
        public override long TimestampFrequency=>TimeSpan.TicksPerSecond;
        public override long GetTimestamp()=>Ticks;
        public override DateTimeOffset GetUtcNow()=>Utc;
        public override TimeZoneInfo LocalTimeZone=>TimeZoneInfo.Utc;
        public void Advance(double seconds) { Ticks+=TimeSpan.FromSeconds(seconds).Ticks; Utc+=TimeSpan.FromSeconds(seconds); }
    }
    private static void Sample(HistoryCollector collector,Clock clock,double span,params ActivityWindow[] windows)=>
        collector.Observe(new(clock.Ticks,clock.Utc,TimeSpan.FromSeconds(span),windows));
    private static ActivityWindow Window(double start,double length)=>new(TimeSpan.FromSeconds(start),TimeSpan.FromSeconds(length));
    private static long Total(HistoryCollector collector)=>collector.Snapshot(0).Usage.Sum(u=>u.ActiveTicks);

    [Fact]
    public void AcceptedWindowsArePreservedWithoutEstimatingTheWholePollingInterval()
    {
        var c=new Clock(); var collector=new HistoryCollector(c,()=>TimeZoneInfo.Utc);
        Sample(collector,c,0); c.Advance(5); Sample(collector,c,5,Window(0,.5),Window(4,1));
        Assert.Equal(TimeSpan.FromSeconds(1.5).Ticks,Total(collector));
        var first=collector.Snapshot(0).Usage.Single().Id;
        c.Advance(1); Sample(collector,c,1,Window(0,1));
        Assert.Equal(first,collector.Snapshot(0).Usage.Single().Id);
        Assert.Equal(TimeSpan.FromSeconds(2.5).Ticks,Total(collector));
    }
    [Theory]
    [InlineData(2,2,0)] [InlineData(4,2,2)]
    public void MidnightSplitsMonotonicTicksAtTheRecordedLocalBoundary(int seconds,int before,int after)
    {
        var c=new Clock(); var zone=TimeZoneInfo.CreateCustomTimeZone("Test +08",TimeSpan.FromHours(8),"Test","Test");
        var collector=new HistoryCollector(c,()=>zone); Sample(collector,c,0);
        c.Advance(seconds); Sample(collector,c,seconds,Window(0,seconds));
        var rows=collector.Snapshot(0).Usage;
        Assert.Equal(TimeSpan.FromSeconds(before).Ticks,rows.Where(r=>r.LocalDate=="2026-11-25").Sum(r=>r.ActiveTicks));
        Assert.Equal(TimeSpan.FromSeconds(after).Ticks,rows.Where(r=>r.LocalDate=="2026-11-26").Sum(r=>r.ActiveTicks));
        Assert.All(rows,r=>Assert.Equal(480,r.OffsetMinutes));
    }
    [Fact]
    public void RepeatedAndOutOfOrderObservationsCannotCreditAnIntervalTwice()
    {
        var c=new Clock(); var collector=new HistoryCollector(c,()=>TimeZoneInfo.Utc);
        Sample(collector,c,0); c.Advance(1);
        var sample=new ActivityObservation(c.Ticks,c.Utc,TimeSpan.FromSeconds(1),[Window(0,1)]);
        collector.Observe(sample); collector.Observe(sample);
        collector.Observe(sample with { Timestamp=sample.Timestamp-1 });
        Assert.Equal(TimeSpan.FromSeconds(1).Ticks,Total(collector));
        c.Advance(1); Sample(collector,c,1,Window(0,1));
        Assert.Equal(TimeSpan.FromSeconds(2).Ticks,Total(collector));
    }
    private static TimeZoneInfo DstZone()
    {
        var start=TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1,1,1,2,0,0),3,2,DayOfWeek.Sunday);
        var end=TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1,1,1,2,0,0),11,1,DayOfWeek.Sunday);
        var rule=TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(new DateTime(2020,1,1),new DateTime(2030,12,31),TimeSpan.FromHours(1),start,end);
        return TimeZoneInfo.CreateCustomTimeZone("Test DST",TimeSpan.FromHours(-5),"Test","Standard","Daylight",[rule]);
    }
    [Theory]
    [InlineData(3,8,6)] [InlineData(11,1,5)]
    public void DstTransitionsKeepDurationsAndRecordBothOffsets(int month,int day,int hour)
    {
        var c=new Clock { Utc=new(2026,month,day,hour,59,58,TimeSpan.Zero) };
        var collector=new HistoryCollector(c,DstZone); Sample(collector,c,0);
        c.Advance(4); Sample(collector,c,4,Window(0,4));
        var credited=collector.Snapshot(0).Usage.Where(r=>r.ActiveTicks>0).ToArray();
        Assert.Equal(2,credited.Length); Assert.Equal(2,credited.Select(r=>r.OffsetMinutes).Distinct().Count());
        Assert.All(credited,r=>Assert.Equal(TimeSpan.FromSeconds(2).Ticks,r.ActiveTicks));
        Assert.Equal(TimeSpan.FromSeconds(4).Ticks,Total(collector));
    }
    [Fact]
    public void TimezoneChangePreservesOldDatesAndKeepsUncertainDurationSeparately()
    {
        var c=new Clock(); var zone=TimeZoneInfo.Utc; var collector=new HistoryCollector(c,()=>zone);
        Sample(collector,c,0); c.Advance(1); Sample(collector,c,1,Window(0,1));
        zone=TimeZoneInfo.CreateCustomTimeZone("New zone",TimeSpan.FromHours(10),"New","New");
        c.Advance(1); Sample(collector,c,1,Window(0,1));
        c.Advance(1); Sample(collector,c,1,Window(0,1));
        var rows=collector.Snapshot(0).Usage;
        Assert.Equal(TimeSpan.FromSeconds(1).Ticks,rows.Single(r=>r.LocalDate is null).ActiveTicks);
        Assert.Equal(TimeSpan.FromSeconds(1).Ticks,rows.Where(r=>r.ZoneId=="UTC"&&r.LocalDate is not null).Sum(r=>r.ActiveTicks));
        Assert.Equal(TimeSpan.FromSeconds(3).Ticks,Total(collector));
    }
    [Theory]
    [InlineData(3600)] [InlineData(-3600)]
    public void WallClockJumpRetainsAcceptedTimeWithoutGuessingItsDate(int jump)
    {
        var c=new Clock(); var collector=new HistoryCollector(c,()=>TimeZoneInfo.Utc); Sample(collector,c,0);
        c.Advance(1); c.Utc=c.Utc.AddSeconds(jump); Sample(collector,c,1,Window(0,1));
        c.Advance(1); Sample(collector,c,1,Window(0,1));
        Assert.Equal(TimeSpan.FromSeconds(2).Ticks,Total(collector));
        Assert.Equal(TimeSpan.FromSeconds(1).Ticks,collector.Snapshot(0).Usage.Single(r=>r.LocalDate is null).ActiveTicks);
    }
    [Fact]
    public void ClearBoundaryClipsTheFirstPartialIntervalAndNeverRewritesTheSessionEstimate()
    {
        var c=new Clock(); var collector=new HistoryCollector(c,()=>TimeZoneInfo.Utc); Sample(collector,c,0);
        c.Advance(1); Sample(collector,c,1,Window(0,1)); var oldId=collector.CollectionId;
        c.Advance(.5); collector.ClearBoundary(); c.Advance(.5);
        Sample(collector,c,1,Window(0,1));
        Assert.NotEqual(oldId,collector.CollectionId);
        Assert.Equal(TimeSpan.FromSeconds(.5).Ticks,Total(collector));
        Assert.DoesNotContain(collector.Snapshot(1).Usage,r=>r.LocalDate is null);
    }
    [Fact]
    public void MissingObservationGapDoesNotBecomeFilledActivity()
    {
        var c=new Clock(); var collector=new HistoryCollector(c,()=>TimeZoneInfo.Utc); Sample(collector,c,0);
        c.Advance(1); Sample(collector,c,1,Window(0,1));
        c.Advance(10_000); Sample(collector,c,0);
        c.Advance(1); Sample(collector,c,1,Window(0,1));
        Assert.Equal(TimeSpan.FromSeconds(2).Ticks,Total(collector));
    }
    [Fact]
    public void DateLabelsAreGregorianAndAcknowledgementDoesNotRemoveNewEvents()
    {
        var original=CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture=new CultureInfo("th-TH");
            var c=new Clock(); var collector=new HistoryCollector(c,()=>TimeZoneInfo.Utc); Sample(collector,c,0);
            var first=new ReminderLifecycle(Guid.NewGuid(),Guid.NewGuid(),null,HistoryKind.ReminderDue,"");
            collector.Record(first); var batch=collector.Snapshot(0);
            collector.Record(new(Guid.NewGuid(),Guid.NewGuid(),null,HistoryKind.BreakStarted,"")); collector.Acknowledge(batch);
            Assert.Equal("2026-11-25",batch.Usage.Single().LocalDate);
            Assert.Single(collector.Snapshot(0).Events); Assert.Equal(HistoryKind.BreakStarted,collector.Snapshot(0).Events[0].Kind);
        }
        finally { CultureInfo.CurrentCulture=original; }
    }
    [Fact]
    public void ExplicitMappingInvalidationDoesNotDiscardAcceptedTicks()
    {
        var c=new Clock(); var collector=new HistoryCollector(c,()=>TimeZoneInfo.Utc); Sample(collector,c,0);
        collector.InvalidateMapping(); c.Advance(1); Sample(collector,c,1,Window(0,1));
        Assert.Equal(TimeSpan.FromSeconds(1).Ticks,collector.Snapshot(0).Usage.Single(r=>r.LocalDate is null).ActiveTicks);
    }
    private sealed class Source(Clock clock) : IActivitySource
    {
        public long Input;
        public bool Available=true;
        public ActivityReading Read()=>Available?new(true,TimeSpan.FromTicks(clock.Ticks-Input),(ulong)(Input/10_000),false):ActivityReading.Unavailable;
    }
    [Theory]
    [InlineData(true,"Application closed")]
    [InlineData(false,"Cycle reset")]
    public void AbandoningARunningBreakRecordsOneInterruption(bool close,string reason)
    {
        var c=new Clock(); var collector=new HistoryCollector(c,()=>TimeZoneInfo.Utc); var source=new Source(c);
        var session=new ActivitySession(source,c); session.Timer.SetInterval(1);
        using var experience=new ReminderExperience(session,new Notifications(),new Sounds(),c);
        experience.Lifecycle+=collector.Record; experience.Start();
        for(var i=0;i<60;i++) { c.Advance(1); source.Input=c.Ticks; experience.Refresh(); }
        experience.StartBreak();
        if(close) { experience.Dispose(); experience.Dispose(); } else { experience.Reset(); experience.Reset(); }
        var events=collector.Snapshot(0).Events;
        Assert.Equal(reason,Assert.Single(events,e=>e.Kind==HistoryKind.BreakInterrupted).Reason);
        Assert.DoesNotContain(events,e=>e.Kind==HistoryKind.BreakTimerFinished);
    }
    private sealed class Notifications : IReminderNotifications { public void ShowDue(){} public void Clear(){} public void Dispose(){} }
    private sealed class Sounds : IReminderSounds { public void Play(SoundCue cue){} public void Stop(){} public void Dispose(){} }
    [Fact]
    public void RealEstimatorWindowsMatchTheSessionTotalAcrossIdleAndFailure()
    {
        var c=new Clock(); var collector=new HistoryCollector(c,()=>TimeZoneInfo.Utc); var source=new Source(c);
        var session=new ActivitySession(source,c,idleThresholdSeconds:30); session.Observed+=collector.Observe;
        collector.Observe(session.LastObservation!);
        for(var i=0;i<35;i++) { c.Advance(1); session.Poll(); }
        c.Advance(.5); source.Input=c.Ticks; c.Advance(.5); session.Poll();
        source.Available=false; c.Advance(1); session.Poll(); source.Available=true; c.Advance(1); session.Poll();
        c.Advance(1); session.Poll();
        Assert.Equal(session.EstimatedActiveTime.Ticks,Total(collector));
        Assert.Equal(TimeSpan.FromSeconds(31.5).Ticks,Total(collector));
    }
    [Fact]
    public void LifecycleEventsHaveStableOccurrencesAndDistinctBreakOutcomes()
    {
        var c=new Clock(); var collector=new HistoryCollector(c,()=>TimeZoneInfo.Utc); var source=new Source(c);
        var session=new ActivitySession(source,c); session.Timer.SetInterval(1);
        using var experience=new ReminderExperience(session,new Notifications(),new Sounds(),c);
        experience.Lifecycle+=collector.Record; experience.Start();
        for(var i=0;i<60;i++) { c.Advance(1); source.Input=c.Ticks; experience.Refresh(); }
        experience.StartBreak(); experience.StartBreak(); experience.CancelBreak(); experience.CancelBreak();
        experience.StartBreak(); for(var i=0;i<20;i++) { c.Advance(1); experience.Refresh(); }
        var events=collector.Snapshot(0).Events;
        Assert.Single(events,e=>e.Kind==HistoryKind.ReminderDue);
        Assert.Equal(2,events.Count(e=>e.Kind==HistoryKind.BreakStarted));
        Assert.Single(events,e=>e.Kind==HistoryKind.BreakCancelled); Assert.Single(events,e=>e.Kind==HistoryKind.BreakTimerFinished);
        Assert.Single(events.Select(e=>e.OccurrenceId).Distinct());
        Assert.Equal(2,events.Where(e=>e.BreakId is not null).Select(e=>e.BreakId).Distinct().Count());
        Assert.Equal(events.Count,events.Select(e=>e.Id).Distinct().Count());
    }
}
