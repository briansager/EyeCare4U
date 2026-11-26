using EyeCare4U.Core;

namespace EyeCare4U.Core.Tests;

public sealed class InputTickTrackerTests
{
    [Fact]
    public void OrdinaryIdleAndRolloverRemainContinuous()
    {
        var t=new InputTickTracker();
        Assert.True(t.TryRead(uint.MaxValue-500UL,uint.MaxValue-1000,out var a));
        Assert.Equal(TimeSpan.FromMilliseconds(500),a.IdleFor);
        Assert.True(t.TryRead((1UL<<32)+500,uint.MaxValue-1000,out var b));
        Assert.Equal(TimeSpan.FromMilliseconds(1501),b.IdleFor);
        Assert.True(t.TryRead((1UL<<32)+1000,900,out var c));
        Assert.Equal(TimeSpan.FromMilliseconds(100),c.IdleFor);
    }
    [Fact]
    public void HighUptimeStartupRequiresNewObservedInputToDisambiguateWrap()
    {
        var t=new InputTickTracker(); var wrap=1UL<<32;
        Assert.False(t.TryRead(wrap+1000,900,out _));
        Assert.False(t.TryRead(wrap+2000,900,out _));
        Assert.True(t.TryRead(wrap+3000,2900,out var reading));
        Assert.Equal(TimeSpan.FromMilliseconds(100),reading.IdleFor);
    }
    [Fact]
    public void FutureRegressedAndStaleSamplesAreNotActive()
    {
        var t=new InputTickTracker();
        Assert.False(t.TryRead(1000,1100,out _));
        Assert.True(t.TryRead(2000,1900,out _));
        Assert.False(t.TryRead(3000,1800,out _));
        Assert.False(t.TryRead(4000,1800,out _));
        Assert.True(t.TryRead(5000,4900,out _));
        Assert.False(t.TryRead(4000,3900,out _));
        t.Reset(); Assert.False(t.TryRead(100_000_000,1,out _));
    }
    [Fact]
    public void LongGapRequiresFreshInputAndNeverBackfills()
    {
        var t=new InputTickTracker(); Assert.True(t.TryRead(1000,900,out _));
        Assert.False(t.TryRead(10000,900,out _));
        Assert.False(t.TryRead(11000,900,out _));
        Assert.True(t.TryRead(12000,11900,out _));
    }
    [Theory]
    [InlineData(2000,20_000_000,true)] [InlineData(2000,10_000_000,false)]
    [InlineData(2000,100_000_000,false)]
    [InlineData(999,20_000_000,false)] [InlineData(2000,9_000_000,false)]
    public void AwakeCounterDetectsMissedSleepOrCounterRegressions(ulong uptime,ulong awake,bool expected)
    {
        var t=new PowerTickTracker(); Assert.True(t.TryRead(1000,10_000_000));
        Assert.Equal(expected,t.TryRead(uptime,awake));
    }
    [Theory]
    [InlineData("30",30)] [InlineData("900",900)] [InlineData(" 120 ",120)]
    public void ValidThresholdInput(string text,int expected)
    {
        Assert.True(IdleThresholdInput.TryParse(text,out var seconds)); Assert.Equal(expected,seconds);
    }
    [Theory]
    [InlineData(null)] [InlineData("")] [InlineData("29")] [InlineData("901")] [InlineData("1.5")]
    [InlineData("abc")] [InlineData("-30")] [InlineData("99999999999999999999999")]
    public void InvalidThresholdInput(string? text) => Assert.False(IdleThresholdInput.TryParse(text,out _));
}
