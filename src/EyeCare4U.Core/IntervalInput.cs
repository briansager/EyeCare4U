using System.Globalization;

namespace EyeCare4U.Core;

public static class IntervalInput
{
    public const string ErrorMessage = "Enter a whole number from 1 through 240 minutes.";

    public static bool TryParse(string? text, out int minutes) =>
        int.TryParse(text?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out minutes)
        && minutes is >= 1 and <= 240;
}
