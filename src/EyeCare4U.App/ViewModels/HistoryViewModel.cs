using System.Collections.ObjectModel;
using System.ComponentModel;
using EyeCare4U.Core;
using EyeCare4U.Storage;

namespace EyeCare4U.App.ViewModels;

public sealed class DailyHistoryRow : INotifyPropertyChanged
{
    public DailyHistoryRow(DateOnly date) { Date=date; }
    public DateOnly Date { get; }
    public string DateText=>Date.ToString("yyyy-MM-dd",System.Globalization.CultureInfo.InvariantCulture);
    public string ActiveTime { get; private set; }="No recorded data";
    public string Finished { get; private set; }="No recorded data";
    public event PropertyChangedEventHandler? PropertyChanged;
    public void Update(DailyHistory value)
    {
        var active=HistoryViewModel.Duration(value.ActiveTicks); var finished=value.BreakTimersFinished?.ToString()??"No recorded data";
        if(active!=ActiveTime) { ActiveTime=active; PropertyChanged?.Invoke(this,new(nameof(ActiveTime))); }
        if(finished!=Finished) { Finished=finished; PropertyChanged?.Invoke(this,new(nameof(Finished))); }
    }
}
public sealed record RecentHistoryRow(Guid Id,string When,string Event,string Reason);

public sealed class HistoryViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly HistoryService _service;
    private readonly Func<UserPreferences> _preferences;
    private Task _operation=Task.CompletedTask;
    private bool _disposed;
    public HistoryViewModel(HistoryService service,Func<UserPreferences> preferences,string databasePath)
    {
        _service=service; _preferences=preferences; DatabaseLocation=databasePath;
        RefreshCommand=new(()=>_ = CheckpointAsync(),()=>!IsBusy);
        ClearHistoryCommand=new(()=>{ClearConfirmationVisible=true; Notify();},()=>!IsBusy);
        CancelClearCommand=new(()=>{ClearConfirmationVisible=false; Notify();},()=>!IsBusy);
        ConfirmClearCommand=new(()=>{ClearConfirmationVisible=false; _operation=RunAsync(true);},()=>!IsBusy);
        _service.Changed+=OnServiceChanged; Update();
    }
    public event PropertyChangedEventHandler? PropertyChanged;
    public ObservableCollection<DailyHistoryRow> Days { get; }=[];
    public ObservableCollection<RecentHistoryRow> Events { get; }=[];
    public string DatabaseLocation { get; }
    public string Status=>_service.Status;
    public bool IsBusy { get; private set; }
    public bool ClearConfirmationVisible { get; private set; }
    public string LoadingText=>IsBusy?"Loading or saving local history…":"";
    public string EmptyText=>_service.Snapshot is null?"Saved history is unavailable. Timing and reminders continue in memory.":
        Days.All(d=>d.ActiveTime=="No recorded data")&&Events.Count==0?"No recorded history yet. Data appears after an accepted observation is checkpointed.":"";
    public string TodayDate=>_service.Snapshot?.Today.ToString("yyyy-MM-dd")??"Not available";
    public string TodayActiveText=>_service.Snapshot is { } s?Duration(s.Days.First(d=>d.Date==s.Today).ActiveTicks):"Not available";
    public string TodayFinishedText=>_service.Snapshot is { } s?s.Days.First(d=>d.Date==s.Today).BreakTimersFinished?.ToString()??"No recorded data":"Not available";
    public string AttributionNote=>_service.Snapshot?.UnattributedTicks is >0?$"Saved accepted time without reliable daily attribution: {Duration(_service.Snapshot.UnattributedTicks)}. Excluded from daily totals.":"";
    public RelayCommand RefreshCommand { get; }
    public RelayCommand ClearHistoryCommand { get; }
    public RelayCommand ConfirmClearCommand { get; }
    public RelayCommand CancelClearCommand { get; }
    public Task WaitForPendingAsync()=>_operation;
    public Task CheckpointAsync()
    {
        if(_disposed) return Task.CompletedTask;
        if(IsBusy) return _operation;
        _operation=RunAsync(false); return _operation;
    }
    private async Task RunAsync(bool clear)
    {
        IsBusy=true; Notify();
        try { if(clear) await _service.ClearAsync(); else await _service.CheckpointAsync(_preferences()); }
        finally { IsBusy=false; Update(); }
    }
    private void OnServiceChanged()=>Update();
    private void Update()
    {
        if(_disposed) return;
        if(_service.Snapshot is { } snapshot)
        {
            var dates=snapshot.Days.Select(d=>d.Date).ToHashSet();
            foreach(var old in Days.Where(d=>!dates.Contains(d.Date)).ToArray()) Days.Remove(old);
            for(var i=0;i<snapshot.Days.Count;i++)
            {
                var value=snapshot.Days[i]; var row=Days.FirstOrDefault(d=>d.Date==value.Date);
                if(row is null) { row=new(value.Date); Days.Insert(i,row); }
                else if(Days.IndexOf(row)!=i) Days.Move(Days.IndexOf(row),i);
                row.Update(value);
            }
            var ids=snapshot.Events.Select(e=>e.Id).ToHashSet();
            foreach(var old in Events.Where(e=>!ids.Contains(e.Id)).ToArray()) Events.Remove(old);
            for(var i=0;i<snapshot.Events.Count;i++)
            {
                var value=snapshot.Events[i]; var existing=Events.FirstOrDefault(e=>e.Id==value.Id);
                if(existing is null) Events.Insert(i,new(value.Id,value.Utc.ToOffset(TimeSpan.FromMinutes(value.OffsetMinutes)).ToString("yyyy-MM-dd HH:mm:ss zzz"),Label(value.Kind),value.Reason));
                else if(Events.IndexOf(existing)!=i) Events.Move(Events.IndexOf(existing),i);
            }
        }
        else { Days.Clear(); Events.Clear(); }
        Notify();
    }
    public static string Duration(long? ticks)
    {
        if(ticks is null) return "No recorded data";
        var span=TimeSpan.FromTicks(ticks.Value); return $"{(long)span.TotalHours:00}:{span.Minutes:00}:{span.Seconds:00}";
    }
    private static string Label(HistoryKind kind)=>kind switch
    {
        HistoryKind.ReminderDue=>"Reminder became due", HistoryKind.BreakStarted=>"Break started",
        HistoryKind.BreakTimerFinished=>"Break timer finished", HistoryKind.BreakCancelled=>"Break cancelled",
        HistoryKind.BreakInterrupted=>"Break interrupted", HistoryKind.SnoozeStarted=>"Snooze started", _=>"Cycle reset"
    };
    private void Notify()
    {
        PropertyChanged?.Invoke(this,new(null));
        RefreshCommand.NotifyCanExecuteChanged(); ClearHistoryCommand.NotifyCanExecuteChanged();
        ConfirmClearCommand.NotifyCanExecuteChanged(); CancelClearCommand.NotifyCanExecuteChanged();
    }
    public void Dispose() { _disposed=true; _service.Changed-=OnServiceChanged; }
}
