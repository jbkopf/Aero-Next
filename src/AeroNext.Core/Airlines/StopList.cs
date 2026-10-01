using AeroNext.Core.World;

namespace AeroNext.Core.Airlines;

/// <summary>
/// The ordered intermediate stops of a multi-stop route
/// (docs/MULTI_STOP_ROUTES_DESIGN.md): cities an aircraft lands at for fuel
/// between a route's two endpoints, carrying no passengers of their own.
/// Empty for a nonstop route, which is every route that doesn't say
/// otherwise - <c>default(StopList)</c> is the empty list, so a
/// <see cref="Route"/> or <see cref="Turns.RouteOrder"/> built without
/// stops needs no change.
///
/// A struct with value equality on purpose: <see cref="Route"/> is a record
/// used as a dictionary key (NetworkPlanner, CompetitiveMarket), and a plain
/// list member would compare by reference and break that silently. Two stop
/// lists are equal when they name the same cities in the same order.
/// </summary>
public readonly struct StopList : IEquatable<StopList>, IReadOnlyList<City>
{
    private readonly City[]? _cities;

    public StopList(IEnumerable<City> cities)
    {
        var array = cities.ToArray();
        _cities = array.Length == 0 ? null : array;
    }

    public static StopList Empty => default;

    public static StopList Of(params City[] cities) => new(cities);

    public int Count => _cities?.Length ?? 0;

    public City this[int index] => (_cities ?? Array.Empty<City>())[index];

    public bool IsEmpty => Count == 0;

    public IEnumerator<City> GetEnumerator() => ((IEnumerable<City>)(_cities ?? Array.Empty<City>())).GetEnumerator();

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();

    public bool Equals(StopList other) =>
        Count == other.Count && this.Select(c => c.Id).SequenceEqual(other.Select(c => c.Id), StringComparer.Ordinal);

    public override bool Equals(object? obj) => obj is StopList other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();

        foreach (var city in this)
        {
            hash.Add(city.Id, StringComparer.Ordinal);
        }

        return hash.ToHashCode();
    }

    public static bool operator ==(StopList left, StopList right) => left.Equals(right);

    public static bool operator !=(StopList left, StopList right) => !left.Equals(right);

    public override string ToString() => IsEmpty ? "nonstop" : "via " + string.Join(", ", this.Select(c => c.Id));
}
