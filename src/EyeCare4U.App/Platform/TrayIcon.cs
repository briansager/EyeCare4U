using System.Windows.Input;
using EyeCare4U.App.ViewModels;
using EyeCare4U.Core;
using Forms = System.Windows.Forms;

namespace EyeCare4U.App.Platform;

/// <summary>Windows-only adapter. All callbacks run on the WPF dispatcher thread.</summary>
public sealed class TrayIcon : IDisposable
{
    private readonly MainWindowViewModel _viewModel;
    private readonly Forms.NotifyIcon _icon;
    private readonly System.Drawing.Icon _image;
    private readonly Forms.ContextMenuStrip _menu = new();
    private readonly Forms.ToolStripMenuItem _status;
    private readonly Forms.ToolStripMenuItem _start;
    private readonly Forms.ToolStripMenuItem _pause;
    private readonly Forms.ToolStripMenuItem _reset;
    private bool _disposed;

    public TrayIcon(MainWindowViewModel viewModel, Action open, Action exit)
    {
        _viewModel = viewModel;
        using var stream = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Assets/EyeCare4U.ico")).Stream;
        _image = new System.Drawing.Icon(stream);
        _status = new Forms.ToolStripMenuItem { Enabled = false };
        _menu.Items.Add(_status);
        _menu.Items.Add(new Forms.ToolStripSeparator());
        _menu.Items.Add("&Open EyeCare4U", null, (_, _) => open());
        _start = new Forms.ToolStripMenuItem("&Start", null, (_, _) => Execute(
            _viewModel.State == TimerState.Paused ? _viewModel.ResumeCommand : _viewModel.StartCommand));
        _pause = new Forms.ToolStripMenuItem("&Pause", null, (_, _) => Execute(_viewModel.PauseCommand));
        _reset = new Forms.ToolStripMenuItem("&Reset", null, (_, _) => Execute(_viewModel.ResetCommand));
        _menu.Items.AddRange([_start, _pause, _reset, new Forms.ToolStripSeparator()]);
        _menu.Items.Add("E&xit", null, (_, _) => exit());
        _menu.Opening += OnOpening;
        _icon = new Forms.NotifyIcon { Icon = _image, ContextMenuStrip = _menu, Visible = true };
        _icon.DoubleClick += (_, _) => open();
        Refresh();
    }

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
        _icon.Text = $"EyeCare4U · {_viewModel.Status} · {_viewModel.RemainingText}";
        _start.Text = _viewModel.State == TimerState.Paused ? "&Resume" : "&Start";
        _start.Enabled = (_viewModel.State == TimerState.Paused ? _viewModel.ResumeCommand : _viewModel.StartCommand).CanExecute(null);
        _pause.Enabled = _viewModel.PauseCommand.CanExecute(null);
        _reset.Enabled = _viewModel.ResetCommand.CanExecute(null);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _icon.Visible = false;
        _icon.Dispose();
        _menu.Opening -= OnOpening;
        _menu.Dispose();
        _image.Dispose();
    }
}
