using AeroNext.Core.Airlines;
using AeroNext.Core.World;

namespace AeroNext.Core.Turns;

/// <summary>
/// Sets a route's aircraft and frequency for the coming quarter — covers
/// both "enter a new city-pair" and "add frequency to a pair already served"
/// (CLAUDE.md's market actions) with one shape, since applying an order both
/// creates a route that doesn't exist yet and updates one that does.
/// </summary>
public sealed record Order(City Origin, City Destination, AircraftType Aircraft, int WeeklyRoundTrips);
