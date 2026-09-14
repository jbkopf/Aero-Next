using AeroNext.Core.Airlines;
using AeroNext.Core.Competition;
using AeroNext.Core.Finance;
using AeroNext.Core.Setup;
using AeroNext.Core.Turns;
using AeroNext.Core.World;
using Xunit;

namespace AeroNext.Core.Tests;

/// <summary>
/// M19: proves M15-M18 actually work together through the real resolver
/// chain (QuarterEngine -&gt; CompetitiveMarket -&gt; FinancialResolver), not
/// just that each piece is individually correct in isolation — the same
/// role M14's DeterminismRegressionTests plays for the sim core roadmap.
/// AeroNext.Sim's own M19 demo exercises the identical path for real,
/// printed numbers; this is the regression net that survives a refactor.
/// </summary>
public class GameSetupIntegrationTests
{
    private static string DataDir => Path.Combine(AppContext.BaseDirectory, "data");
    private static IReadOnlyList<City> Cities => CityCatalog.LoadFile(Path.Combine(DataDir, "cities.json"));
    private static IReadOnlyList<AircraftType> Fleet => AircraftCatalog.LoadFile(Path.Combine(DataDir, "aircraft.json"));
    private static IReadOnlyList<DemandGrowthPoint> GrowthSchedule => DemandGrowthCatalog.LoadCampaignSchedule(DataDir);
    private static readonly GravityDemandModel DemandModel = new();

    public static IEnumerable<object[]> Configurations()
    {
        yield return new object[] { "0 humans", Array.Empty<HumanPlayerChoice>() };
        yield return new object[] { "1 human + 3 AI", new[] { new HumanPlayerChoice("Founders Airways", "yyz") } };
        yield return new object[]
        {
            "4 humans",
            new[]
            {
                new HumanPlayerChoice("Atlas Air", "ord"),
                new HumanPlayerChoice("Meridian", "cdg"),
                new HumanPlayerChoice("Skyward", "sin"),
                new HumanPlayerChoice("Continental Wings", "eze"),
            },
        };
    }

    [Theory]
    [MemberData(nameof(Configurations))]
    public void FullChain_EveryConfiguration_ProducesFourDistinctSolventAirlinesAfterQuarter1(
        string label, IReadOnlyList<HumanPlayerChoice> humanChoices)
    {
        var seats = GameSetup.CreateSeats(humanChoices, Cities);
        Assert.Equal(4, seats.Count);
        // Regions aren't required to be distinct across seats (an AI
        // filler can now land in a human-occupied region too) - only
        // starting cities are.
        Assert.Equal(4, seats.Select(s => s.StartingCity.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count());

        var conditionsBySeat = seats.ToDictionary(s => s, s => StartingConditionsResolver.Resolve(s, Cities, Fleet, GrowthSchedule));
        var routesBySeat = new Dictionary<AirlineSeat, IReadOnlyList<Route>>();
        var appliedOrdersBySeat = new Dictionary<AirlineSeat, IReadOnlyList<QuarterOrder>>();

        foreach (var seat in seats)
        {
            var conditions = conditionsBySeat[seat];
            var order = new RouteOrder(seat.StartingCity, conditions.GuaranteedRouteDestination, conditions.StartingAircraft);
            var quarter1 = QuarterEngine.RunQuarter(1, Array.Empty<Route>(), QuarterEngine.BaseActionCap, new[] { order }, Cities, DemandModel);

            Assert.Empty(quarter1.RejectedOrders);
            Assert.Single(quarter1.Routes);

            routesBySeat[seat] = quarter1.Routes;
            appliedOrdersBySeat[seat] = quarter1.AppliedOrders;
        }

        var league = seats.Select(s => new Airline(s.AirlineName, routesBySeat[s])).ToArray();
        var market = CompetitiveMarket.Resolve(league, Cities, DemandModel);

        foreach (var seat in seats)
        {
            var airline = league.Single(a => a.Name == seat.AirlineName);
            var financeStart = FinancialState.Start(conditionsBySeat[seat].StartingCash, Money.FromDollars(10_000_000), interestRatePerQuarter: 0.02);

            var (_, statement) = FinancialResolver.Resolve(financeStart, airline, market, appliedOrdersBySeat[seat]);

            Assert.False(statement.IsBankrupt, $"{label}: {seat.AirlineName} should not be bankrupt after a single starting quarter.");
            Assert.True(market.TotalCarried(airline) > 0, $"{label}: {seat.AirlineName}'s guaranteed route should carry real passengers.");
        }
    }
}
