using AeroNext.Core.Airlines;
using AeroNext.Core.World;
using Xunit;

namespace AeroNext.Core.Tests;

public class TradeRestrictionTests
{
    private static City MakeCity(string id) => new(id, id.ToUpperInvariant(), Region.EastAsia, 0, 0, 50, false, null);

    private static AircraftType MakeAircraft(PoliticalBloc? bloc) => new("t", "Test", Seats: 100, Bloc: bloc);

    [Fact]
    public void IsRestricted_HomeCityNull_AlwaysFalse()
    {
        var aircraft = MakeAircraft(PoliticalBloc.American);
        var restrictions = new[] { new TradeRestriction("pek", PoliticalBloc.American, 1949, 1972) };

        Assert.False(TradeRestriction.IsRestricted(aircraft, homeCity: null, year: 1965, restrictions));
    }

    [Fact]
    public void IsRestricted_AircraftBlocNull_AlwaysFalse()
    {
        var beijing = MakeCity("pek");
        var aircraft = MakeAircraft(bloc: null);
        var restrictions = new[] { new TradeRestriction("pek", PoliticalBloc.American, 1949, 1972) };

        Assert.False(TradeRestriction.IsRestricted(aircraft, beijing, year: 1965, restrictions));
    }

    [Fact]
    public void IsRestricted_NoMatchingRestrictionForThisCity_False()
    {
        var paris = MakeCity("cdg");
        var aircraft = MakeAircraft(PoliticalBloc.American);
        var restrictions = new[] { new TradeRestriction("pek", PoliticalBloc.American, 1949, 1972) };

        Assert.False(TradeRestriction.IsRestricted(aircraft, paris, year: 1965, restrictions));
    }

    [Fact]
    public void IsRestricted_MatchingCityButDifferentBloc_False()
    {
        var beijing = MakeCity("pek");
        var europeanAircraft = MakeAircraft(PoliticalBloc.European);
        var restrictions = new[] { new TradeRestriction("pek", PoliticalBloc.American, 1949, 1972) };

        Assert.False(TradeRestriction.IsRestricted(europeanAircraft, beijing, year: 1965, restrictions));
    }

    [Fact]
    public void IsRestricted_BeforeStartYear_False()
    {
        var beijing = MakeCity("pek");
        var aircraft = MakeAircraft(PoliticalBloc.American);
        var restrictions = new[] { new TradeRestriction("pek", PoliticalBloc.American, 1949, 1972) };

        Assert.False(TradeRestriction.IsRestricted(aircraft, beijing, year: 1948, restrictions));
    }

    [Fact]
    public void IsRestricted_AtStartYear_True()
    {
        var beijing = MakeCity("pek");
        var aircraft = MakeAircraft(PoliticalBloc.American);
        var restrictions = new[] { new TradeRestriction("pek", PoliticalBloc.American, 1949, 1972) };

        Assert.True(TradeRestriction.IsRestricted(aircraft, beijing, year: 1949, restrictions));
    }

    [Fact]
    public void IsRestricted_AtEndYear_StillTrue()
    {
        var beijing = MakeCity("pek");
        var aircraft = MakeAircraft(PoliticalBloc.American);
        var restrictions = new[] { new TradeRestriction("pek", PoliticalBloc.American, 1949, 1972) };

        Assert.True(TradeRestriction.IsRestricted(aircraft, beijing, year: 1972, restrictions));
    }

    [Fact]
    public void IsRestricted_AfterEndYear_False()
    {
        var beijing = MakeCity("pek");
        var aircraft = MakeAircraft(PoliticalBloc.American);
        var restrictions = new[] { new TradeRestriction("pek", PoliticalBloc.American, 1949, 1972) };

        Assert.False(TradeRestriction.IsRestricted(aircraft, beijing, year: 1973, restrictions));
    }

    [Fact]
    public void IsRestricted_NullEndYear_NeverLiftedWithinTheCampaign()
    {
        var havana = MakeCity("hav");
        var aircraft = MakeAircraft(PoliticalBloc.American);
        var restrictions = new[] { new TradeRestriction("hav", PoliticalBloc.American, 1960, null) };

        Assert.True(TradeRestriction.IsRestricted(aircraft, havana, year: 1960, restrictions));
        Assert.True(TradeRestriction.IsRestricted(aircraft, havana, year: 2015, restrictions));
    }

    [Fact]
    public void IsRestricted_TwoRestrictionsSameCity_EachBlocGatedIndependently()
    {
        // Beijing's own double-embargo: American 1949-1972, Soviet
        // 1960-1989 - a European aircraft is never restricted, and each
        // of the other two blocs follows its own window independently.
        var beijing = MakeCity("pek");
        var restrictions = new[]
        {
            new TradeRestriction("pek", PoliticalBloc.American, 1949, 1972),
            new TradeRestriction("pek", PoliticalBloc.Soviet, 1960, 1989),
        };

        Assert.True(TradeRestriction.IsRestricted(MakeAircraft(PoliticalBloc.American), beijing, 1965, restrictions));
        Assert.True(TradeRestriction.IsRestricted(MakeAircraft(PoliticalBloc.Soviet), beijing, 1965, restrictions));
        Assert.False(TradeRestriction.IsRestricted(MakeAircraft(PoliticalBloc.European), beijing, 1965, restrictions));

        // 1958: Soviet window hasn't started yet, American already has.
        Assert.True(TradeRestriction.IsRestricted(MakeAircraft(PoliticalBloc.American), beijing, 1958, restrictions));
        Assert.False(TradeRestriction.IsRestricted(MakeAircraft(PoliticalBloc.Soviet), beijing, 1958, restrictions));

        // 1975: American window has closed, Soviet is still active.
        Assert.False(TradeRestriction.IsRestricted(MakeAircraft(PoliticalBloc.American), beijing, 1975, restrictions));
        Assert.True(TradeRestriction.IsRestricted(MakeAircraft(PoliticalBloc.Soviet), beijing, 1975, restrictions));
    }
}
