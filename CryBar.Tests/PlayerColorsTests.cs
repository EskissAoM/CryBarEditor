using CryBar.Scenario;
using Xunit;

namespace CryBar.Tests;

public class PlayerColorsTests
{
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)]
    [InlineData(5)] [InlineData(6)] [InlineData(7)] [InlineData(8)] [InlineData(9)]
    [InlineData(10)] [InlineData(11)] [InlineData(12)]
    public void GetRgb_ValidId_ReturnsConfiguredColor(int id)
    {
        var rgb = PlayerColors.GetRgb((byte)id);
        Assert.True(rgb.R >= 0 && rgb.R <= 1);
        Assert.True(rgb.G >= 0 && rgb.G <= 1);
        Assert.True(rgb.B >= 0 && rgb.B <= 1);
    }

    [Fact]
    public void GetRgb_OutOfRange_ReturnsLastEntryAsFallback()
    {
        var fallback = PlayerColors.GetRgb(255);
        var twelve   = PlayerColors.GetRgb(12);
        Assert.Equal(twelve, fallback);
    }

    [Fact]
    public void Player0_IsGray()
    {
        var c = PlayerColors.GetRgb(0);
        Assert.True(System.Math.Abs(c.R - c.G) < 0.05f);
        Assert.True(System.Math.Abs(c.G - c.B) < 0.05f);
    }
}
