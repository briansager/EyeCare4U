using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using EyeCare4U.Core;
using Forms = System.Windows.Forms;

namespace EyeCare4U.App.Platform;

/// <summary>Uses the existing tray menu/owned icon, with a silent, quiet-time-respecting shell balloon.</summary>
public sealed class ShellTrayIcon : IReminderNotifications
{
    private const int Callback = 0x8000 + 31;
    private readonly HwndSource _window;
    private readonly System.Drawing.Icon _image;
    private readonly Forms.ContextMenuStrip _menu;
    private readonly Action _open;
    private readonly uint _taskbarCreated;
    private Forms.NotifyIcon? _fallback;
    private string _text = "EyeCare4U";
    private bool _added;
    private bool _disposed;

    public ShellTrayIcon(nint owner, System.Drawing.Icon image, Forms.ContextMenuStrip menu, Action open)
    {
        _window = HwndSource.FromHwnd(owner) ?? throw new ArgumentException("An owner window is required.");
        _image = image; _menu = menu; _open = open;
        _window.AddHook(WindowProc);
        _taskbarCreated = RegisterWindowMessageW("TaskbarCreated");
        Add();
    }
    private Data Create(uint flags) => new()
    {
        Size = (uint)Marshal.SizeOf<Data>(), Window = _window.Handle, Id = 1, Flags = flags,
        CallbackMessage = Callback, Icon = _image.Handle, Tip = _text, Info = "", Title = ""
    };
    private void Add()
    {
        var data = Create(1 | 2 | 4);
        // A duplicate taskbar-created message must not create a fallback second icon.
        if (_added && Shell_NotifyIconW(1, ref data)) return;
        _added = Shell_NotifyIconW(0, ref data);
        if (!_added && _fallback is null)
        {
            // Preserve basic tray access even if native silent balloons cannot be initialized.
            _fallback = new Forms.NotifyIcon { Icon = _image, ContextMenuStrip = _menu, Text = _text, Visible = true };
            _fallback.DoubleClick += OnFallbackOpen;
        }
        if (_added && _fallback is not null) { _fallback.DoubleClick -= OnFallbackOpen; _fallback.Dispose(); _fallback = null; }
    }
    private void OnFallbackOpen(object? sender, EventArgs e) => _open();
    public string Text
    {
        set
        {
            if (_disposed || value == _text) return;
            _text = value;
            if (_fallback is not null) _fallback.Text = value;
            if (_added) { var data = Create(4); Shell_NotifyIconW(1, ref data); }
        }
    }
    public void ShowDue()
    {
        if (_disposed || !_added) throw new InvalidOperationException("Silent system notifications are unavailable.");
        var data = Create(0x10 | 0x40); // NIF_REALTIME: discard instead of queueing a stale reminder.
        data.Title = "EyeCare4U — time for an eye break";
        data.Info = "Start a 20-second eye break or snooze from EyeCare4U. Click to open the existing window.";
        data.InfoFlags = 0x1 | 0x10 | 0x80; // NIIF_INFO | NIIF_NOSOUND | NIIF_RESPECT_QUIET_TIME
        if (!Shell_NotifyIconW(1, ref data)) throw new Win32Exception();
    }
    public void Clear()
    {
        if (_disposed || !_added) return;
        var data = Create(0x10); // Empty info removes an outstanding balloon request.
        if (!Shell_NotifyIconW(1, ref data)) throw new Win32Exception();
    }
    private nint WindowProc(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (_disposed) return 0;
        if (_taskbarCreated != 0 && (uint)message == _taskbarCreated) Add();
        if (message == Callback)
        {
            var code = (int)lParam;
            if (code is 0x203 or 0x405) _open(); // Double-click or explicit balloon click only.
            if (code is 0x205 or 0x7B) _menu.Show(Forms.Cursor.Position);
        }
        return 0;
    }
    public void Dispose()
    {
        if (_disposed) return;
        if (_added) { var data = Create(0); Shell_NotifyIconW(2, ref data); }
        _disposed = true;
        if (_fallback is not null) { _fallback.DoubleClick -= OnFallbackOpen; _fallback.Dispose(); }
        _window.RemoveHook(WindowProc);
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Data
    {
        public uint Size; public nint Window; public uint Id, Flags, CallbackMessage; public nint Icon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst=128)] public string Tip;
        public uint State, StateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst=256)] public string Info;
        public uint TimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst=64)] public string Title;
        public uint InfoFlags; public Guid Guid; public nint BalloonIcon;
    }
    [DllImport("shell32.dll", CharSet=CharSet.Unicode, ExactSpelling=true, SetLastError=true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool Shell_NotifyIconW(uint message, ref Data data);
    [DllImport("user32.dll", CharSet=CharSet.Unicode, ExactSpelling=true)] private static extern uint RegisterWindowMessageW(string message);
}
