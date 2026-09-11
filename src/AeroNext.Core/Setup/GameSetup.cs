using AeroNext.Core;
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
    /// <param name="fillerRegionRng">
    /// docs/OPEN_WORK.md's filler-seat investigation: null (the default)
    /// keeps this method's own long-shipped behavior — richest-unclaimed-
    /// region-first, deterministic, byte-identical. When supplied, each
    /// remaining slot instead draws a region by weighted random (weight =
    /// <see cref="RegionalEconomics.Factor"/>) without replacement, via
    /// <see cref="WeightedRandomRegionsWithoutReplacement"/> — richer
    /// regions stay more likely, but AustralAsia/SouthWestAsia are no
    /// longer excluded from filler outright, and EastAsia/NorthAmerica no
    /// longer co-occur as filler in almost every AI-heavy game. Per
    /// CLAUDE.md's determinism rules, the caller's own seeded
    /// <see cref="SimRng"/> — never a new ambient random source.
    /// </param>
    public static IReadOnlyList<AirlineSeat> CreateSeats(
        IReadOnlyList<HumanPlayerChoice> humanChoices, IReadOnlyList<City> allCities, SimRng? fillerRegionRng = null)
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
        var unclaimedRegions = HomeRegions.All.Where(r => !claimedRegions.Contains(r)).ToList();

        var aiRegions = fillerRegionRng is null
            ? unclaimedRegions
                .Select(r => (Region: r, Factor: RegionalEconomics.Factor(r, allCities)))
                .OrderByDescending(x => x.Factor)
                .ThenBy(x => x.Region.ToString(), StringComparer.Ordinal)
                .Take(remainingSlots)
                .Select(x => x.Region)
            : WeightedRandomRegionsWithoutReplacement(unclaimedRegions, allCities, remainingSlots, fillerRegionRng)
                .OrderBy(r => r.ToString(), StringComparer.Ordinal);

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

    /// <summary>
    /// Weighted sampling without replacement: repeatedly draws one region
    /// from whatever remains, with probability proportional to its own
    /// <see cref="RegionalEconomics.Factor"/>, removes it, and repeats.
    /// Richer regions stay more likely per draw, but every candidate has a
    /// real, nonzero chance — unlike a strict ranked cut. <paramref
    /// name="candidates"/> must already be in a stable, deterministic order
    /// (it is — <see cref="HomeRegions.All"/> filtered by a `Where`) so the
    /// same <paramref name="rng"/> sequence always reproduces the same draw.
    /// </summary>
    private static IReadOnlyList<Region> WeightedRandomRegionsWithoutReplacement(
        IReadOnlyList<Region> candidates, IReadOnlyList<City> allCities, int count, SimRng rng)
    {
        var pool = candidates.Select(r => (Region: r, Weight: RegionalEconomics.Factor(r, allCities))).ToList();
        var chosen = new List<Region>();

        while (chosen.Count < count && pool.Count > 0)
        {
            var totalWeight = pool.Sum(x => x.Weight);
            var roll = rng.NextDouble() * totalWeight;
            var cumulative = 0.0;
            var pickIndex = pool.Count - 1; // floating-point fallback: last item absorbs any rounding slop

            for (var i = 0; i < pool.Count; i++)
            {
                cumulative += pool[i].Weight;
                if (roll < cumulative)
                {
                    pickIndex = i;
                    break;
                }
            }

            chosen.Add(pool[pickIndex].Region);
            pool.RemoveAt(pickIndex);
        }

        return chosen;
    }
}
