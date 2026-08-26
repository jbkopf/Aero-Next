using AeroNext.Core.Airlines;
using AeroNext.Core.Alliances;
using AeroNext.Core.Turns;
using Xunit;

namespace AeroNext.Core.Tests;

public class AllianceResolverTests
{
    private static readonly RegionalCarrier Carrier = new("carrier-a", "Carrier A");

    [Fact]
    public void ResolveQuarter_SoleSuitor_Wins()
    {
        var airline = new Airline("A", Array.Empty<Route>());
        var orders = new[] { (airline, new AllianceCourtOrder(Carrier)) };

        var (board, outcomes) = AllianceResolver.ResolveQuarter(AllianceBoard.Empty, orders);

        Assert.True(outcomes.Single().Won);
        Assert.False(board.IsAvailable(Carrier));
    }

    [Fact]
    public void ResolveQuarter_HigherQuality_WinsOverLowerQuality()
    {
        var premium = new Airline("Premium", Array.Empty<Route>(), Quality: 2.0);
        var basic = new Airline("Basic", Array.Empty<Route>(), Quality: 1.0);
        var orders = new[]
        {
            (premium, new AllianceCourtOrder(Carrier)),
            (basic, new AllianceCourtOrder(Carrier)),
        };

        var (_, outcomes) = AllianceResolver.ResolveQuarter(AllianceBoard.Empty, orders);

        Assert.True(outcomes.Single(o => o.Airline.Name == "Premium").Won);
        Assert.False(outcomes.Single(o => o.Airline.Name == "Basic").Won);
    }

    [Fact]
    public void ResolveQuarter_TiedQuality_BreaksAlphabeticallyByAirlineName()
    {
        var zAirline = new Airline("Zulu", Array.Empty<Route>(), Quality: 1.0);
        var aAirline = new Airline("Alpha", Array.Empty<Route>(), Quality: 1.0);
        var orders = new[]
        {
            (zAirline, new AllianceCourtOrder(Carrier)),
            (aAirline, new AllianceCourtOrder(Carrier)),
        };

        var (_, outcomes) = AllianceResolver.ResolveQuarter(AllianceBoard.Empty, orders);

        Assert.True(outcomes.Single(o => o.Airline.Name == "Alpha").Won);
    }

    [Fact]
    public void ResolveQuarter_CarrierAlreadyJoined_EverySuitorLosesAndBoardUnchanged()
    {
        var priorWinner = "Incumbent";
        var board = AllianceBoard.Empty with { JoinedCarrierToAirlineName = new Dictionary<string, string> { [Carrier.Id] = priorWinner } };
        var newSuitor = new Airline("NewSuitor", Array.Empty<Route>(), Quality: 100.0); // even a huge quality edge can't undo it
        var orders = new[] { (newSuitor, new AllianceCourtOrder(Carrier)) };

        var (resolvedBoard, outcomes) = AllianceResolver.ResolveQuarter(board, orders);

        Assert.False(outcomes.Single().Won);
        Assert.Equal(priorWinner, resolvedBoard.JoinedCarrierToAirlineName[Carrier.Id]);
    }

    [Fact]
    public void ResolveQuarter_MultipleCarriersContestedSimultaneously_ResolveIndependently()
    {
        var carrierX = new RegionalCarrier("x", "Carrier X");
        var carrierY = new RegionalCarrier("y", "Carrier Y");
        var a = new Airline("A", Array.Empty<Route>(), Quality: 2.0);
        var b = new Airline("B", Array.Empty<Route>(), Quality: 1.0);
        var orders = new[]
        {
            (a, new AllianceCourtOrder(carrierX)),
            (b, new AllianceCourtOrder(carrierX)),
            (b, new AllianceCourtOrder(carrierY)), // B is the sole suitor for Y
        };

        var (board, outcomes) = AllianceResolver.ResolveQuarter(AllianceBoard.Empty, orders);

        Assert.True(outcomes.Single(o => o.Target.Id == "x" && o.Airline.Name == "A").Won);
        Assert.False(outcomes.Single(o => o.Target.Id == "x" && o.Airline.Name == "B").Won);
        Assert.True(outcomes.Single(o => o.Target.Id == "y").Won);
        Assert.False(board.IsAvailable(carrierX));
        Assert.False(board.IsAvailable(carrierY));
    }

    [Fact]
    public void ResolveQuarter_IsDeterministic_RegardlessOfOrderInputOrder()
    {
        var a = new Airline("A", Array.Empty<Route>(), Quality: 1.0);
        var b = new Airline("B", Array.Empty<Route>(), Quality: 1.0);
        var c = new Airline("C", Array.Empty<Route>(), Quality: 1.0);
        var orders = new[]
        {
            (a, new AllianceCourtOrder(Carrier)),
            (b, new AllianceCourtOrder(Carrier)),
            (c, new AllianceCourtOrder(Carrier)),
        };

        var (_, outcomes1) = AllianceResolver.ResolveQuarter(AllianceBoard.Empty, orders);
        var (_, outcomes2) = AllianceResolver.ResolveQuarter(AllianceBoard.Empty, orders.Reverse().ToArray());

        Assert.Equal(
            outcomes1.Select(o => (o.Airline.Name, o.Won)),
            outcomes2.Select(o => (o.Airline.Name, o.Won)));
    }

    [Fact]
    public void QuarterEngine_Cost_AllianceCourtOrder_AlwaysCostsOne()
    {
        Assert.Equal(1, QuarterEngine.Cost(new AllianceCourtOrder(Carrier), Array.Empty<Route>()));
    }
}
