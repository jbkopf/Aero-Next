using AeroNext.Core.Airlines;

namespace AeroNext.Core.Competition;

/// <summary>
/// What a passenger weighs when several airlines offer the same city pair
/// (docs/MULTI_STOP_ROUTES_DESIGN.md, Phase 2): the fare they pay plus the
/// time the journey takes, priced at <see cref="ValueOfTimePerHour"/>. A
/// multi-stop route flies further and spends time on the ground at each
/// stop, and a one-stop connection pays two fares and waits at the hub -
/// both cost the passenger more than a nonstop, with no flat penalty for
/// having a stop. <see cref="CompetitiveMarket"/> compares these costs
/// across rivals to split a contested pair's passengers.
/// </summary>
public static class JourneyCost
{
    /// <summary>
    /// (Proposed, tunable) — what an hour of a passenger's journey is worth
    /// against the fare, in the game's money. Set so time is a real but
    /// secondary part of the choice: a 1955 transatlantic DC-7 journey via
    /// Gander comes to roughly 15-20% time, 80-85% fare.
    /// </summary>
    public static readonly Money ValueOfTimePerHour = Money.FromDollars(20);

    /// <summary>(Proposed, tunable) — time a passenger spends at the hub changing aircraft on a one-stop connection between two separate routes.</summary>
    public const double ConnectionHours = 2.0;

    /// <summary>
    /// Door-to-door hours on the itinerary: flying time on every leg (each
    /// route's full distance, stops included, at its aircraft's cruise
    /// speed), an hour on the ground at each refuelling stop
    /// (<see cref="FlightPerformance.TurnaroundHoursPerLanding"/>), and
    /// <see cref="ConnectionHours"/> at each change of aircraft.
    /// </summary>
    public static double JourneyHours(Itinerary itinerary) =>
        itinerary.Legs.Sum(leg => leg.DistanceKm / leg.Aircraft.CruiseSpeedKmh + leg.Stops.Count * FlightPerformance.TurnaroundHoursPerLanding)
        + Math.Max(0, itinerary.Legs.Count - 1) * ConnectionHours;

    /// <summary>The fare a passenger pays for the whole journey - every leg's fare, since a connection between two routes means two tickets.</summary>
    public static Money FarePaid(Itinerary itinerary) =>
        itinerary.Legs.Aggregate(Money.Zero, (total, leg) => total + leg.FarePerPassenger);

    /// <summary>Fare paid plus journey time at <see cref="ValueOfTimePerHour"/>.</summary>
    public static Money PerPassenger(Itinerary itinerary) =>
        FarePaid(itinerary) + new Money((long)Math.Round(ValueOfTimePerHour.Cents * JourneyHours(itinerary), MidpointRounding.AwayFromZero));
}
