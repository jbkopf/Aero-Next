using AeroNext.Core.Airlines;
using AeroNext.Core.Finance;
using AeroNext.Core.Fleet;
using AeroNext.Core.Turns;
using AeroNext.Core.World;

namespace AeroNext.Core.Ai;

/// <summary>
/// A simple heuristic opponent: greedily enters the single highest-demand
/// city pair it can actually reach and doesn't already serve, if it can
/// afford a market action; bids on the single highest-demand gateway it
/// doesn't yet touch, if it can also afford a contested action *and* the
/// bid itself (M28 — negotiations cost real cash at submission now, not
/// just an action) (per CLAUDE.md — negotiations are "blind" even for a
/// skilled human, so not checking the target's remaining gate capacity
/// before bidding isn't a missing smarts, it's true to the mechanic); and
/// tries to grow its own best-performing existing route by assigning it
/// another aircraft (M32 — no longer free; competes for the same action
/// budget as everything else); hires an executive every quarter,
/// unconditionally (M31, docs/BASELINE_AI_ROADMAP.md — HireExecutiveOrder
/// is free and never competes with anything else in the action budget,
/// and this AI already spends its full budget every quarter, so nothing
/// else in that roadmap has room to land without this landing first);
/// shrinks (never fully closes) any served route whose load factor has
/// fallen below a floor, reactively correcting an over-built route
/// without risking the same pair being reopened next quarter (M35); and
/// retires any idle unit that's genuinely stranded — no served route
/// uses its type and no unserved pair worth reaching needs it either
/// (M35). Both M35 checks are free, so every qualifying route/unit is
/// acted on the same quarter, not just the single best one. Borrows just
/// enough to cover a negotiation bid shortfall when a real target exists
/// and the debt ceiling has room — but never attempts the negotiation
/// the same quarter, since a same-quarter RefinanceOrder's cash isn't
/// visible to QuarterEngine's own affordability gate; it borrows now so
/// next quarter's cash actually covers the bid, then negotiates then.
/// Separately pays down debt whenever cash comfortably exceeds a buffer
/// and debt is nonzero — never both the same quarter, since the two
/// thresholds are spaced far enough apart that cash can't be both short
/// of the bid and above the buffer at once (M36). Raises the fare on
/// any served route that's essentially sold out, and separately cuts
/// the fare on any served route below the same load-factor floor
/// CloseRouteOrder already uses (M37). Worth stating plainly: only the
/// raise is currently revenue-positive — fare doesn't yet affect demand
/// size or competitive share (M34's own explicit scope), so a saturated
/// route's capacity-capped carried volume pays more for free, while an
/// underperforming route's cut wins no additional passengers and is a
/// straight per-passenger loss under today's mechanics. Both are built
/// anyway, to exercise the full mechanic and measure the real effect —
/// see docs/BASELINE_AI_ROADMAP.md's M37 for the measured numbers.
/// Orders an aircraft outright, instead of leasing, whenever cash
/// comfortably covers the purchase price — cheaper long-run (no
/// recurring lease fee) — but only when nothing idle is available to
/// use immediately: AircraftOrder is a contested resource (delivery-line
/// position, M10) that doesn't deliver this quarter the way leasing
/// does, so choosing to order means the route it would have funded
/// doesn't open/grow this quarter either, trading an immediate route for
/// a cheaper one later (M38). This closes out phase 1 (mechanical
/// completeness) of docs/BASELINE_AI_ROADMAP.md — every order type this
/// AI can meaningfully use, it now does.
///
/// Not trying to play well — CLAUDE.md's game is always exactly 4 airlines,
/// so this exists because the sim can never run a representative quarter
/// without something producing orders for whichever seats aren't human, not
/// because this is meant to be a strong opponent. Deliberately narrow:
/// no hub-awareness (picks the single best pair/gateway in the whole world,
/// not the best one connected to its existing network), no executive
/// hiring or alliance courting of its own — M9's alliance mechanic has no
/// consequence that would change this AI's route decisions, so extending it
/// there would be cosmetic. Range-awareness (M11), fleet availability
/// (M13), and real fleet *ownership* (QuarterEngine's fleet-aware
/// RouteOrder check) are different: an out-of-range, not-yet-available, or
/// unowned-aircraft order is rejected today, for real — so this AI must
/// never propose one, the same "never attempt what will predictably fail"
/// principle already settled for cash affordability. Purely a function of
/// current state — no randomness, so it's deterministic by construction
/// without needing a seeded RNG.
/// </summary>
public static class BaselineAi
{
    internal static readonly Money NegotiationBidAmount = Money.FromDollars(2_000_000);

    /// <summary>
    /// M35 (proposed, tunable): below this, a served route reads as
    /// over-built relative to its own demand, not under-served — the
    /// reactive correction is to shrink it by one aircraft
    /// (CloseRouteOrder), never to close it outright (see the class doc
    /// comment's own reasoning against reopening the same pair next
    /// quarter).
    /// </summary>
    internal const double CloseRouteLoadFactorFloor = 0.5;

    /// <summary>
    /// M36 (proposed, tunable): pay down debt only once cash comfortably
    /// clears this — five times NegotiationBidAmount, deliberately, so
    /// cash can never be simultaneously short of the bid (the borrow
    /// trigger below) and above this buffer (the paydown trigger) in the
    /// same quarter. No explicit mutual-exclusion check needed; the
    /// thresholds themselves guarantee it.
    /// </summary>
    internal static readonly Money PayDownCashBuffer = Money.FromDollars(10_000_000);

    /// <summary>M37 (proposed, tunable): at or above this, a served route reads as essentially sold out — the reactive correction is to raise its fare toward FareRaiseFraction above the computed default.</summary>
    internal const double SaturatedLoadFactorCeiling = 0.95;

    /// <summary>M37 (proposed, tunable): how far above the computed default a saturated route's fare gets raised.</summary>
    internal const double FareRaiseFraction = 0.15;

    /// <summary>M37 (proposed, tunable): how far below the computed default an underperforming route's fare gets cut — see the class doc comment for why this is currently revenue-negative under today's demand model, built anyway to exercise the full mechanic.</summary>
    internal const double FareCutFraction = 0.20;

    /// <summary>
    /// M38 (proposed, tunable): order instead of lease only when cash
    /// clears this multiple of the aircraft's own purchase price —
    /// deliberately more conservative than QuarterEngine's own
    /// affordability gate for AircraftOrder (1x cost), so this AI never
    /// gets its own order rejected and keeps comfortable margin for the
    /// same quarter's salary/operating cost.
    /// </summary>
    private const int AircraftOrderCashComfortMultiplier = 3;

    internal static Money ComfortableOrderCost(AircraftType type) => type.Cost * AircraftOrderCashComfortMultiplier;

    /// <param name="eraAvailableTypes">
    /// The full aircraft catalog available to lease this era (e.g.
    /// Timeline.AvailableAircraft) — not this airline's own fleet. Only
    /// consulted as a fallback, when nothing already in ownFleet is idle
    /// and able to reach anywhere worth serving.
    /// </param>
    /// <param name="ownFleet">
    /// This airline's actual holdings. A new route always prefers an idle
    /// unit already owned or leased — free, no new action needed for the
    /// aircraft itself — over acquiring something new. Only when nothing
    /// idle can reach a positive-demand unserved pair does this AI
    /// acquire one — leasing, and in the same order batch proposing a
    /// route using it (relying on QuarterEngine's inline lease resolution
    /// to make that same-quarter use possible), unless cash comfortably
    /// covers buying outright instead (M38 — see the class doc comment),
    /// in which case it orders and proposes no route this quarter.
    /// </param>
    /// <param name="availableSlotsByCity">
    /// M33: null (the default) never blocks a candidate on gate scarcity,
    /// matching QuarterEngine's own permissive default. Passing this
    /// airline's real per-city balance (see QuarterEngine.RunQuarter's own
    /// parameter of the same name) makes this AI skip — not search for an
    /// alternative to — a growth or new-pair candidate that would touch a
    /// gateway city it has no slot left at, the same "never attempt what
    /// will predictably fail" principle already applied to cash and fleet.
    /// </param>
    /// <param name="availableCash">
    /// M28: null (the default) never blocks the negotiation step, matching
    /// QuarterEngine's own permissive default. Passing quarter-start cash
    /// on hand skips proposing a NegotiationOrder this AI can't afford —
    /// negotiations now cost real cash at submission (M28), and this is
    /// the only cash-consuming action this AI ever proposes (growth/new-pair
    /// never cost cash upfront; leasing is a recurring cost, not a
    /// submission-time one), so no running "spent so far" tracker is
    /// needed here the way fleet/slot reservations already need one. M36:
    /// also the basis for both new debt decisions below.
    /// </param>
    /// <param name="currentDebtBalance">
    /// M36: null (the default) means this AI never borrows or pays down
    /// debt — no debt tracking means no ceiling to check a borrow against,
    /// so staying fully passive here is the conservative choice, the same
    /// asymmetry PayDownDebtOrder's own QuarterEngine gating already
    /// established. Passing quarter-start debt (before this quarter's own
    /// automatic interest/principal) turns on both: borrowing just enough
    /// to cover a negotiation bid shortfall when real ceiling headroom
    /// exists, and paying down whenever cash clears PayDownCashBuffer.
    /// </param>
    public static IReadOnlyList<QuarterOrder> DecideOrders(
        IReadOnlyList<Route> currentRoutes,
        int actionCap,
        IReadOnlyList<City> allCities,
        GravityDemandModel demandModel,
        IReadOnlyList<AircraftType> eraAvailableTypes,
        AirlineFleet ownFleet,
        IReadOnlyDictionary<string, int>? availableSlotsByCity = null,
        Money? availableCash = null,
        Money? currentDebtBalance = null)
    {
        var orders = new List<QuarterOrder>();
        var spent = 0;

        // Reserved this planning pass but not yet actually assigned in
        // ownFleet (QuarterEngine hasn't run yet) - tracked locally so the
        // new-pair search below doesn't also propose the same idle unit
        // this step already claimed, which QuarterEngine would then
        // reject as a wasted action.
        var reservedUnitIds = new HashSet<int>();

        // Same idea for gate slots (M33): a city touched by both the
        // growth and new-pair steps in one pass must not be treated as
        // having two separate slots available when it only has one.
        var slotsReservedByCity = new Dictionary<string, int>();

        var bestExisting = FindBestPerformingRoute(currentRoutes, demandModel);
        if (bestExisting is not null && spent + 1 <= actionCap
            && HasSlotAtEveryGateway(bestExisting.Origin, bestExisting.Destination, availableSlotsByCity, slotsReservedByCity))
        {
            var idleUnit = ownFleet.Idle
                .Where(u => u.Type.Id == bestExisting.Aircraft.Id)
                .OrderBy(u => u.Id)
                .FirstOrDefault();

            if (idleUnit is not null)
            {
                orders.Add(new RouteOrder(bestExisting.Origin, bestExisting.Destination, bestExisting.Aircraft));
                reservedUnitIds.Add(idleUnit.Id);
                ReserveSlots(bestExisting.Origin, bestExisting.Destination, slotsReservedByCity);
                spent += 1;
            }
            else if (availableCash is not null && FinancialState.CanAfford(availableCash.Value, ComfortableOrderCost(bestExisting.Aircraft)))
            {
                // M38: nothing idle of this route's own type, but cash
                // comfortably covers buying one outright - order instead
                // of lease (cheaper long-run, no recurring fee). No
                // RouteOrder this quarter: AircraftOrder doesn't deliver
                // the same quarter the way LeaseAircraftOrder does (M10's
                // contested delivery-line resource) - the growth simply
                // happens later, once the order delivers and the unit
                // shows up idle.
                orders.Add(new AircraftOrder(bestExisting.Aircraft, Quantity: 1));
                spent += 1;
            }
            else if (spent + 2 <= actionCap)
            {
                // Nothing idle of this route's own type, and cash isn't
                // comfortable enough to order instead - lease one more
                // and add it in the same batch, mirroring the new-pair
                // fallback below. No "best available type" search needed
                // here: growing an existing route requires the exact type
                // already flying it - QuarterEngine rejects a mismatch.
                orders.Add(new LeaseAircraftOrder(bestExisting.Aircraft, Quantity: 1));
                orders.Add(new RouteOrder(bestExisting.Origin, bestExisting.Destination, bestExisting.Aircraft));
                ReserveSlots(bestExisting.Origin, bestExisting.Destination, slotsReservedByCity);
                spent += 2;
            }
        }

        if (spent + 1 <= actionCap)
        {
            var idleBest = FindBestUnservedPairUsingIdleFleet(currentRoutes, allCities, demandModel, ownFleet, reservedUnitIds);

            if (idleBest is not null && HasSlotAtEveryGateway(idleBest.Value.Pair.Origin, idleBest.Value.Pair.Destination, availableSlotsByCity, slotsReservedByCity))
            {
                var (pair, aircraft) = idleBest.Value;
                orders.Add(new RouteOrder(pair.Origin, pair.Destination, aircraft));
                ReserveSlots(pair.Origin, pair.Destination, slotsReservedByCity);
                spent += 1;
            }
            else if (idleBest is null && spent + 1 <= actionCap)
            {
                // Nothing idle can reach anywhere worth serving - either
                // lease the best available type and open a route with it
                // in the same batch, or (M38) order it instead when cash
                // comfortably covers buying outright. Ordering never
                // proposes a route this quarter (AircraftOrder doesn't
                // deliver the same quarter - M10's contested delivery-line
                // resource), so it's cheaper but not immediate - see the
                // class doc comment.
                var bestAircraft = eraAvailableTypes
                    .OrderByDescending(a => a.RangeKm)
                    .ThenBy(a => a.Id, StringComparer.Ordinal)
                    .FirstOrDefault();

                var bestUnserved = bestAircraft is null ? null : FindBestUnservedPair(currentRoutes, allCities, demandModel, bestAircraft);

                if (bestAircraft is not null && bestUnserved is not null
                    && availableCash is not null && FinancialState.CanAfford(availableCash.Value, ComfortableOrderCost(bestAircraft)))
                {
                    orders.Add(new AircraftOrder(bestAircraft, Quantity: 1));
                    spent += 1;
                }
                else if (bestAircraft is not null && bestUnserved is not null && spent + 2 <= actionCap
                    && HasSlotAtEveryGateway(bestUnserved.Origin, bestUnserved.Destination, availableSlotsByCity, slotsReservedByCity))
                {
                    orders.Add(new LeaseAircraftOrder(bestAircraft, Quantity: 1));
                    orders.Add(new RouteOrder(bestUnserved.Origin, bestUnserved.Destination, bestAircraft));
                    ReserveSlots(bestUnserved.Origin, bestUnserved.Destination, slotsReservedByCity);
                    spent += 2;
                }
            }
        }

        if (spent + 1 <= actionCap)
        {
            var bestGateway = FindBestUnservedGateway(currentRoutes, allCities);
            var canAffordNow = availableCash is null || FinancialState.CanAfford(availableCash.Value, NegotiationBidAmount);

            if (bestGateway is not null && canAffordNow)
            {
                orders.Add(new NegotiationOrder(bestGateway, NegotiationBidAmount));
                spent += 1;
            }
            else if (bestGateway is not null && availableCash is not null && currentDebtBalance is not null)
            {
                // M36: cash falls short of the bid this quarter. Borrowing
                // and negotiating in the *same* quarter would still get
                // the NegotiationOrder rejected — QuarterEngine's own cash
                // gate checks only the quarter-start availableCash passed
                // in below; a same-quarter RefinanceOrder does no
                // QuarterEngine-side processing at all and never raises
                // it (see QuarterEngine's own doc comment on this exact
                // point, established back in M29). So: borrow the gap now
                // — free, no action spent — so FinancialResolver applies
                // it to CashBalance before next quarter, and next
                // quarter's own availableCash will actually cover the
                // bid. Never attempt the negotiation itself until it can
                // really afford it. Gated by real debt-ceiling headroom
                // (CanAfford reused a second way: headroom is "available,"
                // the borrow amount is "cost"), not just the cash gap.
                // shortfall is always positive here: this branch only
                // runs when canAffordNow was already false, i.e.
                // NegotiationBidAmount > availableCash.Value.
                var shortfall = NegotiationBidAmount - availableCash.Value;
                var ceilingHeadroom = Money.FromDollars(FinancialResolver.DebtCeilingDollars) - currentDebtBalance.Value;

                if (FinancialState.CanAfford(ceilingHeadroom, shortfall))
                {
                    orders.Add(new RefinanceOrder(shortfall));
                }
            }
        }

        // Computed once, shared by the close/raise/cut checks below -
        // each reads the same per-route load factor, just against a
        // different threshold.
        var routeLoads = RouteEvaluator.EvaluateNetwork(currentRoutes, demandModel);

        // M35: shrink (never fully close, see class doc comment) every
        // served route whose load factor has fallen below the floor -
        // free, so every qualifying route gets one shrink this quarter,
        // not just the single worst one.
        foreach (var overBuilt in routeLoads
            .Where(load => load.Route.AircraftCount > 1 && load.LoadFactor < CloseRouteLoadFactorFloor)
            .Select(load => load.Route))
        {
            orders.Add(new CloseRouteOrder(overBuilt.Origin, overBuilt.Destination));
        }

        // M37: raise the fare on every served route that's essentially
        // sold out - free, so every qualifying route gets one this
        // quarter. Flat against the computed default, not the current
        // fare (non-compounding), and skipped once already at or above
        // the target so a route that's still saturated after the raise
        // doesn't get a pointless repeat order.
        foreach (var saturated in routeLoads
            .Where(load => load.LoadFactor >= SaturatedLoadFactorCeiling)
            .Select(load => load.Route))
        {
            var target = RaisedFare(saturated);

            if (saturated.FarePerPassenger < target)
            {
                orders.Add(new SetFareOrder(saturated.Origin, saturated.Destination, target));
            }
        }

        // M37: cut the fare on every served route below the same floor
        // CloseRouteOrder already uses - mirroring M35's own threshold
        // logic, not inventing a separate one. See the class doc comment:
        // this direction is currently revenue-negative under today's
        // demand model, built anyway to exercise the full mechanic.
        // Same flat-against-default, non-compounding, skip-if-already-there
        // shape as the raise above.
        foreach (var underperforming in routeLoads
            .Where(load => load.LoadFactor < CloseRouteLoadFactorFloor)
            .Select(load => load.Route))
        {
            var target = CutFare(underperforming);

            if (underperforming.FarePerPassenger > target)
            {
                orders.Add(new SetFareOrder(underperforming.Origin, underperforming.Destination, target));
            }
        }

        // M35: retire every genuinely stranded idle unit - nothing to
        // grow (no served route uses its type) and nothing new to open
        // (no unserved, in-range pair has positive demand for its type
        // either). Conservative on purpose: an idle unit whose type IS
        // still useful somewhere is kept, even if this quarter's single
        // greedy growth/new-pair pick above happened to use a different
        // unit - it may be exactly what a future quarter wants. Free, so
        // every stranded unit retires the same quarter, not just one.
        foreach (var stranded in ownFleet.Idle
            .Where(u => !reservedUnitIds.Contains(u.Id))
            .Where(u => currentRoutes.All(r => r.Aircraft.Id != u.Type.Id))
            .Where(u => (FindBestUnservedPair(currentRoutes, allCities, demandModel, u.Type)?.PassengersPerQuarter ?? 0) <= 0)
            .OrderBy(u => u.Id))
        {
            orders.Add(new RetireAircraftOrder(stranded.Id));
        }

        // M36: pay down debt whenever cash comfortably clears the buffer
        // and debt is nonzero - free, so it fires whenever the condition
        // holds, not gated by budget. Can never coincide with borrowing
        // above in the same quarter - see PayDownCashBuffer's own doc
        // comment for why the two thresholds guarantee that.
        if (availableCash is not null && currentDebtBalance is not null
            && currentDebtBalance.Value > Money.Zero && availableCash.Value > PayDownCashBuffer)
        {
            var affordablePaydown = availableCash.Value - PayDownCashBuffer;
            var payDownAmount = affordablePaydown < currentDebtBalance.Value ? affordablePaydown : currentDebtBalance.Value;

            if (payDownAmount > Money.Zero)
            {
                orders.Add(new PayDownDebtOrder(payDownAmount));
            }
        }

        // M31: unconditional, every quarter, regardless of cities/routes/
        // fleet/cash/action cap — HireExecutiveOrder is free (0 actions)
        // and never competes with anything above, so there's no candidate
        // to weigh it against. Given the mechanic's own current shape
        // (uncapped, costless), always hiring is the correct behavior,
        // not a heuristic bug — see docs/BASELINE_AI_ROADMAP.md's M31.
        orders.Add(new HireExecutiveOrder());

        return orders;
    }

    /// <summary>
    /// M39 (docs/BASELINE_AI_ROADMAP.md): the strategy roster. Order
    /// doesn't matter — Strategies() is only ever consulted through
    /// SelectStrategy, which sorts by Suitability with a deterministic
    /// Name tie-break, never by roster position. M58: NetworkOptimizerStrategy
    /// added, the first strategy to cover GameEra.LateGame at all.
    /// BalancedGrowthStrategy follow-up: the second MidGame/LateGame
    /// strategy - see its own class doc comment and
    /// AiToolkit.NetworkTroubleRatio's for why the two are a guaranteed
    /// non-overlapping Suitability partition, not independently-tuned
    /// scores hoping to compare correctly (the exact bug M58 resume round
    /// 3 already found and fixed once for the prior pairing).
    /// </summary>
    public static IReadOnlyList<IAiStrategy> Strategies { get; } = new IAiStrategy[]
    {
        new DomesticProfitOptimizerStrategy(),
        new RapidExpansionStrategy(),
        new NetworkOptimizerStrategy(),
        new BalancedGrowthStrategy(),
    };

    /// <summary>
    /// M39: unifies initial assignment and later switching into one
    /// path. Re-evaluates only when nothing is assigned yet (current is
    /// null — quarter 1, or a seat that's never run a strategy before)
    /// or quarterNumber starts a new in-game year (4 quarters); otherwise
    /// returns current unchanged. Filters the roster to strategies whose
    /// ApplicableEras contains the current GameEra, picks the highest
    /// Suitability among eligible ones (deterministic Name tie-break).
    /// If nothing is eligible for this era (a real, deliberate gap with
    /// only two strategies, both Starting-only or Starting/MidGame —
    /// nothing yet exists for a LateGame-only slot), keeps current rather
    /// than throwing — the era tag exists so a future milestone can add
    /// era-native strategies into this same roster without touching this
    /// method.
    /// </summary>
    public static IAiStrategy SelectStrategy(IAiStrategy? current, int quarterNumber, StrategyContext context)
    {
        var isYearBoundary = (quarterNumber - 1) % 4 == 0;

        if (current is not null && !isYearBoundary)
        {
            return current;
        }

        var era = GameEras.ForQuarter(quarterNumber);
        var eligible = Strategies
            .Where(s => s.ApplicableEras.Contains(era))
            .OrderByDescending(s => s.Suitability(context))
            .ThenBy(s => s.Name, StringComparer.Ordinal)
            .ToList();

        if (eligible.Count == 0)
        {
            return current ?? throw new InvalidOperationException(
                $"No strategy is eligible for {era} and no current strategy exists to fall back to — the roster must cover every era at least once for quarter 1.");
        }

        return eligible[0];
    }

    /// <summary>
    /// The best unserved pair reachable using any type this airline
    /// currently has an idle unit of — searched per distinct idle type,
    /// then the single best result kept, so a smaller idle jet doesn't get
    /// passed over just because a bigger one happens to sort first.
    /// reservedUnitIds excludes units another step in this same planning
    /// pass already claimed (see DecideOrders).
    /// </summary>
    internal static (PairDemand Pair, AircraftType Aircraft)? FindBestUnservedPairUsingIdleFleet(
        IReadOnlyList<Route> currentRoutes, IReadOnlyList<City> allCities, GravityDemandModel demandModel, AirlineFleet ownFleet, IReadOnlySet<int> reservedUnitIds)
    {
        var candidates = ownFleet.Idle
            .Where(u => !reservedUnitIds.Contains(u.Id))
            .Select(u => u.Type)
            .Distinct()
            .Select(type => (Pair: FindBestUnservedPair(currentRoutes, allCities, demandModel, type), Aircraft: type))
            .Where(c => c.Pair is not null)
            .ToList();

        if (candidates.Count == 0)
        {
            return null;
        }

        var best = candidates
            .OrderByDescending(c => c.Pair!.PassengersPerQuarter)
            .ThenBy(c => c.Pair!.Origin.Id, StringComparer.Ordinal)
            .ThenBy(c => c.Pair!.Destination.Id, StringComparer.Ordinal)
            .ThenBy(c => c.Aircraft.Id, StringComparer.Ordinal)
            .First();

        return (best.Pair!, best.Aircraft);
    }

    internal static PairDemand? FindBestUnservedPair(
        IReadOnlyList<Route> currentRoutes, IReadOnlyList<City> allCities, GravityDemandModel demandModel, AircraftType aircraft)
    {
        var served = currentRoutes
            .SelectMany(r => new[] { (r.Origin.Id, r.Destination.Id), (r.Destination.Id, r.Origin.Id) })
            .ToHashSet();

        return demandModel.AllPairs(allCities)
            .Where(p => !served.Contains((p.Origin.Id, p.Destination.Id)))
            .Where(p => p.DistanceKm <= aircraft.RangeKm)
            .OrderByDescending(p => p.PassengersPerQuarter)
            .ThenBy(p => p.Origin.Id, StringComparer.Ordinal)
            .ThenBy(p => p.Destination.Id, StringComparer.Ordinal)
            .FirstOrDefault();
    }

    /// <param name="excludePairKeys">
    /// M55: normalized pair keys (QuarterEngine.NormalizedPairKey) to
    /// skip entirely, regardless of load factor - null (the default)
    /// preserves every pre-M55 call site's exact behavior, including
    /// this method's own frozen legacy caller, BaselineAi.DecideOrders.
    /// RapidExpansionStrategy passes AiToolkit.RecentlyShrunkPairKeys
    /// here, the real fix for the oscillation M43 traced: shrinking a
    /// route mechanically raises its own load factor, so without this a
    /// route shrunk one quarter could rank as "best performing" again the
    /// very next and get grown right back.
    /// </param>
    internal static Route? FindBestPerformingRoute(IReadOnlyList<Route> currentRoutes, GravityDemandModel demandModel, IReadOnlySet<(string, string)>? excludePairKeys = null)
    {
        return RouteEvaluator.EvaluateNetwork(currentRoutes, demandModel)
            .Where(l => excludePairKeys is null || !excludePairKeys.Contains(QuarterEngine.NormalizedPairKey(l.Route.Origin, l.Route.Destination)))
            .OrderByDescending(l => l.LoadFactor)
            .ThenBy(l => l.Route.Origin.Id, StringComparer.Ordinal)
            .ThenBy(l => l.Route.Destination.Id, StringComparer.Ordinal)
            .Select(l => l.Route)
            .FirstOrDefault();
    }

    /// <summary>M37: FareCalculator's own default, grossed up by FareRaiseFraction — the standard double-then-round-once cents pattern FareCalculator itself already uses.</summary>
    internal static Money RaisedFare(Route route)
    {
        var cents = (long)Math.Round(FareCalculator.SuggestedFarePerPassenger(route).Cents * (1 + FareRaiseFraction), MidpointRounding.AwayFromZero);
        return new Money(cents);
    }

    /// <summary>M37: FareCalculator's own default, cut by FareCutFraction — same rounding pattern as RaisedFare.</summary>
    internal static Money CutFare(Route route)
    {
        var cents = (long)Math.Round(FareCalculator.SuggestedFarePerPassenger(route).Cents * (1 - FareCutFraction), MidpointRounding.AwayFromZero);
        return new Money(cents);
    }

    /// <summary>M33: true when availableSlotsByCity is null (permissive), or this airline has a slot left at every gateway origin/destination touches.</summary>
    internal static bool HasSlotAtEveryGateway(City origin, City destination, IReadOnlyDictionary<string, int>? availableSlotsByCity, Dictionary<string, int> slotsReservedByCity)
    {
        if (availableSlotsByCity is null)
        {
            return true;
        }

        return new[] { origin, destination }
            .Where(c => c.GateCapacity is not null)
            .All(c => availableSlotsByCity.GetValueOrDefault(c.Id) - slotsReservedByCity.GetValueOrDefault(c.Id) > 0);
    }

    internal static void ReserveSlots(City origin, City destination, Dictionary<string, int> slotsReservedByCity)
    {
        foreach (var city in new[] { origin, destination }.Where(c => c.GateCapacity is not null))
        {
            slotsReservedByCity[city.Id] = slotsReservedByCity.GetValueOrDefault(city.Id) + 1;
        }
    }

    internal static City? FindBestUnservedGateway(IReadOnlyList<Route> currentRoutes, IReadOnlyList<City> allCities)
    {
        var touchedCityIds = currentRoutes
            .SelectMany(r => new[] { r.Origin.Id, r.Destination.Id })
            .ToHashSet();

        return allCities
            .Where(c => c.IsGateway && !touchedCityIds.Contains(c.Id))
            .OrderByDescending(c => c.DemandWeight)
            .ThenBy(c => c.Id, StringComparer.Ordinal)
            .FirstOrDefault();
    }
}
