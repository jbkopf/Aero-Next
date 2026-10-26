using AeroNext.Core.World;

namespace AeroNext.Core.Airlines;

/// <summary>
/// Finds the stops a route needs for a given aircraft range
/// (docs/MULTI_STOP_ROUTES_DESIGN.md, Phase 3): nonstop when the aircraft
/// can fly the pair outright, otherwise the fewest refuelling stops at
/// gateway cities, then the shortest total distance, up to
/// <see cref="MaxStops"/>. Ties break on the stops' city ids, so the same
/// inputs always give the same path.
///
/// Built once per decision over a fixed city list: gateway-to-gateway
/// distances are computed up front and each city's distances to the
/// gateways are cached on first use, so checking a pair is a few hundred
/// comparisons rather than fresh great-circle work. The AI checks
/// thousands of pairs per decision, so this matters.
/// </summary>
public sealed class StopPathFinder
{
    /// <summary>Two stops is what a 1955 Pacific crossing needs (Los Angeles - Honolulu - Wake - Tokyo); more is out of scope.</summary>
    public const int MaxStops = 2;

    private readonly IReadOnlyList<City> _gateways;
    private readonly double[,] _gatewayToGatewayKm;
    private readonly Dictionary<string, double[]> _cityToGatewaysKm = new(StringComparer.Ordinal);

    public StopPathFinder(IReadOnlyList<City> allCities)
    {
        _gateways = allCities
            .Where(c => c.IsGateway)
            .OrderBy(c => c.Id, StringComparer.Ordinal)
            .ToList();
        _gatewayToGatewayKm = new double[_gateways.Count, _gateways.Count];

        for (var i = 0; i < _gateways.Count; i++)
        {
            for (var j = 0; j < _gateways.Count; j++)
            {
                _gatewayToGatewayKm[i, j] = GreatCircle.DistanceKm(_gateways[i], _gateways[j]);
            }
        }
    }

    /// <summary>
    /// The stops a route between the two cities needs for an aircraft with
    /// this range: empty when it can fly nonstop, null when no path of at
    /// most <see cref="MaxStops"/> gateway stops keeps every leg in range.
    /// </summary>
    public StopList? ShortestStops(City origin, City destination, double rangeKm)
    {
        if (GreatCircle.DistanceKm(origin, destination) <= rangeKm)
        {
            return StopList.Empty;
        }

        var fromOrigin = DistancesToGateways(origin);
        var toDestination = DistancesToGateways(destination);

        var bestOne = -1;
        var bestOneKm = double.MaxValue;

        for (var g = 0; g < _gateways.Count; g++)
        {
            if (!IsUsableStop(g, origin, destination) || fromOrigin[g] > rangeKm || toDestination[g] > rangeKm)
            {
                continue;
            }

            var total = fromOrigin[g] + toDestination[g];

            if (total < bestOneKm)
            {
                bestOneKm = total;
                bestOne = g;
            }
        }

        if (bestOne >= 0)
        {
            return StopList.Of(_gateways[bestOne]);
        }

        var bestTwo = (-1, -1);
        var bestTwoKm = double.MaxValue;

        for (var g1 = 0; g1 < _gateways.Count; g1++)
        {
            if (!IsUsableStop(g1, origin, destination) || fromOrigin[g1] > rangeKm)
            {
                continue;
            }

            for (var g2 = 0; g2 < _gateways.Count; g2++)
            {
                if (g2 == g1 || !IsUsableStop(g2, origin, destination) || _gatewayToGatewayKm[g1, g2] > rangeKm || toDestination[g2] > rangeKm)
                {
                    continue;
                }

                var total = fromOrigin[g1] + _gatewayToGatewayKm[g1, g2] + toDestination[g2];

                if (total < bestTwoKm)
                {
                    bestTwoKm = total;
                    bestTwo = (g1, g2);
                }
            }
        }

        return bestTwo.Item1 >= 0 ? StopList.Of(_gateways[bestTwo.Item1], _gateways[bestTwo.Item2]) : null;
    }

    /// <summary>A stop can't be either endpoint (RoutePath.AreValidStops).</summary>
    private bool IsUsableStop(int gatewayIndex, City origin, City destination) =>
        _gateways[gatewayIndex].Id != origin.Id && _gateways[gatewayIndex].Id != destination.Id;

    private double[] DistancesToGateways(City city)
    {
        if (!_cityToGatewaysKm.TryGetValue(city.Id, out var distances))
        {
            distances = _gateways.Select(g => GreatCircle.DistanceKm(city, g)).ToArray();
            _cityToGatewaysKm[city.Id] = distances;
        }

        return distances;
    }
}
