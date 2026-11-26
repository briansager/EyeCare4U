namespace EyeCare4U.Core;

public sealed record UserPreferences
{
    public string SelectedInterval { get; init; } = "20";
    public int CustomMinutes { get; init; } = 20;
    public int IdleThresholdSeconds { get; init; } = 120;
    public bool NotificationsEnabled { get; init; } = true;
    public bool ReminderSoundEnabled { get; init; }
    public bool BreakSoundsEnabled { get; init; }
    public int SnoozeMinutes { get; init; } = 5;
    public UserPreferences Validated() => this with
    {
        SelectedInterval = SelectedInterval is "20" or "30" or "60" or "Custom" ? SelectedInterval : "20",
        CustomMinutes = CustomMinutes is >= 1 and <= 240 ? CustomMinutes : 20,
        IdleThresholdSeconds = IdleThresholdSeconds is >= 30 and <= 900 ? IdleThresholdSeconds : 120,
        SnoozeMinutes = SnoozeMinutes is 1 or 5 or 10 ? SnoozeMinutes : 5
    };
}
public readonly record struct ActivityWindow(TimeSpan Start, TimeSpan Duration);
public sealed record ActivityObservation(long Timestamp, DateTimeOffset Utc, TimeSpan Span, IReadOnlyList<ActivityWindow> Windows);
public enum HistoryKind { ReminderDue, BreakStarted, BreakTimerFinished, BreakCancelled, BreakInterrupted, SnoozeStarted, CycleReset }
public sealed record ReminderLifecycle(Guid Id, Guid OccurrenceId, Guid? BreakId, HistoryKind Kind, string Reason);
public sealed record UsageTotal(Guid Id, string? LocalDate, int? OffsetMinutes, string ZoneId, long ActiveTicks, DateTimeOffset FirstUtc, DateTimeOffset LastUtc);
public sealed record RecordedEvent(Guid Id, Guid OccurrenceId, Guid? BreakId, HistoryKind Kind, string Reason, DateTimeOffset Utc, string LocalDate, int OffsetMinutes, string ZoneId, long Sequence);
public sealed record HistoryBatch(long Epoch, Guid CollectionId, DateTimeOffset StartedUtc, IReadOnlyList<UsageTotal> Usage, IReadOnlyList<RecordedEvent> Events);
public sealed record DailyHistory(DateOnly Date, long? ActiveTicks, int? BreakTimersFinished);
public sealed record HistorySnapshot(DateOnly Today, IReadOnlyList<DailyHistory> Days, IReadOnlyList<RecordedEvent> Events, long UnattributedTicks);
public sealed record StorageInitialization(UserPreferences Preferences, long Epoch);

public interface IHistoryStore
{
    Task<StorageInitialization> InitializeAsync();
    Task<bool> SaveAsync(HistoryBatch batch, UserPreferences preferences);
    Task<long> ClearHistoryAsync();
    Task<HistorySnapshot> QueryAsync(DateOnly today);
}
