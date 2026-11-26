using System.Runtime.InteropServices;
using System.Windows.Interop;
using EyeCare4U.Core;

namespace EyeCare4U.App.Platform;

/// <summary>
/// Current-session input timing only. HWND notifications arrive on the owning UI dispatcher.
/// Session state is also queried at every sample, including startup/recovery.
/// </summary>
public sealed class WindowsActivitySource : IActivitySource, IDisposable
{
    private readonly HwndSource _window;
    private readonly InputTickTracker _ticks = new();
    private bool _sessionRegistered;
    private nint _powerRegistration;
    private bool _disposed;
    private readonly PowerTickTracker _powerTicks = new();
    private int? _lastSessionEvent;

    public WindowsActivitySource(nint windowHandle)
    {
        _window = HwndSource.FromHwnd(windowHandle) ?? throw new ArgumentException("Window handle is required.");
        _window.AddHook(WindowProc);
        RegisterNotifications();
    }

    public event Action<SessionSignal>? Signal;

    private void RegisterNotifications()
    {
        if (!_sessionRegistered) _sessionRegistered = WTSRegisterSessionNotification(_window.Handle, 0);
        if (_powerRegistration == 0) _powerRegistration = RegisterSuspendResumeNotification(_window.Handle, 0);
    }

    public ActivityReading Read()
    {
        if (_disposed) return ActivityReading.Unavailable;
        // Retry startup/service registration failures at the same low-frequency cadence as sampling.
        RegisterNotifications();
        var input = new LastInputInfo { Size = (uint)Marshal.SizeOf<LastInputInfo>() };
        if (!_sessionRegistered || _powerRegistration == 0 || !GetLastInputInfo(ref input)
            || !TryReadLock(out var locked) || !QueryUnbiasedInterruptTime(out var awake)) return Failed();
        var uptime = GetTickCount64();
        if (!_powerTicks.TryRead(uptime, awake)) return Failed();
        return _ticks.TryRead(uptime, input.Tick, out var reading, locked) ? reading : ActivityReading.Unavailable;
    }

    private ActivityReading Failed()
    {
        _ticks.Reset();
        _powerTicks.Reset();
        return ActivityReading.Unavailable;
    }

    private static bool TryReadLock(out bool locked)
    {
        locked = false;
        nint buffer = 0;
        try
        {
            // WTSSessionInfoEx = 25; WTS_CURRENT_SESSION = -1; local server only.
            if (!WTSQuerySessionInformationW(0, -1, 25, out buffer, out var bytes) || bytes < 20) return false;
            // Native WTSINFOEX Data union has 8-byte alignment on supported Windows ABIs.
            // Read only Level, connection state and flags; never marshal names or other session data.
            if (Marshal.ReadInt32(buffer, 0) != 1 || Marshal.ReadInt32(buffer, 12) != 0) return false; // WTSActive
            var flags = Marshal.ReadInt32(buffer, 16);
            if (flags is not (0 or 1)) return false;
            locked = flags == 0; // Modern Windows; Windows 7's reversed flags are out of target support.
            return true;
        }
        finally { if (buffer != 0) WTSFreeMemory(buffer); }
    }

    private nint WindowProc(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (_disposed) return 0;
        SessionSignal? signal = null;
        if (message == 0x02B1) // WM_WTSSESSION_CHANGE; registered for this session only.
        {
            var reason = (int)wParam;
            if (_lastSessionEvent != reason)
            {
                _lastSessionEvent = reason;
                signal = SessionSignal.SessionChanged;
            }
        }
        if (message == 0x0218) // WM_POWERBROADCAST
        {
            if ((int)wParam == 4) signal = SessionSignal.Suspending;
            if ((int)wParam == 18) signal = SessionSignal.Resumed;
            // Ignore PBT_APMRESUMESUSPEND (7): it follows automatic resume, not a second wake.
        }
        if (signal is { } value)
        {
            _ticks.Reset();
            _powerTicks.Reset();
            Signal?.Invoke(value);
        }
        return 0;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_sessionRegistered) WTSUnRegisterSessionNotification(_window.Handle);
        if (_powerRegistration != 0) UnregisterSuspendResumeNotification(_powerRegistration);
        _window.RemoveHook(WindowProc);
        Signal = null;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LastInputInfo { public uint Size; public uint Tick; }
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetLastInputInfo(ref LastInputInfo info);
    [DllImport("kernel32.dll")] private static extern ulong GetTickCount64();
    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool QueryUnbiasedInterruptTime(out ulong time);
    [DllImport("wtsapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool WTSRegisterSessionNotification(nint hwnd, uint flags);
    [DllImport("wtsapi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool WTSUnRegisterSessionNotification(nint hwnd);
    [DllImport("wtsapi32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool WTSQuerySessionInformationW(nint server, int session, int infoClass, out nint buffer, out uint bytes);
    [DllImport("wtsapi32.dll")] private static extern void WTSFreeMemory(nint buffer);
    [DllImport("user32.dll", SetLastError = true)] private static extern nint RegisterSuspendResumeNotification(nint hwnd, uint flags);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool UnregisterSuspendResumeNotification(nint registration);
}
