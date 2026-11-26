using System.ComponentModel;
using System.Runtime.InteropServices;
using EyeCare4U.Core;

namespace EyeCare4U.App.Platform;

public sealed class WindowsReminderSounds : IReminderSounds
{
    private bool _disposed;
    public void Play(SoundCue cue)
    {
        if (_disposed) return;
        Stop(); // Only one app-owned asynchronous sound at a time; no queued playback.
        var alias = cue switch { SoundCue.Reminder => "SystemAsterisk", SoundCue.BreakStart => "SystemQuestion", _ => "SystemExclamation" };
        if (!PlaySoundW(alias, 0, 0x10000 | 0x1 | 0x2)) throw new Win32Exception(); // ALIAS | ASYNC | NODEFAULT
    }
    public void Stop() { if (!_disposed) PlaySoundW(null, 0, 0); }
    public void Dispose() { if (_disposed) return; Stop(); _disposed = true; }
    [DllImport("winmm.dll", CharSet=CharSet.Unicode, ExactSpelling=true, SetLastError=true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool PlaySoundW(string? sound, nint module, uint flags);
}
