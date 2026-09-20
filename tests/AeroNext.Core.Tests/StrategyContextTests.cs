using AeroNext.Core.Airlines;
using AeroNext.Core.Ai;
using AeroNext.Core.Fleet;
using AeroNext.Core.World;
using Xunit;

namespace AeroNext.Core.Tests;

/// <summary>
/// Geopolitical-aircraft-restrictions follow-up
/// (docs/GEOPOLITICAL_AIRCRAFT_RESTRICTIONS_ROADMAP.md): PurchasableTypes/
/// LeasableTypes and their own EraAvailableTypes-falling-back convenience
/// properties.
/// </summary>
public class StrategyContextTests
{
    private static City MakeCity(string id) => new(id, id.ToUpperInvariant(), Region.EastAsia, 0, 0, 50, false, null);

    private static readonly GravityDemandModel DemandModel = new();
    private static readonly AircraftType EraType = new("t", "Test", Seats: 100);

    private static StrategyContext MakeContext(IReadOnlyList<AircraftType>? purchasable = null, IReadOnlyList<AircraftType>? leasable = null) =>
        new(Array.Empty<Route>(), ActionCap: 3, Array.Empty<City>(), DemandModel, new[] { EraType }, AirlineFleet.Empty,
            Region.EastAsia, MakeCity("home"), PurchasableTypes: purchasable, LeasableTypes: leasable);

    [Fact]
    public void PurchasableOrEraTypes_NullPurchasableTypes_FallsBackToEraAvailableTypes()
    {
        var context = MakeContext(purchasable: null);

        Assert.Equal(context.EraAvailableTypes, context.PurchasableOrEraTypes);
    }

    [Fact]
    public void PurchasableOrEraTypes_RealPurchasableTypes_UsesItInstead()
    {
        var narrowed = new[] { EraType }.Where(_ => false).ToList(); // empty, but a distinct real list
        var context = MakeContext(purchasable: narrowed);

        Assert.Same(narrowed, context.PurchasableOrEraTypes);
    }

    [Fact]
    public void LeasableOrEraTypes_NullLeasableTypes_FallsBackToEraAvailableTypes()
    {
        var context = MakeContext(leasable: null);

        Assert.Equal(context.EraAvailableTypes, context.LeasableOrEraTypes);
    }

    [Fact]
    public void LeasableOrEraTypes_RealLeasableTypes_UsesItInstead()
    {
        var narrowed = new List<AircraftType>();
        var context = MakeContext(leasable: narrowed);

        Assert.Same(narrowed, context.LeasableOrEraTypes);
    }
}
