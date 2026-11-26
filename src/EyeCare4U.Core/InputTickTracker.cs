namespace EyeCare4U.Core;

/// <summary>Validates a 32-bit last-input tick against 64-bit system uptime.</summary>
public sealed class InputTickTracker
{
    private const ulong Rollover = 1UL << 32;
    private ulong? _previousNow;
    private uint _previousRaw;
    private ulong _previousInput;
    private bool _trusted;

    public void Reset() { _previousNow = null; _trusted = false; }

    public bool TryRead(ulong now, uint rawInput, out ActivityReading reading, bool locked = false)
    {
        reading = ActivityReading.Unavailable;
        var previousNow = _previousNow;
        var previousRaw = _previousRaw;
        var wasTrusted = _trusted;
        _previousNow = now;
        _previousRaw = rawInput;
        _trusted = false;

        var input = (now & ~(Rollover - 1)) | rawInput;
        if (input > now)
        {
            if (input < Rollover) return false; // Future input tick; do not wrap underflow.
            input -= Rollover;
        }
        if (previousNow is { } previous && (now < previous || now - previous > 5000)) return false;
        if (now - input > 86_400_000) return false; // Old/ambiguous input: require fresh input.
        if (wasTrusted && input < _previousInput) return false; // API timestamps can regress.

        // After 49.7 days, an unchanged 32-bit tick alone cannot prove which wrap it belongs to.
        // On startup/recovery require a changed tick located within the observed interval.
        if (!wasTrusted && now >= Rollover &&
            !(previousNow is { } observed && rawInput != previousRaw && input >= observed)) return false;
        if (previousNow is not null && !wasTrusted && rawInput == previousRaw) return false;

        _trusted = true;
        _previousInput = input;
        reading = new(true, TimeSpan.FromMilliseconds(now - input), input, locked);
        return true;
    }
}
