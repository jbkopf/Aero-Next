using AeroNext.Core.World;

namespace AeroNext.Core.Setup;

/// <summary>
/// One human player's setup choices. StartingCityId must resolve to a
/// non-gateway city in one of the home regions — see
/// <see cref="HomeRegions.EligibleStartingCities"/>. Gateway cities stay
/// inside the ordinary M9 contested mechanic for everyone, including the
/// local airline, per M15.
/// </summary>
public sealed record HumanPlayerChoice(string AirlineName, string StartingCityId);

/// <summary>
/// One finalized airline's setup outcome. Every seat maps to exactly one
/// home region and exactly one starting city within it — but as of the
/// same-region change below, region is no longer unique across seats; only
/// the starting city is.
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
///
/// **Regions are no longer exclusive.** Multiple humans (or a human and an
/// AI) can now share a home region, as long as their starting cities are
/// distinct — CLAUDE.md's "a home region cannot fund a win" pillar already
/// assumes rivals can converge on the same demand pool; a region was never
/// meant to be a private lane. AI region *choice* stays exactly as naive as
/// before (still just "richest unclaimed region," never doubling up on its
/// own) — giving AI a reason to deliberately share a region with a human is
/// real strategy, left for the AI fleshout work, not bolted on here.
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
        var claimedCityIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var claimedRegions = new HashSet<Region>();
        var humanSeats = new List<AirlineSeat>();

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

            if (!claimedCityIds.Add(city.Id))
            {
                throw new ArgumentException($"'{city.Id}' is already claimed by another airline.", nameof(humanChoices));
            }

            claimedRegions.Add(city.Region);
            humanSeats.Add(new AirlineSeat(choice.AirlineName, IsHuman: true, city.Region, city));
        }

        var seats = new List<AirlineSeat>(humanSeats);
        var remainingSlots = 4 - seats.Count;

        var aiRegions = HomeRegions.All
            .Where(r => !claimedRegions.Contains(r))
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

        return seats
            .OrderBy(s => s.HomeRegion.ToString(), StringComparer.Ordinal)
            .ThenBy(s => s.AirlineName, StringComparer.Ordinal)
            .ToList();
    }
}
