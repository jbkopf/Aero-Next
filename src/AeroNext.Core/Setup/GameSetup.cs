using AeroNext.Core.World;

namespace AeroNext.Core.Setup;

/// <summary>
/// One human player's setup choices. StartingCityId must resolve to a
/// non-gateway city in one of the 4 home regions — see
/// <see cref="HomeRegions.EligibleStartingCities"/>. Gateway cities stay
/// inside the ordinary M9 contested mechanic for everyone, including the
/// local airline, per M15.
/// </summary>
public sealed record HumanPlayerChoice(string AirlineName, string StartingCityId);

/// <summary>
/// One finalized airline's setup outcome — the direct implementation of
/// "four home regions... one per player" (CLAUDE.md): every seat maps to
/// exactly one home region and exactly one starting city within it.
/// </summary>
public sealed record AirlineSeat(string AirlineName, bool IsHuman, Region HomeRegion, City StartingCity);

/// <summary>
/// M17: turns 0-4 human choices into 4 fully-assigned airline seats, filling
/// any seat a human didn't claim with a deterministic AI pick — per
/// CLAUDE.md, the sim never runs with fewer than 4 competing networks. A
/// pure function over structured input; AeroNext.Sim supplies the actual
/// "ask the player" sequence — no console I/O belongs in AeroNext.Core.
///
/// Since the Asia region split (HomeRegions.All has 6 members for exactly 4
/// player slots), this is a genuine draft rather than "one seat per region":
/// humans claim any home region they like, and whichever slots remain go to
/// the richest *unclaimed* regions by RegionalEconomics.Factor — so not
/// every home region appears in every game.
/// </summary>
public static class GameSetup
{
    public static IReadOnlyList<AirlineSeat> CreateSeats(IReadOnlyList<HumanPlayerChoice> humanChoices, IReadOnlyList<City> allCities)
    {
        if (humanChoices.Count > 4)
        {
            throw new ArgumentException($"Human player count must be 0-4, got {humanChoices.Count}.", nameof(humanChoices));
        }

        var citiesById = allCities.ToDictionary(c => c.Id, StringComparer.OrdinalIgnoreCase);
        var claimedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var humanSeatsByRegion = new Dictionary<Region, AirlineSeat>();

        foreach (var choice in humanChoices)
        {
            if (string.IsNullOrWhiteSpace(choice.AirlineName))
            {
                throw new ArgumentException("Airline name cannot be blank.", nameof(humanChoices));
            }

            if (!claimedNames.Add(choice.AirlineName))
            {
                throw new ArgumentException($"Airline name '{choice.AirlineName}' is already taken.", nameof(humanChoices));
            }

            if (!citiesById.TryGetValue(choice.StartingCityId, out var city))
            {
                throw new ArgumentException($"'{choice.StartingCityId}' is not a known city.", nameof(humanChoices));
            }

            if (city.IsGateway || !HomeRegions.IsHomeRegion(city.Region))
            {
                throw new ArgumentException($"'{choice.StartingCityId}' is not an eligible starting city.", nameof(humanChoices));
            }

            if (!humanSeatsByRegion.TryAdd(city.Region, new AirlineSeat(choice.AirlineName, IsHuman: true, city.Region, city)))
            {
                throw new ArgumentException($"Region '{city.Region}' is already claimed by another airline.", nameof(humanChoices));
            }
        }

        var seats = new List<AirlineSeat>(humanSeatsByRegion.Values);
        var remainingSlots = 4 - seats.Count;

        var aiRegions = HomeRegions.All
            .Where(r => !humanSeatsByRegion.ContainsKey(r))
            .Select(r => (Region: r, Factor: RegionalEconomics.Factor(r, allCities)))
            .OrderByDescending(x => x.Factor)
            .ThenBy(x => x.Region.ToString(), StringComparer.Ordinal)
            .Take(remainingSlots)
            .Select(x => x.Region);

        foreach (var region in aiRegions)
        {
            var startingCity = HomeRegions.EligibleStartingCities(region, allCities)
                .OrderByDescending(c => c.DemandWeight)
                .ThenBy(c => c.Id, StringComparer.Ordinal)
                .First();

            var aiName = $"{region} Air";
            if (!claimedNames.Add(aiName))
            {
                throw new ArgumentException($"AI airline name '{aiName}' collides with a human-chosen name.", nameof(humanChoices));
            }

            seats.Add(new AirlineSeat(aiName, IsHuman: false, region, startingCity));
        }

        return seats.OrderBy(s => s.HomeRegion.ToString(), StringComparer.Ordinal).ToList();
    }
}
