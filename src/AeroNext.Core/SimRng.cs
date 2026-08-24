namespace AeroNext.Core;

/// <summary>
/// The one seeded RNG source for a simulation run. Pass it explicitly through
/// call chains — never reach for a static Random or ambient randomness. Same
/// seed + same orders must reproduce the same quarter, always.
/// </summary>
public sealed class SimRng
{
    private readonly Random _random;

    public SimRng(int seed)
    {
        _random = new Random(seed);
    }

    public int Next(int minInclusive, int maxExclusive) => _random.Next(minInclusive, maxExclusive);

    public double NextDouble() => _random.NextDouble();
}
