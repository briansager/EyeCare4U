using System.Windows.Input;
using EyeCare4U.App.ViewModels;
using EyeCare4U.Core;
using Forms = System.Windows.Forms;

namespace EyeCare4U.App.Platform;

/// <summary>Windows-only adapter. All callbacks run on the WPF dispatcher thread.</summary>
public sealed class TrayIcon : IDisposable
{
    private readonly MainWindowViewModel _viewModel;
    private readonly ShellTrayIcon _icon;
    private readonly System.Drawing.Icon _image;
    private readonly Forms.ContextMenuStrip _menu = new();
    private readonly Forms.ToolStripMenuItem _status;
    private readonly Forms.ToolStripMenuItem _monitoring;
    private readonly Forms.ToolStripMenuItem _usage;
    private readonly Forms.ToolStripMenuItem _start;
    private readonly Forms.ToolStripMenuItem _pause;
    private readonly Forms.ToolStripMenuItem _reset;
    private bool _disposed;
    private readonly Forms.ToolStripMenuItem _breakStart;
    private readonly Forms.ToolStripMenuItem _breakCancel;
    private readonly Forms.ToolStripMenuItem _snooze;

    public TrayIcon(MainWindowViewModel viewModel, Action open, Action exit)
    {
        _viewModel = viewModel;
        using var stream = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Assets/EyeCare4U.ico")).Stream;
        _image = new System.Drawing.Icon(stream);
        _status = new Forms.ToolStripMenuItem { Enabled = false };
        _menu.Items.Add(_status);
        _monitoring = new Forms.ToolStripMenuItem { Enabled = false };
        _usage = new Forms.ToolStripMenuItem { Enabled = false };
        _menu.Items.Add(_monitoring);
        _menu.Items.Add(_usage);
        _menu.Items.Add(new Forms.ToolStripSeparator());
        _menu.Items.Add("&Open EyeCare4U", null, (_, _) => open());
        _start = new Forms.ToolStripMenuItem("&Start", null, (_, _) => Execute(
            _viewModel.ResumeCommand.CanExecute(null) ? _viewModel.ResumeCommand : _viewModel.StartCommand));
        _pause = new Forms.ToolStripMenuItem("&Pause", null, (_, _) => Execute(_viewModel.PauseCommand));
        _reset = new Forms.ToolStripMenuItem("&Reset", null, (_, _) => Execute(_viewModel.ResetCommand));
        _breakStart = new Forms.ToolStripMenuItem("Start eye &break", null, (_, _) => Execute(_viewModel.StartBreakCommand));
        _breakCancel = new Forms.ToolStripMenuItem("&Cancel break", null, (_, _) => Execute(_viewModel.CancelBreakCommand));
        _snooze = new Forms.ToolStripMenuItem("S&nooze");
        foreach (var minutes in new[] { 1, 5, 10 })
            _snooze.DropDownItems.Add($"{minutes} minute{(minutes == 1 ? "" : "s")}", null, (_, _) =>
            { _viewModel.SelectedSnooze = minutes; Execute(_viewModel.SnoozeCommand); });
        _menu.Items.AddRange([_start, _pause, _breakStart, _breakCancel, _snooze, _reset, new Forms.ToolStripSeparator()]);
        _menu.Items.Add("E&xit", null, (_, _) => exit());
        _menu.Opening += OnOpening;
        _icon = new ShellTrayIcon(new System.Windows.Interop.WindowInteropHelper(System.Windows.Application.Current.MainWindow).Handle, _image, _menu, open);
        Refresh();
    }

    public IReminderNotifications Notifications => _icon;

    private void OnOpening(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _viewModel.Refresh();
        Refresh();
    }

    private void Execute(ICommand command)
    {
        if (command.CanExecute(null)) command.Execute(null);
        Refresh();
    }

    public void Refresh()
    {
        _status.Text = $"{_viewModel.Status} · {_viewModel.RemainingText}";
        // Under even the legacy Windows tooltip limit of 63 characters.
        var tooltip = $"EyeCare4U · {_viewModel.Status} · {_viewModel.RemainingText}";
        _icon.Text = tooltip.Length <= 63 ? tooltip : tooltip[..60] + "...";
        _monitoring.Text = _viewModel.ActivityStatusText;
        _usage.Text = $"Estimated active PC time: {_viewModel.EstimatedTimeText}";
        _start.Text = _viewModel.ResumeCommand.CanExecute(null) ? "&Resume" : "&Start";
        _start.Enabled = (_viewModel.ResumeCommand.CanExecute(null) ? _viewModel.ResumeCommand : _viewModel.StartCommand).CanExecute(null);
        _pause.Enabled = _viewModel.PauseCommand.CanExecute(null);
        _reset.Enabled = _viewModel.ResetCommand.CanExecute(null);
        _breakStart.Enabled = _viewModel.StartBreakCommand.CanExecute(null);
        _breakCancel.Enabled = _viewModel.CancelBreakCommand.CanExecute(null);
        _snooze.Enabled = _viewModel.SnoozeCommand.CanExecute(null);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _icon.Dispose();
        _menu.Opening -= OnOpening;
        _menu.Dispose();
        _image.Dispose();
    }
}
