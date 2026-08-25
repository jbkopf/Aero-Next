namespace AeroNext.Core.Airlines;

public sealed record Airline(string Name, IReadOnlyList<Route> Routes);
