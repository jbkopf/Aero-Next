using AeroNext.Core.Airlines;

namespace AeroNext.Core.Competition;

public sealed record CompetitiveMarketResult(IReadOnlyList<CompetitivePairResult> Pairs, IReadOnlyList<CompetitiveRouteUsage> RouteUsage)
{
    public long TotalCarried(Airline airline) => Pairs.Sum(p => p.CarriedByAirline.GetValueOrDefault(airline));
}
