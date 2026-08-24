using AeroNext.Core;
using Xunit;

namespace AeroNext.Core.Tests;

public class SimRngTests
{
    [Fact]
    public void SameSeed_ProducesSameSequence()
    {
        var a = new SimRng(seed: 42);
        var b = new SimRng(seed: 42);

        for (var i = 0; i < 10; i++)
        {
            Assert.Equal(a.NextDouble(), b.NextDouble());
        }
    }
}
