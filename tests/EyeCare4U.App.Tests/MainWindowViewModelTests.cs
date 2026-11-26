using EyeCare4U.App.ViewModels;
using EyeCare4U.Core;

namespace EyeCare4U.App.Tests;

public sealed class MainWindowViewModelTests
{
    private sealed class ManualTime : TimeProvider
    {
        private long _now;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => _now;
        public void Advance(double seconds) => _now += TimeSpan.FromSeconds(seconds).Ticks;
    }

    [Theory]
    [InlineData("")] [InlineData("0")] [InlineData("241")] [InlineData("abc")]
    [InlineData("1.5")] [InlineData("999999999999999999999999")]
    public void InvalidCustomTextRemainsVisibleAndCannotStartAnOldInterval(string input)
    {
        var timer = new ReminderTimer(new ManualTime()); var vm = new MainWindowViewModel(timer);
        vm.SelectedInterval = "Custom"; vm.CustomMinutes = input;
        Assert.Equal(input, vm.CustomMinutes);
        Assert.Equal(IntervalInput.ErrorMessage, vm.ValidationError);
        Assert.Equal("--:--", vm.RemainingText);
        Assert.False(vm.StartCommand.CanExecute(null));
        vm.StartCommand.Execute(null);
        Assert.Equal(TimerState.Ready, timer.State);
    }

    [Theory]
    [InlineData("1", "01:00")] [InlineData("240", "240:00")]
    public void CorrectedCustomInputStartsTheEnteredInterval(string input, string expected)
    {
        var timer = new ReminderTimer(new ManualTime()); var vm = new MainWindowViewModel(timer);
        vm.SelectedInterval = "Custom"; vm.CustomMinutes = "bad"; vm.CustomMinutes = input;
        Assert.Equal("", vm.ValidationError);
        Assert.True(vm.StartCommand.CanExecute(null));
        Assert.Equal(expected, vm.RemainingText);
        vm.StartCommand.Execute(null);
        Assert.Equal(TimerState.Running, timer.State);
        Assert.Equal(TimeSpan.FromMinutes(int.Parse(input)), timer.Remaining);
    }

    [Theory]
    [InlineData("20")] [InlineData("30")] [InlineData("60")]
    public void PresetsUpdateTheTimerAndIgnoreChangesWhileActive(string preset)
    {
        var vm = new MainWindowViewModel(new ReminderTimer());
        vm.SelectedInterval = preset;
        Assert.Equal($"{preset}:00", vm.RemainingText);
        vm.StartCommand.Execute(null);
        vm.SelectedInterval = "Custom"; vm.CustomMinutes = "1";
        Assert.Equal(preset, vm.SelectedInterval);
        Assert.Equal("20", vm.CustomMinutes);
        Assert.False(vm.CanEditInterval);
        vm.PauseCommand.Execute(null); vm.SelectedInterval = "Custom";
        Assert.Equal(preset, vm.SelectedInterval);
        vm.ResetCommand.Execute(null);
        Assert.True(vm.CanEditInterval);
        Assert.Equal($"{preset}:00", vm.RemainingText);
    }

    [Fact]
    public void CommandsFollowStateAndPausedTimeIsNotConsumed()
    {
        var clock = new ManualTime(); var vm = new MainWindowViewModel(new ReminderTimer(clock));
        Assert.True(vm.StartCommand.CanExecute(null)); Assert.False(vm.ResumeCommand.CanExecute(null));
        vm.StartCommand.Execute(null); clock.Advance(10); vm.Refresh();
        Assert.False(vm.StartCommand.CanExecute(null)); Assert.True(vm.PauseCommand.CanExecute(null));
        vm.PauseCommand.Execute(null); clock.Advance(1000); vm.Refresh();
        Assert.Equal("19:50", vm.RemainingText);
        Assert.False(vm.PauseCommand.CanExecute(null)); Assert.True(vm.ResumeCommand.CanExecute(null));
        vm.ResumeCommand.Execute(null); clock.Advance(10); vm.Refresh();
        Assert.Equal("19:40", vm.RemainingText);
        vm.ResetCommand.Execute(null);
        Assert.Equal(TimerState.Ready, vm.State); Assert.Equal("20:00", vm.RemainingText);
    }

    [Fact]
    public void DueRequiresResetThenStartAndStateAnnouncementsDoNotRepeatOnDisplayTicks()
    {
        var clock = new ManualTime(); var vm = new MainWindowViewModel(new ReminderTimer(clock));
        vm.SelectedInterval = "Custom"; vm.CustomMinutes = "1";
        var stateNotifications = 0;
        vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(vm.Status)) stateNotifications++; };
        vm.StartCommand.Execute(null);
        clock.Advance(59.75); vm.Refresh();
        Assert.Equal("00:01", vm.RemainingText);
        Assert.Equal(1, stateNotifications);
        clock.Advance(.25); vm.Refresh();
        Assert.Equal("Time for an eye break", vm.Status); Assert.Equal("00:00", vm.RemainingText);
        Assert.False(vm.StartCommand.CanExecute(null)); Assert.False(vm.ResumeCommand.CanExecute(null));
        Assert.False(vm.PauseCommand.CanExecute(null)); Assert.True(vm.ResetCommand.CanExecute(null));
        Assert.False(vm.CanEditInterval);
        vm.StartCommand.Execute(null); vm.ResumeCommand.Execute(null);
        for (var i=0; i<20; i++) { clock.Advance(1); vm.Refresh(); }
        Assert.Equal(TimerState.Due, vm.State); Assert.Equal(2, stateNotifications);
        vm.ResetCommand.Execute(null);
        Assert.Equal(TimerState.Ready, vm.State); Assert.Equal("01:00", vm.RemainingText);
        Assert.True(vm.StartCommand.CanExecute(null));
        vm.StartCommand.Execute(null); Assert.Equal(TimerState.Running, vm.State);
    }
}
