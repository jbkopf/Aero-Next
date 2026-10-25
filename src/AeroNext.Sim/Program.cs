using AeroNext.Core;
using AeroNext.Core.Ai;
using AeroNext.Core.Airlines;
using AeroNext.Core.Alliances;
using AeroNext.Core.Competition;
using AeroNext.Core.Finance;
using AeroNext.Core.Fleet;
using AeroNext.Core.Negotiations;
using AeroNext.Core.Setup;
using AeroNext.Core.Turns;
using AeroNext.Core.World;

// Headless sim entry point. Per CLAUDE.md's current phase: load cities, run
// scripted quarters, print results as text. No rendering, no input, no UI.
var dataPath = Path.Combine(AppContext.BaseDirectory, "data", "cities.json");
var cities = CityCatalog.LoadFile(dataPath);
var byId = cities.ToDictionary(c => c.Id);

Console.WriteLine($"Loaded {cities.Count} cities from {dataPath}");
Console.WriteLine();
Console.WriteLine($"{"Id",-5} {"Name",-16} {"Region",-13} {"Demand",6} {"Gateway",-7} {"Cap",3}");

foreach (var city in cities)
{
    var gateway = city.IsGateway ? "yes" : "no";
    var cap = city.GateCapacity?.ToString() ?? "-";
    Console.WriteLine($"{city.Id,-5} {city.Name,-16} {city.Region,-13} {city.DemandWeight,6} {gateway,-7} {cap,3}");
}

var demandModel = new GravityDemandModel(enableHubDemandBonus: true, intraRegionMultipliers: GravityDemandModel.ShippedIntraRegionMultipliers,
    groundCompetitionDistanceKm: GravityDemandModel.ShippedGroundCompetitionDistanceKm);
var allPairs = demandModel.AllPairs(cities);

Console.WriteLine();
Console.WriteLine("Top 15 city-pair markets by quarterly demand:");
Console.WriteLine($"{"Origin",-6} {"Destination",-16} {"Dist (km)",10} {"Pax/qtr",12}");

foreach (var pair in allPairs.OrderByDescending(p => p.PassengersPerQuarter).Take(15))
{
    Console.WriteLine($"{pair.Origin.Id,-6} {pair.Destination.Id,-16} {pair.DistanceKm,10:N0} {pair.PassengersPerQuarter,12:N0}");
}

Console.WriteLine();
Console.WriteLine("Bottom 5 city-pair markets by quarterly demand (chokepoints, mostly):");

foreach (var pair in allPairs.OrderBy(p => p.PassengersPerQuarter).Take(5))
{
    Console.WriteLine($"{pair.Origin.Id,-6} {pair.Destination.Id,-16} {pair.DistanceKm,10:N0} {pair.PassengersPerQuarter,12:N0}");
}

// --- M13: historical arc — the full ~240-quarter campaign, 1955-2015 ---
// Always 4 airlines, all AI-driven (0 human-scripted satisfies "at most 1").
Console.WriteLine();
Console.WriteLine("--- M13: full campaign, 1955-2015, 4 AI-driven airlines ---");

var fleetCatalog = AircraftCatalog.LoadFile(Path.Combine(AppContext.BaseDirectory, "data", "aircraft.json"));
var growthSchedule = DemandGrowthCatalog.LoadCampaignSchedule(Path.Combine(AppContext.BaseDirectory, "data")); // M21: baseline curve + authored historical_events.json, merged
var campaignKnownCityIds = cities.Select(c => c.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
var campaignTradeRestrictions = TradeRestrictionCatalog.LoadFile(Path.Combine(AppContext.BaseDirectory, "data", "trade-restrictions.json"), campaignKnownCityIds); // geopolitical-aircraft-restrictions follow-up

const int campaignQuarters = 244; // 1955 Q1 through 2015 Q4
var campaignAirlineNames = new[] { "Atlas Air", "Meridian", "Skyward", "Continental Wings" };

// M39: real per-region starting conditions, mirroring GameSetup.CreateSeats's
// own AI-region-picking algorithm (top 4 HomeRegions by RegionalEconomics.Factor,
// tie-broken by region name, each paired with its own top EligibleStartingCities
// entry by DemandWeight) - zipped against this loop's existing flavor names
// rather than renamed to "$Region Air", so every prior quarter's own writeup
// below (which names these airlines directly) stays valid. This replaces the
// prior deliberately-flat/symmetric setup (same cash, same aircraft, no
// region) - a reversal the M39 roadmap section itself already flagged.
// Deliberately NOT adopting FullGameRunner.cs's M18 guaranteed-quarter-1-route
// seed here - M39's own region-aware strategies already pick a sensible
// quarter-1 target on their own (unlike the old flat BaselineAi.DecideOrders
// call this replaced), and layering the guaranteed-route mechanic on top
// would be a second, separable change; if quarter-1 convergence is still
// observed once this is measured, that's a finding to report, not a bug to
// silently chase inside this same milestone.
var campaignRegions = HomeRegions.All
    .Select(r => (Region: r, Factor: RegionalEconomics.Factor(r, cities)))
    .OrderByDescending(x => x.Factor)
    .ThenBy(x => x.Region.ToString(), StringComparer.Ordinal)
    .Take(4)
    .Select(x => x.Region)
    .ToList();

var campaignSeats = campaignAirlineNames.Zip(campaignRegions, (name, region) =>
{
    var startingCity = HomeRegions.EligibleStartingCities(region, cities)
        .OrderByDescending(c => c.DemandWeight)
        .ThenBy(c => c.Id, StringComparer.Ordinal)
        .First();
    return new AirlineSeat(name, IsHuman: false, region, startingCity);
}).ToDictionary(s => s.AirlineName);

var campaignConditions = campaignAirlineNames.ToDictionary(n => n, n => StartingConditionsResolver.Resolve(campaignSeats[n], cities, fleetCatalog, growthSchedule));
var campaignHomeRegionByName = campaignAirlineNames.ToDictionary(n => n, n => campaignSeats[n].HomeRegion);
var campaignRoutes = campaignAirlineNames.ToDictionary(n => n, _ => (IReadOnlyList<Route>)Array.Empty<Route>());
var campaignCaps = campaignAirlineNames.ToDictionary(n => n, _ => QuarterEngine.BaseActionCap);
var campaignFinances = campaignAirlineNames.ToDictionary(n => n, n => FinancialState.Start(campaignConditions[n].StartingCash, Money.FromDollars(0), interestRatePerQuarter: 0.02));
var campaignCurrentStrategyByName = campaignAirlineNames.ToDictionary(n => n, n => (IAiStrategy?)null);

var campaignFleets = campaignAirlineNames.ToDictionary(n => n, n => FleetLedger.ApplyDelivery(
    AirlineFleet.Empty, campaignConditions[n].StartingAircraft, StartingConditionsResolver.StartingAircraftQuantity, FleetOwnershipMode.Owned));
var campaignAircraftOrderBoard = AircraftOrderBoard.Empty;
var campaignLeaseRequestBoard = LeaseRequestBoard.Empty;
// Aircraft-acquisition-limits follow-up: this demo loop has no other RNG
// of its own (it never wires NegotiationOrders into LandingRightsResolver
// either, the same pre-existing gap FullGameRunner.cs's own doc comment
// already names) - a dedicated, fixed-seed stream, matching
// negotiationRng's own simple convention above, rather than reusing that
// one across two unrelated concerns.
var campaignLeaseAllocationRng = new SimRng(seed: 11);

// Reference chokepoint check: does the era's best available aircraft still
// need Gander for JFK-LHR, or can it fly direct?
string ChokepointStatus(int year)
{
    var best = Timeline.AvailableAircraft(fleetCatalog, year).OrderByDescending(a => a.RangeKm).First();
    var direct = new Route(byId["jfk"], byId["lhr"], best, 1).IsWithinRange;
    return direct ? $"{best.Name} flies JFK-LHR direct" : $"{best.Name} still needs a Gander stop";
}

// Re-measured after M31 (docs/BASELINE_AI_ROADMAP.md) taught BaselineAi
// to hire an executive every quarter, unconditionally - the action cap
// climbs by 1/quarter instead of sitting flat at 3 forever, and this AI
// spends that extra room opening a genuinely new route almost every
// quarter (1 route at quarter 1, up to 22 by quarter 22) instead of
// endlessly re-leasing onto the same 2 saturated ones (M34's own
// frozen-network shape). The result is a dramatically bigger climb -
// peak cash $42,113,450.14 at quarter 18, more than double M34's own
// peak - but also a dramatically sharper crash once route count outruns
// what the network can actually support: bankruptcy at quarter 22
// (1960, -$445,156.38), only slightly later than M34's quarter 17, not
// the multi-quarter runway the cash peak alone would suggest. Frozen at
// 22 routes/-$445,156.38 from there; carried pax keeps climbing after
// bankruptcy the same way M34's writeup already explained (NetworkPlanner
// still recomputes against real, growing demand regardless of financial
// bankruptcy). This is the expected, *necessary but not sufficient*
// result the roadmap doc itself predicts: M31 gives BaselineAi room to
// grow a real network, but nothing yet stops it from over-expanding past
// what its fares/costs can sustain - that's M39's job, still ahead.
//
// M35 (retire/close) re-measured on top of this and produced byte-for-
// byte identical figures - confirmed why, not just observed it:
// instrumented this exact loop and found CloseRouteOrder/RetireAircraftOrder
// apply zero times across the entire 22-quarter run. Idle units are 0
// every single quarter (BaselineAi always immediately uses or leases
// exactly what it needs, never over-acquires - M35's retire target never
// exists here), and every multi-aircraft route's load factor stays
// pinned at a perfect 1.000 the whole run (this loop's demand keeps
// growing fast enough that capacity never outpaces it - M35's close
// target never exists here either). Both mechanics are real and
// correctly built (verified directly against the QA harness's 150 real
// games instead, where they DO fire and meaningfully help - see M35's
// own "Done" writeup) - this loop's specific demand-rich environment
// just never triggers either one, the same kind of loop-specific
// insensitivity M31's own hiring change already showed in reverse (there
// it was the QA harness that didn't move; here it's this loop).
//
// M36 (debt tools) re-measured on top of this too, and stays a third
// consecutive byte-for-byte match here - no diagnostic needed this time,
// the cash figures already printed above make the reason plain: cash
// never once drops below ~$15,760,767 across all 22 quarters, nowhere
// near the $2,000,000 negotiation bid that triggers a borrow, and this
// loop's debt balance stays at exactly $0 throughout (nothing here ever
// borrows), so PayDownDebtOrder's own trigger (debt must be nonzero)
// never has anything to act on either. Both mechanics fire for real
// against the QA harness's 150 games instead - see M36's own "Done"
// writeup. Three milestones in a row now where this loop's specific
// demand-rich, cash-rich environment simply never exercises the newest
// mechanic, while the harder QA harness games do.
//
// M37 (fares) breaks that three-milestone streak decisively - the
// biggest single trajectory shift measured all session. Instrumented
// this loop directly: every route here stays saturated (load factor
// >= 0.95) essentially permanently once opened - underperforming=0
// every single quarter through bankruptcy - so the raise mechanic fires
// on nearly every newly-opened route (one SetFareOrder/quarter, roughly
// matching one new route/quarter) while the cut mechanic never fires
// at all here. Pure, uncontested upside: peak cash more than doubled to
// $110,877,173.79 (quarter 20, versus M36's $42,113,450.14), and
// bankruptcy pushed from quarter 22 all the way to quarter 30 (1962,
// -$13,225,238.57) before the same fundamental problem (route count
// outrunning what salary/operating cost can sustain, M31's own finding)
// finally catches up. Frozen at 30 routes from there. The QA harness's
// 150 real games told a genuinely different, more mixed story instead -
// see M37's own "Done" writeup - because those harder conditions
// actually produce underperforming routes the cut mechanic acts on,
// unlike this loop.
//
// M38 (buy, not just lease) moves it further still, and does something
// no milestone before it has: the 4 airlines genuinely diverge from
// each other for the first time, despite making byte-identical
// decisions (BaselineAi is still deterministic) - AircraftOrder is a
// genuinely *contested* resource (M10's production-line position), so 4
// airlines simultaneously ordering the same best-range type can't all
// be satisfied the same quarter, and AircraftOrderResolver's own
// deterministic tie-break sends them down different delivery timelines
// from there. Peak cash (Atlas Air) climbs to $122,534,810.05 at
// quarter 44 (1965) - beyond M37's own peak - and bankruptcy is pushed
// to quarter 56 (1968, -$2,320,032.11), nearly double M37's quarter 30.
// Frozen at 24 routes from there. The QA harness's 150 real games moved
// broadly positive too this time (unlike M37's mixed result) - see
// M38's own "Done" writeup - eliminating leasing's recurring 4%/quarter
// fee compounds meaningfully over a long campaign regardless of how
// harsh the starting conditions are.
//
// M39 (strategy framework) replaces this loop's setup outright, not just
// its AI: the flat "$20M/smallest-1955-type for everyone" starting
// conditions above the M31-M38 trajectory was measured under are gone,
// replaced by real per-region StartingConditionsResolver figures, and
// BaselineAi.DecideOrders's own single global-best-pair heuristic is
// replaced by a per-airline IAiStrategy, region-aware from quarter 1. The
// specific dollar figures above (all measured pre-M39) no longer describe
// what this loop actually runs - see M39's own "Done" writeup in
// docs/BASELINE_AI_ROADMAP.md for the re-measured trajectory.
// M39 re-measured: with real per-region starting conditions and
// region-aware strategies (replacing the prior flat setup this loop's own
// M31-M38 history above was measured under), this loop no longer runs
// anywhere near its old quarter-17-to-56 bankruptcy range at all - every
// airline grows steadily with zero bankruptcies, and Continental Wings
// wins outright at quarter 10 (led 3+ regions for 4+ consecutive
// quarters). Checkpoints below show that whole short arc.
var checkpoints = new HashSet<int> { 1, 4, 7, 10 };

// Difficulty follow-up: this demo loop stays on GameDifficulty.Normal -
// see FullGameRunner.Run's own difficulty parameter for the real,
// setup-time-selectable mechanism every QA sweep can use.
var campaignRegionCounts = DifficultySettings.RegionCounts(GameDifficulty.Normal);
var campaignDominance = RegionalDominanceState.Empty;
string? campaignWinner = null;
int? campaignWinQuarter = null;
CompetitiveMarketResult? campaignLastMarket = null;
IReadOnlySet<string> campaignLastEligible = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

for (var q = 1; q <= campaignQuarters; q++)
{
    var year = Timeline.YearForQuarter(q);
    var availableFleet = Timeline.AvailableAircraft(fleetCatalog, year);
    var yearDemandModel = new GravityDemandModel(
        schedule: growthSchedule, year: year, enableHubDemandBonus: true,
        intraRegionMultipliers: GravityDemandModel.ShippedIntraRegionMultipliers,
        groundCompetitionDistanceKm: GravityDemandModel.ShippedGroundCompetitionDistanceKm); // M20: model resolves global (and any region/city-scoped) growth itself; M65: real AustralAsia tuning
    var appliedOrdersByAirline = new Dictionary<string, IReadOnlyList<QuarterOrder>>();
    var retiredUnitsByAirline = new Dictionary<string, IReadOnlyList<FleetUnit>>();

    IReadOnlyList<AircraftDelivery> campaignDeliveries;
    (campaignAircraftOrderBoard, campaignDeliveries) = AircraftOrderResolver.ResolveQuarter(campaignAircraftOrderBoard, q);
    foreach (var delivery in campaignDeliveries)
    {
        campaignFleets[delivery.Airline.Name] = FleetLedger.ApplyDelivery(campaignFleets[delivery.Airline.Name], delivery, FleetOwnershipMode.Owned);
    }

    IReadOnlyList<LeaseDelivery> campaignLeaseDeliveries;
    (campaignLeaseRequestBoard, campaignLeaseDeliveries) = LeaseAllocationResolver.ResolveQuarter(campaignLeaseRequestBoard, q, year, campaignLeaseAllocationRng);
    foreach (var leaseDelivery in campaignLeaseDeliveries)
    {
        campaignFleets[leaseDelivery.Airline.Name] = FleetLedger.ApplyDelivery(campaignFleets[leaseDelivery.Airline.Name], leaseDelivery.Type, leaseDelivery.Quantity, FleetOwnershipMode.Leased);
        campaignFinances[leaseDelivery.Airline.Name] = campaignFinances[leaseDelivery.Airline.Name] with { CashBalance = campaignFinances[leaseDelivery.Airline.Name].CashBalance - leaseDelivery.UpfrontCost };
    }

    foreach (var name in campaignAirlineNames)
    {
        // A bankrupt airline is out: routes and action cap freeze at
        // whatever they were, rather than an insolvent airline
        // nonsensically continuing to grow forever.
        if (campaignFinances[name].IsBankrupt)
        {
            appliedOrdersByAirline[name] = Array.Empty<QuarterOrder>();
            retiredUnitsByAirline[name] = Array.Empty<FleetUnit>();
            continue;
        }

        // M33's gate-slot gating is deliberately left off (availableSlotsByCity: null,
        // both calls) — this loop never wires NegotiationOrders into
        // LandingRightsResolver at all (a pre-existing, already-documented
        // gap; see FullGameRunner.cs's own doc comment), so it never builds
        // a real LandingRightsBoard to compute a genuine balance from.
        // Gating on an always-empty board would block every gateway route
        // outright rather than leaving this demo loop's existing behavior
        // alone. M28's cash-gating on NegotiationOrder bids is NOT in the
        // same boat, though — this loop already tracks real per-airline
        // cash, so BaselineAi's negotiation step becomes genuinely
        // cash-aware here too.
        // Geopolitical-aircraft-restrictions follow-up: the same query a
        // UI layer would use, computed once and threaded into both the
        // AI's own StrategyContext and QuarterEngine's own enforcement
        // gate below, so neither can drift from the other.
        var campaignPurchasableTypes = Timeline.AvailableForPurchase(availableFleet, campaignSeats[name].StartingCity, year, campaignTradeRestrictions);
        var campaignLeasableTypes = Timeline.AvailableForLease(availableFleet, campaignSeats[name].StartingCity, year, campaignTradeRestrictions);

        var campaignStrategyContext = new StrategyContext(
            campaignRoutes[name], campaignCaps[name], cities, yearDemandModel, availableFleet, campaignFleets[name], campaignHomeRegionByName[name],
            HomeCity: campaignSeats[name].StartingCity,
            AvailableSlotsByCity: null, AvailableCash: campaignFinances[name].CashBalance, CurrentDebtBalance: campaignFinances[name].DebtBalance,
            RegionsLedLastQuarter: DominanceTrackedRegions.All.Where(r => campaignDominance.StreakFor(name, r) >= 1).ToHashSet(),
            TargetRegionCountOverride: campaignRegionCounts.TargetRegionCount,
            PurchasableTypes: campaignPurchasableTypes,
            LeasableTypes: campaignLeasableTypes);
        var campaignStrategy = BaselineAi.SelectStrategy(campaignCurrentStrategyByName[name], q, campaignStrategyContext);
        campaignCurrentStrategyByName[name] = campaignStrategy;
        var orders = campaignStrategy.DecideOrders(campaignStrategyContext);
        var result = QuarterEngine.RunQuarter(q, campaignRoutes[name], campaignCaps[name], orders, cities, yearDemandModel, campaignFleets[name], availableCash: campaignFinances[name].CashBalance, currentDebtBalance: campaignFinances[name].DebtBalance, homeCity: campaignSeats[name].StartingCity, tradeRestrictions: campaignTradeRestrictions);
        campaignRoutes[name] = result.Routes;
        campaignCaps[name] = result.ActionCap;
        campaignFleets[name] = result.UpdatedFleet!;
        appliedOrdersByAirline[name] = result.AppliedOrders;
        retiredUnitsByAirline[name] = result.RetiredUnits!;

        var campaignAirlineSnapshot = new Airline(name, campaignRoutes[name]);
        foreach (var aircraftOrder in result.AppliedOrders.OfType<AircraftOrder>())
        {
            campaignAircraftOrderBoard = AircraftOrderResolver.Submit(campaignAircraftOrderBoard, campaignAirlineSnapshot, aircraftOrder, q);
        }

        foreach (var leaseOrder in result.AppliedOrders.OfType<LeaseAircraftOrder>())
        {
            campaignLeaseRequestBoard = LeaseAllocationResolver.Submit(campaignLeaseRequestBoard, campaignAirlineSnapshot, leaseOrder, q);
        }
    }

    var campaignLeague = campaignAirlineNames.Select(n => new Airline(n, campaignRoutes[n])).ToArray();
    var campaignMarket = CompetitiveMarket.Resolve(campaignLeague, cities, yearDemandModel);

    foreach (var seat in campaignLeague)
    {
        var (nextState, _) = FinancialResolver.Resolve(campaignFinances[seat.Name], seat, campaignMarket, appliedOrdersByAirline[seat.Name], fleet: campaignFleets[seat.Name], retiredUnits: retiredUnitsByAirline[seat.Name]);
        campaignFinances[seat.Name] = nextState;
    }

    campaignLastMarket = campaignMarket;

    campaignLastEligible = campaignAirlineNames.Where(n => !campaignFinances[n].IsBankrupt).ToHashSet(StringComparer.OrdinalIgnoreCase);
    campaignDominance = RegionalDominanceTracker.Advance(campaignDominance, campaignMarket, campaignLastEligible);

    var checkedCampaignWinner = WinConditionChecker.CheckWinner(campaignDominance, campaignLastEligible, campaignRegionCounts.RequiredRegionCount);
    if (checkedCampaignWinner is not null)
    {
        campaignWinner = checkedCampaignWinner;
        campaignWinQuarter = q;
    }

    // Every remaining airline going bankrupt the same quarter is a real
    // stalemate (nobody left to declare a winner from) - a deciding
    // quarter worth printing, same as a real win, not silently skipped.
    var isMutualElimination = campaignWinner is null && campaignLastEligible.Count == 0;

    if (!checkpoints.Contains(q) && campaignWinner is null && !isMutualElimination)
    {
        continue;
    }

    Console.WriteLine();
    Console.WriteLine($"Quarter {q} ({year}): {ChokepointStatus(year)}, fleet has {availableFleet.Count} aircraft type(s) available");
    foreach (var seat in campaignLeague)
    {
        var finance = campaignFinances[seat.Name];
        Console.WriteLine($"  {seat.Name,-18} {campaignRoutes[seat.Name].Count,3} routes, " +
            $"{campaignMarket.TotalCarried(seat),10:N0} pax/qtr, cash {finance.CashBalance,16}, bankrupt={finance.IsBankrupt}");
    }

    if (campaignWinner is not null)
    {
        Console.WriteLine($"  *** {campaignWinner} WINS: led {WinConditionChecker.RequiredRegionCount}+ regions for {WinConditionChecker.RequiredStreakQuarters}+ consecutive quarters, as of quarter {campaignWinQuarter} ***");
        break;
    }

    if (isMutualElimination)
    {
        Console.WriteLine("  *** Every remaining airline is bankrupt - true stalemate, nobody left to declare a winner from ***");
        break;
    }
}

if (campaignWinner is null)
{
    var campaignNetWorth = campaignAirlineNames.ToDictionary(n => n, n => campaignFinances[n].CashBalance - campaignFinances[n].DebtBalance);
    var campaignRanking = CampaignRanking.Rank(campaignDominance, campaignLastMarket!, campaignNetWorth);

    // The timeline ran its full course (or ended on mutual elimination,
    // in which case campaignLastEligible is empty and this correctly
    // stays null) with 2+ solvent airlines and no dominance/sole-survivor
    // winner - declare one from the same ranking, per the user's own
    // direction. See CampaignRanking.DeclareWinner.
    campaignWinner = CampaignRanking.DeclareWinner(campaignRanking, campaignLastEligible);

    if (campaignWinner is not null)
    {
        Console.WriteLine();
        Console.WriteLine($"  *** {campaignWinner} WINS by final ranking: timeline ended with no dominance winner, but led on regions/passengers/net worth among the {campaignLastEligible.Count} still-solvent airlines ***");
    }
    else
    {
        Console.WriteLine();
        Console.WriteLine("Timeline ended in a true stalemate (every remaining airline bankrupt) - final ranking:");
    }

    for (var i = 0; i < campaignRanking.Count; i++)
    {
        var r = campaignRanking[i];
        Console.WriteLine($"  {i + 1}. {r.AirlineName,-18} regions led {r.RegionsLed}, passengers {r.TotalPassengersCarried,10:N0}, net worth {r.NetWorth}");
    }
}

// ---------------------------------------------------------------------
// M15 (Game Setup & Initial Conditions roadmap): home region model.
// Confirms Africa is excluded as a home region and prints each home
// region's eligible (non-gateway) starting cities, straight from the same
// cities.json every other demo section already loads above.
// ---------------------------------------------------------------------
Console.WriteLine();
Console.WriteLine("=== M15: Home regions and eligible starting cities ===");
Console.WriteLine($"Home regions: {string.Join(", ", HomeRegions.All)} (Africa excluded: {!HomeRegions.IsHomeRegion(Region.Africa)})");

foreach (var region in HomeRegions.All)
{
    var eligible = HomeRegions.EligibleStartingCities(region, cities);
    var names = string.Join(", ", eligible.Select(c => $"{c.Name} ({c.Id})"));
    Console.WriteLine($"  {region,-13} {eligible.Count} eligible: {names}");
}

// ---------------------------------------------------------------------
// M16 (Game Setup & Initial Conditions roadmap): regional economics and
// region-appropriate starting aircraft, at the 1955 campaign start.
// ---------------------------------------------------------------------
Console.WriteLine();
Console.WriteLine("=== M16: Regional economics and region-appropriate aircraft (1955) ===");

foreach (var region in HomeRegions.All)
{
    var factor = RegionalEconomics.Factor(region, cities);
    var appropriate = RegionalFleet.AppropriateAircraft(region, fleetCatalog, Timeline.StartYear);
    var options = string.Join(", ", appropriate.Select(a => $"{a.Name} ({a.Manufacturer})"));
    Console.WriteLine($"  {region,-13} economic factor {factor,5:F3}  starting aircraft: {options}");
}

// ---------------------------------------------------------------------
// M17 (Game Setup & Initial Conditions roadmap): player setup flow.
// GameSetup.CreateSeats is a pure function over structured input; this is
// the "ask the player" sequence AeroNext.Sim supplies on its behalf. Three
// configurations at the scope boundary: 0 humans, 1 human + 3 AI, 4 humans.
// ---------------------------------------------------------------------
Console.WriteLine();
Console.WriteLine("=== M17: Player setup flow ===");

void PrintSeats(string label, IReadOnlyList<HumanPlayerChoice> humanChoices)
{
    var seats = GameSetup.CreateSeats(humanChoices, cities);
    Console.WriteLine($"  {label}:");
    foreach (var seat in seats)
    {
        var role = seat.IsHuman ? "human" : "AI  ";
        Console.WriteLine($"    [{role}] {seat.AirlineName,-20} {seat.HomeRegion,-13} starting in {seat.StartingCity.Name} ({seat.StartingCity.Id})");
    }
}

PrintSeats("0 human players", Array.Empty<HumanPlayerChoice>());
PrintSeats("1 human + 3 AI", new[] { new HumanPlayerChoice("Founders Airways", "yyz") });
PrintSeats("4 human players", new[]
{
    new HumanPlayerChoice("Atlas Air", "ord"),
    new HumanPlayerChoice("Meridian", "cdg"),
    new HumanPlayerChoice("Skyward", "sin"),
    new HumanPlayerChoice("Continental Wings", "eze"),
});

// Regions are no longer exclusive - two humans can now share a home
// region, as long as their starting cities are distinct.
PrintSeats("2 humans sharing NorthAmerica", new[]
{
    new HumanPlayerChoice("Atlas Air", "ord"),
    new HumanPlayerChoice("Meridian", "sfo"), // also NorthAmerica - now allowed
});

try
{
    GameSetup.CreateSeats(new[]
    {
        new HumanPlayerChoice("Atlas Air", "ord"),
        new HumanPlayerChoice("Meridian", "ord"), // same city - still rejected
    }, cities);
}
catch (ArgumentException ex)
{
    Console.WriteLine($"  Rejected (two humans claiming the same city): {ex.Message}");
}

// ---------------------------------------------------------------------
// M18 (Game Setup & Initial Conditions roadmap): deriving starting cash,
// aircraft, and a guaranteed turn-1 route from each seat's starting city.
// Confirms the guaranteed route is actually accepted by the real
// QuarterEngine on quarter 1 — not just computed, but proven to work.
// ---------------------------------------------------------------------
Console.WriteLine();
Console.WriteLine("=== M18: Starting conditions and guaranteed turn-1 route ===");

var setupSeats = GameSetup.CreateSeats(new[]
{
    new HumanPlayerChoice("Atlas Air", "ord"),
    new HumanPlayerChoice("Meridian", "cdg"),
    new HumanPlayerChoice("Skyward", "sin"),
    new HumanPlayerChoice("Continental Wings", "eze"),
}, cities);

foreach (var seat in setupSeats)
{
    var conditions = StartingConditionsResolver.Resolve(seat, cities, fleetCatalog, growthSchedule);
    Console.WriteLine($"  {seat.AirlineName,-18} cash {conditions.StartingCash,14}  " +
        $"{conditions.AircraftQuantity}x {conditions.StartingAircraft.Name,-20} -> {conditions.GuaranteedRouteDestination.Name}");

    var order = new RouteOrder(seat.StartingCity, conditions.GuaranteedRouteDestination, conditions.StartingAircraft);
    var quarter1 = QuarterEngine.RunQuarter(1, Array.Empty<Route>(), QuarterEngine.BaseActionCap, new[] { order }, cities, demandModel);
    Console.WriteLine($"    Quarter 1 route order: {(quarter1.RejectedOrders.Count == 0 ? "accepted" : "REJECTED")}");
}

// ---------------------------------------------------------------------
// M19 (Game Setup & Initial Conditions roadmap): the full chain, wired
// into one entry point, run through the real QuarterEngine ->
// CompetitiveMarket -> FinancialResolver pipeline (not a mock path) for
// the three configurations at the scope boundary. This is the roadmap's
// last milestone.
// ---------------------------------------------------------------------
Console.WriteLine();
Console.WriteLine("=== M19: Full setup-through-quarter-1 integration ===");

void RunSetupThroughQuarter1(string label, IReadOnlyList<HumanPlayerChoice> humanChoices)
{
    Console.WriteLine($"  --- {label} ---");

    var seats = GameSetup.CreateSeats(humanChoices, cities);

    var conditionsBySeat = seats.ToDictionary(s => s, s => StartingConditionsResolver.Resolve(s, cities, fleetCatalog, growthSchedule));
    var routesBySeat = new Dictionary<AirlineSeat, IReadOnlyList<Route>>();
    var appliedOrdersBySeat = new Dictionary<AirlineSeat, IReadOnlyList<QuarterOrder>>();

    foreach (var seat in seats)
    {
        var conditions = conditionsBySeat[seat];
        var order = new RouteOrder(seat.StartingCity, conditions.GuaranteedRouteDestination, conditions.StartingAircraft);
        var quarter1 = QuarterEngine.RunQuarter(1, Array.Empty<Route>(), QuarterEngine.BaseActionCap, new[] { order }, cities, demandModel);

        routesBySeat[seat] = quarter1.Routes;
        appliedOrdersBySeat[seat] = quarter1.AppliedOrders;

        if (quarter1.RejectedOrders.Count > 0)
        {
            throw new InvalidOperationException($"{seat.AirlineName}'s guaranteed quarter-1 route was rejected — a real bug, not expected.");
        }
    }

    var league = seats.Select(s => new Airline(s.AirlineName, routesBySeat[s])).ToArray();
    var market = CompetitiveMarket.Resolve(league, cities, demandModel);

    foreach (var seat in seats)
    {
        var conditions = conditionsBySeat[seat];
        var airline = league.Single(a => a.Name == seat.AirlineName);
        var financeStart = FinancialState.Start(conditions.StartingCash, Money.FromDollars(10_000_000), interestRatePerQuarter: 0.02);
        var (_, statement) = FinancialResolver.Resolve(financeStart, airline, market, appliedOrdersBySeat[seat]);

        var role = seat.IsHuman ? "human" : "AI  ";
        Console.WriteLine($"    [{role}] {seat.AirlineName,-18} {seat.HomeRegion,-13} " +
            $"carried {market.TotalCarried(airline),8:N0} pax  net {statement.NetIncome,14}  cash {statement.CashBalanceAfter,14}  bankrupt={statement.IsBankrupt}");
    }
}

RunSetupThroughQuarter1("0 human players", Array.Empty<HumanPlayerChoice>());
RunSetupThroughQuarter1("1 human + 3 AI", new[] { new HumanPlayerChoice("Founders Airways", "yyz") });
RunSetupThroughQuarter1("4 human players", new[]
{
    new HumanPlayerChoice("Atlas Air", "ord"),
    new HumanPlayerChoice("Meridian", "cdg"),
    new HumanPlayerChoice("Skyward", "sin"),
    new HumanPlayerChoice("Continental Wings", "eze"),
});
