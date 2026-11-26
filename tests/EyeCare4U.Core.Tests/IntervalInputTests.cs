using EyeCare4U.Core;

namespace EyeCare4U.Core.Tests;

public sealed class IntervalInputTests
{
    [Theory]
    [InlineData("1", 1)] [InlineData("240", 240)] [InlineData(" 30 ", 30)] [InlineData("060", 60)]
    public void AcceptsWholeMinutesWithinBounds(string text, int expected)
    {
        Assert.True(IntervalInput.TryParse(text, out var minutes));
        Assert.Equal(expected, minutes);
    }

    [Theory]
    [InlineData(null)] [InlineData("")] [InlineData(" ")] [InlineData("0")] [InlineData("241")]
    [InlineData("-1")] [InlineData("+20")] [InlineData("1.5")] [InlineData("1,5")]
    [InlineData("1e2")] [InlineData("one")] [InlineData("99999999999999999999")]
    public void RejectsInvalidInputWithoutSubstitution(string? text)
    {
        Assert.False(IntervalInput.TryParse(text, out _));
    }
}
