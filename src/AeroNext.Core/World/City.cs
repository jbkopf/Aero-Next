namespace AeroNext.Core.World;

/// <summary>
/// A static city definition loaded from data/cities.json. Gate capacity is
/// the number of simultaneous slot-holders a gateway or chokepoint city can
/// support; null means gate access isn't modeled as scarce there.
/// </summary>
public sealed record City(
    string Id,
    string Name,
    Region Region,
    double Latitude,
    double Longitude,
    int DemandWeight,
    bool IsGateway,
    int? GateCapacity);
