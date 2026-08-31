using AeroNext.Core.Ai;
using Xunit;

namespace AeroNext.Core.Tests;

public class GameEraTests
{
    [Theory]
    [InlineData(1, GameEra.Starting)]
    [InlineData(40, GameEra.Starting)]
    [InlineData(41, GameEra.MidGame)]
    [InlineData(160, GameEra.MidGame)]
    [InlineData(161, GameEra.LateGame)]
    [InlineData(244, GameEra.LateGame)]
    public void ForQuarter_ReturnsExpectedEra(int quarter, GameEra expected)
    {
        Assert.Equal(expected, GameEras.ForQuarter(quarter));
    }
}
