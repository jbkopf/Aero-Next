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
/// unclaimed regions — so not every home region appears in every game. Which
/// unclaimed regions is itself a real, tunable choice: the plain default
/// below (no fillerRegionRng) is a deterministic richest-*unclaimed*-first
/// cut by RegionalEconomics.Factor, still exactly what it's always been —
/// but the real QA harness (AeroNext.QaHarness/Program.cs, the closest
/// thing to "the real game" this headless-sim phase has) no longer calls
/// it that way. EastAsia-dominance follow-up (docs/OPEN_WORK.md): that
/// deterministic cut structurally excluded AustralAsia/SouthWestAsia from
/// filler in most games (both rank 5th/6th by Factor) — a real 300-game
/// sweep found EastAsia's own win rate was 0% in the minority of games
/// where both were actually seated by a real competing airline, vs 61.3%
/// when neither was: EastAsia's whole dominance was a walkover, not a real
/// fight. The real harness now passes fillerRegionRng with
/// fillerWeightExponent: 0.0 (uniform-random among unclaimed regions,
/// wealth ignored entirely) — measured on the actual shipped wiring across
/// two independent 300-game seed ranges: EastAsia's own win share
/// collapsed from 47.0% to 11.3%, Europe became the new leading region at
/// 31-34% (plausible given its own real fundamentals — most cities of any
/// region, highest total demand weight), with bankruptcy essentially
/// unchanged. (An earlier pass at this measurement wrongly named
/// NorthAmerica as the new leader, from a since-fixed bug in
/// RegionalOutcomeReport.cs that read "who ended richest" instead of the
/// real win condition — see docs/OPEN_WORK.md's own correction.
/// NorthAmerica is actually the second-weakest region here, 10-13%.)
///
/// The deterministic default here stays
/// exactly as-is (many existing tests and callers rely on it
/// byte-for-byte) — see fillerRegionRng's own doc comment below for the
/// full mechanism.
///
/// **Regions are no longer exclusive between humans.** Multiple humans can
/// share a home region, as long as their starting cities are distinct —
/// CLAUDE.md's "a home region cannot fund a win" pillar already assumes
/// rivals can converge on the same demand pool; a region was never meant to
/// be a private lane between humans. **AI never contests a human's own
/// region, though** — a direct product/UX decision, not a balance one: an
/// AI seat competing with a human from turn one, in a region the human
/// specifically chose, isn't a fair fight the human opted into the way two
/// humans sharing a region (with the real UI expected to surface a warning
/// about it — a tooltip or similar, out of this headless core's scope) are.
/// AI filler's own region draw excludes every human-claimed region, exactly
/// as it always has; AI-vs-AI collisions stay impossible too, by the same
/// without-replacement draw. This was tried the other way once — letting
/// filler reach for a human's own region under its normal richest-first
/// criteria — and reverted after measurement showed a real regression
/// (docs/OPEN_WORK.md): total bankruptcy 2.33%→3.00%, concentrated in the
/// richest regions getting contested *more* often, not less, since nothing
/// about that change addressed the underlying deterministic-collision
/// problem it was meant to route around.
/// </summary>
public static class GameSetup
{
    /// <param name="fillerRegionRng">
    /// docs/OPEN_WORK.md's filler-seat investigation: null (the default)
    /// keeps this method's own long-shipped behavior — richest-unclaimed-
    /// region-first, deterministic, byte-identical. When supplied, each
    /// remaining slot instead draws a region by weighted random (weight =
    /// <see cref="RegionalEconomics.Factor"/> raised to
    /// <paramref name="fillerWeightExponent"/>) without replacement, via
    /// <see cref="WeightedRandomRegionsWithoutReplacement"/> — richer
    /// regions stay more likely, but AustralAsia/SouthWestAsia are no
    /// longer excluded from filler outright, and EastAsia/NorthAmerica no
    /// longer co-occur as filler in almost every AI-heavy game. Either way,
    /// a human-claimed region is never a candidate at all — see this
    /// class's own top-level doc comment for why. Per CLAUDE.md's
    /// determinism rules, the caller's own seeded <see cref="SimRng"/> —
    /// never a new ambient random source.
    /// </param>
    /// <param name="fillerWeightExponent">
    /// Only read when <paramref name="fillerRegionRng"/> is supplied. 1.0
    /// (the default) weights draws by the raw Factor. 0.0 makes every
    /// unclaimed region equally likely regardless of wealth (pure uniform
    /// random). Above 1.0 sharpens the bias back toward richer regions
    /// without returning to a strict deterministic cut — e.g. 2.0 squares
    /// each region's own Factor before weighting.
    /// </param>
    public static IReadOnlyList<AirlineSeat> CreateSeats(
        IReadOnlyList<HumanPlayerChoice> humanChoices, IReadOnlyList<City> allCities,
        SimRng? fillerRegionRng = null, double fillerWeightExponent = 1.0)
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
            : WeightedRandomRegionsWithoutReplacement(unclaimedRegions, allCities, remainingSlots, fillerRegionRng, fillerWeightExponent)
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
    /// <see cref="RegionalEconomics.Factor"/> raised to
    /// <paramref name="weightExponent"/>, removes it, and repeats. Richer
    /// regions stay more likely per draw, but every candidate has a real,
    /// nonzero chance — unlike a strict ranked cut. <paramref
    /// name="candidates"/> must already be in a stable, deterministic order
    /// (it is — <see cref="HomeRegions.All"/> filtered by a `Where`) so the
    /// same <paramref name="rng"/> sequence always reproduces the same draw.
    /// </summary>
    private static IReadOnlyList<Region> WeightedRandomRegionsWithoutReplacement(
        IReadOnlyList<Region> candidates, IReadOnlyList<City> allCities, int count, SimRng rng, double weightExponent)
    {
        var pool = candidates.Select(r => (Region: r, Weight: Math.Pow(RegionalEconomics.Factor(r, allCities), weightExponent))).ToList();
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
