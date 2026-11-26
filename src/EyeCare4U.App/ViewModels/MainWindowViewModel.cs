using System.ComponentModel;
using System.Runtime.CompilerServices;
using EyeCare4U.Core;

namespace EyeCare4U.App.ViewModels;

public sealed class MainWindowViewModel : INotifyPropertyChanged
{
    private readonly ReminderTimer _timer;
    private string _selectedInterval = "20";
    private string _customMinutes = "20";
    private TimerState _lastState = TimerState.Ready;
    private string _lastRemaining = "";

    public MainWindowViewModel(ReminderTimer timer)
    {
        _timer = timer;
        StartCommand = new(() => { _timer.Start(); Refresh(); }, () => CanEditInterval && ValidationError.Length == 0);
        PauseCommand = new(() => { _timer.Pause(); Refresh(); }, () => State == TimerState.Running);
        ResumeCommand = new(() => { _timer.Resume(); Refresh(); }, () => State == TimerState.Paused);
        ResetCommand = new(() => { _timer.Reset(); ApplyInterval(); Refresh(); }, () => State != TimerState.Ready);
        Refresh();
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public RelayCommand StartCommand { get; }
    public RelayCommand PauseCommand { get; }
    public RelayCommand ResumeCommand { get; }
    public RelayCommand ResetCommand { get; }
    public IReadOnlyList<string> IntervalOptions { get; } = ["20", "30", "60", "Custom"];
    public TimerState State => _timer.State;
    public bool CanEditInterval => State == TimerState.Ready;
    public bool IsCustom => SelectedInterval == "Custom";
    public string Status => State switch
    {
        TimerState.Ready => "Ready · Timer not started",
        TimerState.Running => "Running",
        TimerState.Paused => "Paused",
        TimerState.Due => "Time for an eye break",
        _ => throw new InvalidOperationException()
    };
    public string StateName => State.ToString();
    public string RemainingText
    {
        get
        {
            if (CanEditInterval && ValidationError.Length > 0) return "--:--";
            // Ceiling avoids displaying 00:00 before the actual deadline.
            var seconds = (int)Math.Ceiling(_timer.Remaining.TotalSeconds);
            return $"{seconds / 60:00}:{seconds % 60:00}";
        }
    }
    public string ValidationError => !IntervalInput.TryParse(IsCustom ? CustomMinutes : SelectedInterval, out _)
        ? IntervalInput.ErrorMessage : "";
    public string SelectedInterval
    {
        get => _selectedInterval;
        set
        {
            if (!CanEditInterval || !IntervalOptions.Contains(value) || value == _selectedInterval) return;
            _selectedInterval = value;
            Changed(); Changed(nameof(IsCustom));
            ApplyInterval(); Refresh();
        }
    }
    public string CustomMinutes
    {
        get => _customMinutes;
        set
        {
            if (!CanEditInterval || value == _customMinutes) return;
            _customMinutes = value;
            Changed(); ApplyInterval(); Refresh();
        }
    }

    private void ApplyInterval()
    {
        if (CanEditInterval && IntervalInput.TryParse(IsCustom ? CustomMinutes : SelectedInterval, out var minutes))
            _timer.SetInterval(minutes);
        Changed(nameof(ValidationError));
    }

    public void Refresh()
    {
        _timer.Refresh();
        if (_lastState != State)
        {
            _lastState = State;
            Changed(nameof(State)); Changed(nameof(StateName)); Changed(nameof(Status)); Changed(nameof(CanEditInterval));
        }
        var remaining = RemainingText;
        if (_lastRemaining != remaining)
        {
            _lastRemaining = remaining;
            Changed(nameof(RemainingText));
        }
        StartCommand.NotifyCanExecuteChanged(); PauseCommand.NotifyCanExecuteChanged();
        ResumeCommand.NotifyCanExecuteChanged(); ResetCommand.NotifyCanExecuteChanged();
    }

    private void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
}
