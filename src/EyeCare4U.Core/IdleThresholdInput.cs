using System.Globalization;

namespace EyeCare4U.Core;

public static class IdleThresholdInput
{
    public const string ErrorMessage = "Enter a whole number from 30 through 900 seconds.";
    public static bool TryParse(string? text, out int seconds) =>
        int.TryParse(text?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out seconds)
        && seconds is >= 30 and <= 900;
}
