namespace AeroNext.Core.World;

/// <summary>
/// Geopolitical-aircraft-restrictions follow-up
/// (docs/GEOPOLITICAL_AIRCRAFT_RESTRICTIONS_ROADMAP.md): which bloc's
/// industry built an <see cref="Airlines.AircraftType"/>, for
/// <see cref="TradeRestriction"/> to key off of. Three values, not two -
/// American and European only ever collapse into "the West" for the
/// Soviet-facing restriction (COCOM applied broadly); for the
/// China-facing restriction they diverge, since Britain was never bound
/// by the US-specific embargo (real history: CAAC bought Vickers
/// Viscounts in 1963 and ordered Hawker Siddeley Tridents directly in
/// August 1971, a full year before Boeing's own first China sale). A
/// single undifferentiated "Western" bloc can't represent that split.
/// </summary>
public enum PoliticalBloc
{
    American,
    European,
    Soviet,
}
