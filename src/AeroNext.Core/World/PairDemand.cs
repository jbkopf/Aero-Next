namespace AeroNext.Core.World;

public sealed record PairDemand(City Origin, City Destination, double DistanceKm, long PassengersPerQuarter);
