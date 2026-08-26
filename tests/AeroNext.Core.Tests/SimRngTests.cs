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

    [Fact]
    public void SameSeed_ProducesSameIntSequence()
    {
        var a = new SimRng(seed: 7);
        var b = new SimRng(seed: 7);

        for (var i = 0; i < 10; i++)
        {
            Assert.Equal(a.Next(0, 1000), b.Next(0, 1000));
        }
    }

    [Fact]
    public void Next_RespectsBounds()
    {
        var rng = new SimRng(seed: 1);

        for (var i = 0; i < 100; i++)
        {
            var value = rng.Next(5, 10);
            Assert.InRange(value, 5, 9); // maxExclusive
        }
    }

    [Fact]
    public void DifferentSeeds_CanProduceDifferentSequences()
    {
        var a = new SimRng(seed: 1);
        var b = new SimRng(seed: 2);

        var sequenceA = Enumerable.Range(0, 5).Select(_ => a.NextDouble()).ToList();
        var sequenceB = Enumerable.Range(0, 5).Select(_ => b.NextDouble()).ToList();

        Assert.NotEqual(sequenceA, sequenceB);
    }
}
