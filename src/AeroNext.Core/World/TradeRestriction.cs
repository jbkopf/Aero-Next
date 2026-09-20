using AeroNext.Core.Airlines;

namespace AeroNext.Core.World;

/// <summary>
/// Geopolitical-aircraft-restrictions follow-up
/// (docs/GEOPOLITICAL_AIRCRAFT_RESTRICTIONS_ROADMAP.md): a real historical
/// embargo, keyed to a specific <see cref="City.Id"/> rather than a
/// <see cref="Region"/> - the whole reason this exists rather than a
/// simpler region-scoped rule is that every real embargo checked cuts
/// through an existing game region rather than along its edge (Moscow
/// sits in "Europe" alongside London; Beijing sits in "EastAsia"
/// alongside Taipei). A restricted airline can still fly whatever it
/// already owns and can still acquire any aircraft outside
/// <see cref="RestrictedBloc"/> - this only ever gates *acquiring* a new
/// one of the restricted bloc, the same "flying vs. acquiring" split
/// <see cref="Timeline.IsAvailable"/> vs.
/// <see cref="Timeline.IsAvailableForPurchase"/>/
/// <see cref="Timeline.IsAvailableForLease"/> already established.
/// </summary>
public sealed record TradeRestriction(string CityId, PoliticalBloc RestrictedBloc, int StartYear, int? EndYear)
{
    /// <summary>
    /// True when <paramref name="homeCity"/>'s own airline can't newly
    /// acquire <paramref name="aircraft"/> in <paramref name="year"/>.
    /// <paramref name="homeCity"/>: null (no visibility) and
    /// <paramref name="aircraft"/>.Bloc: null (not modeled) both mean "no
    /// restriction ever applies" - the same permissive-default reading
    /// every optional identity field in this codebase already gets.
    /// </summary>
    public static bool IsRestricted(AircraftType aircraft, City? homeCity, int year, IReadOnlyList<TradeRestriction> restrictions) =>
        homeCity is not null
        && aircraft.Bloc is not null
        && restrictions.Any(r =>
            r.CityId == homeCity.Id
            && r.RestrictedBloc == aircraft.Bloc
            && r.StartYear <= year
            && (r.EndYear is null || year <= r.EndYear));
}
