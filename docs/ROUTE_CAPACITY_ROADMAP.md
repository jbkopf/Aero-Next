# Route Capacity Roadmap

Continues the shared milestone numbering from `FINANCIAL_MODEL_ROADMAP.md`
(M25-M31 issued at the time). Picks up at M32. (M31 was later retired from
`FINANCIAL_MODEL_ROADMAP.md` and rebuilt as `docs/BASELINE_AI_ROADMAP.md`,
which continues the sequence through M42 — the current highest issued.)

**Origin:** the user asked directly for three connected changes to how a
route's capacity works: (1) a route's aircraft must come from the real
owned/leased pool; (2) how many round trips a single aircraft can fly each
week should be derived from its real performance and the route's distance,
not picked freely; (3) growing a route's capacity means assigning another
aircraft to it, and each additional aircraft "consumes the same slot as a
new route" — confirmed via a clarifying question to mean the literal
negotiated landing-rights/gate-capacity system (`LandingRightsBoard`), not
just the 3-action quarterly budget. Point 3 closes a gap already named
twice in the docs and never built: `GAME_SETUP_ROADMAP.md`'s "Kept in mind,
not scheduled" section states outright that `QuarterEngine` never checks
landing-rights slot ownership against a `RouteOrder` — winning a gateway
negotiation had zero mechanical effect on what routes could actually be
flown there.

Split into two milestones (matching this project's M25/M25b precedent):
M32 is the mechanical/fleet-count model, independently shippable and
measurable; M33 builds the gateway-slot consumption on top of it, since
that needs `LandingRightsBoard` to track per-airline holdings for the
first time.

---

## M32 — Flight-performance-derived frequency + multi-aircraft routes ✅

**`AircraftType` gained `CruiseSpeedKmh`** (default 900, a generic jet-age
figure, so synthetic test aircraft need no changes). `data/aircraft.json`
gained a real, historically-reasonable cruise speed for all 38 entries —
approximate public-knowledge figures (DC-3 ≈280 km/h, Constellation ≈480,
Boeing 707 ≈900, 747 ≈907, Concorde ≈2150), not claimed as precise, same
spirit as the existing `costPerAvailableSeatKm` figures. `AircraftCatalog`
validates it's positive, matching every other field's pattern.

**New `FlightPerformance` static helper**, isolated from `Route` for direct
unit testing:
```csharp
public const double TurnaroundHoursPerLanding = 1.0; // flat, tunable
public const double UtilizationHoursPerDay = 10.0;   // flat, tunable

public static int MaxWeeklyRoundTrips(AircraftType aircraft, double distanceKm)
{
    var roundTripHours = 2 * (distanceKm / aircraft.CruiseSpeedKmh) + 2 * TurnaroundHoursPerLanding;
    return Math.Max(1, (int)(UtilizationHoursPerDay * 7 / roundTripHours));
}
```
Sanity-checked against real routes by hand: a Boeing 707 on JFK-LHR
(~5,550 km) comes out to ~4 round trips/week; a 747 near its own max range
(~12,000 km) comes out to ~2/week; a short regional hop comes out to
~15-30/week — plausible without claiming precision, matching CLAUDE.md's
"legibility over simulation depth" pillar. Floors at 1: an assigned
aircraft always flies at least weekly, never a degenerate zero.

**`Route`'s stored field changed from `WeeklyRoundTrips` to
`AircraftCount`** — the number of aircraft assigned. `WeeklyRoundTrips`
becomes a computed property (`MaxWeeklyRoundTripsPerAircraft × AircraftCount`).
`SeatsPerQuarter` keeps its exact formula, now reading the derived value.
`NetworkPlanner`, `CompetitiveMarket`/`CompetitiveRouteUsage`, and
`FinancialResolver` needed **zero changes** — confirmed by exploration
before writing any code: they only ever consume `Route.SeatsPerQuarter`/
`DistanceKm`/`Aircraft` as opaque values and never assumed one Route backed
by one physical aircraft.

**`RouteOrder` dropped `WeeklyRoundTrips` entirely** —
`RouteOrder(Origin, Destination, Aircraft)`. Every submission, opening a
new pair or adding another aircraft to an existing one, means the same
thing: "assign one more aircraft of this type here." **`QuarterEngine.Cost`'s
`RouteOrder` case became a flat `1`, always** — the free-frequency-bump
concept is gone; growing a route now always costs an action. This is a
real, deliberate revision of CLAUDE.md's old "adding frequency to a pair
you already serve is free" line, updated in the same pass this shipped in.

**`QuarterEngine`'s `RouteOrder` handling was restructured:**
- Every accepted `RouteOrder` now needs exactly one more idle unit — the
  old "only if aircraft type changed" conditionality is gone (it existed
  to support type-*swapping*, which no longer exists).
- A `RouteOrder` naming a **different** aircraft type than a route already
  in flight is **rejected outright** when fleet is tracked — a deliberate
  scope narrowing (mirrors M26's idle-only-retirement narrowing): swapping
  a route's type isn't supported yet, since there's no detach action to
  free the old units (M27 territory). Checked against a running
  per-quarter commitment map seeded from `currentRoutes` and updated as
  orders are accepted — unlike `Cost()` (deliberately evaluated against
  quarter-start state only, to keep cost order-independent within a
  batch), type-consistency is a real correctness constraint, so two orders
  touching the same pair in the *same* batch have to be checked against
  each other too, not just against quarter-start state. A dedicated test
  covers exactly this: two `RouteOrder`s for one pair in one quarter with
  different types, second rejected, its fleet unit never consumed.
- On success, the new unit is assigned to the **same** route key as any
  units already flying it — never unassign-then-reassign anymore (that was
  only needed for type-swapping). `FleetLedger` already supported multiple
  distinct units sharing one `AssignedRouteKey` structurally; no
  `FleetLedger` changes were needed.
- `ApplyRouteOrders` was rewritten to fold applied `RouteOrder`s
  sequentially: same type already there for that pair → `AircraftCount + 1`;
  different type (or a brand-new pair) → fresh start at `1`. This single
  fold is correct in both fleet-aware mode (type-consistency already
  guaranteed by the rejection above) and permissive mode (`fleet: null`,
  where a type change on an existing pair is still allowed, matching that
  mode's existing "ownership simply isn't enforced" character) without
  needing to know which mode produced the orders.

**`BaselineAi` changes:** the `NewRouteWeeklyRoundTrips`/`FrequencyIncrement`
constants disappeared. "Grow the best-performing route" became "add one
more aircraft to it," mirroring the exact idle-first/lease-fallback
pattern the new-pair logic already used — idle unit of the route's own
type first (cost 1); if none idle, lease one more of that *same* type and
add it in the same 2-action batch (no "best available type" search needed,
since type must match the route already in flight). A new local
`reservedUnitIds` set prevents the new-pair search from also proposing the
same idle unit the growth step just claimed within the same planning pass
(a real cross-step conflict introduced by growth becoming fleet-consuming
for the first time — QuarterEngine would have rejected the resulting
double-booked order as a wasted action if left unhandled).

**Test/data migration was the bulk of the effort**, exactly as scoped:
`Route`/`RouteOrder`'s 4th positional argument changed meaning
(`WeeklyRoundTrips` → nothing / `AircraftCount`), so every existing call
site needed inspection, not just compilation — ~15 files touched
(production: `Route.cs`, `RouteOrder.cs`, `AircraftType.cs`,
`AircraftCatalog.cs`, new `FlightPerformance.cs`, `QuarterEngine.cs`,
`BaselineAi.cs`, `AeroNext.Sim/Program.cs`, `AeroNext.QaHarness/FullGameRunner.cs`,
`data/aircraft.json`; tests: `QuarterEngineTests`, `BaselineAiTests`,
`BriefingGeneratorTests`, `FinancialResolverTests`, `AircraftRangeTests`,
`RouteEvaluatorTests`, `EraAvailabilityTests`, `CompetitiveMarketTests`,
`NetworkPlannerTests`, `GameSetupIntegrationTests`, `StartingConditionsTests`,
`AircraftCatalogTests`). Several tests whose entire premise depended on
the old free/arbitrary-frequency model needed real rewrites, not
mechanical fixes — most notably the old "existing pair replaces rather
than duplicates" test (now "adds another aircraft, doesn't replace"), the
old "frequency bump needs no fleet check" test (now the *opposite* is
true — growing an existing route needs a fresh idle unit just like
opening one), and the old "aircraft type swap frees the old unit" test
(now type swapping is rejected outright, not performed).

**Done when:** the pre-existing call sites compile and pass after
inspection (not assumed unaffected); dedicated tests cover the derived
frequency formula in isolation, multi-aircraft accumulation both across
and within a single quarter's batch, type-mismatch rejection both across
quarters and within one batch, and `BaselineAi` never proposing what the
engine would reject. Both real loops re-verified at full scale.

Done. 368 tests total (10 new — 5 `FlightPerformance`, 3 `AircraftCatalog`
cruise-speed cases, 2 dedicated `QuarterEngine` same-quarter-batch tests),
plus every pre-existing `Route`/`RouteOrder`-constructing test rewritten in
place rather than counted as "new." 100% line/branch coverage on every
touched/new class (`Route`, `RouteOrder`, `AircraftType`, `AircraftCatalog`,
`FlightPerformance`, `QuarterEngine`, `BaselineAi`). Overall project
coverage **99.83%/99.47%**, at the established baseline (the only classes
below 100% — `BriefingGenerator`, `QuarterOrder`, `NetworkPlanner` — are
pre-existing gaps this milestone never touched, confirmed via `git diff`).
150-game QA harness sweep at 244 quarters: 150/150 clean, 0 invariant
violations, 0 crashes.

**The campaign trajectory shifted again, re-measured properly rather than
assumed.** `AeroNext.Sim`'s M13 loop: bankruptcy now at **quarter 16
(1958, -$2,670,885.57)** — earlier than M26's quarter 25. Root cause is
structural, not a repeat of M26's lease-fee story: `BaselineAi` now spends
its 3-action budget on growing its best-performing route (idle-or-lease,
1-2 actions) *before* trying anything else — once nothing's idle (every
quarter after the first, since it always immediately uses whatever it
leases), growth alone costs 2, leaving exactly 1 more action: enough for a
negotiation bid, never enough for a second new pair (which also needs a
lease). Route *count* freezes at 2 from quarter 3 onward for exactly this
reason, confirmed directly in the sweep output, and the AI keeps piling
more aircraft onto those same two routes long after local demand
saturates them — paying real lease fees for capacity nobody's buying,
while flat debt service never lets up. This is the action economy working
exactly as CLAUDE.md's own "attention is the scarce resource" pillar
intends — the AI can no longer do everything every quarter for free — but
the practical result for this un-tuned heuristic AI is an earlier, more
structural bankruptcy than M26's.

**QA harness sweep confirms this isn't M13-specific, and is a real
regression in playability worth flagging plainly:** 150/150 games had at
least one bankruptcy (unchanged from M26), bankruptcy now averages
quarter 10.6-20.4 across regions (versus M26's high-teens-to-mid-20s —
earlier), and **win rate dropped to 0/150** (versus M26's 2/150) — no
game produced a win-condition winner at all. With route count effectively
capped at 2-3 for the whole campaign, no seat ever builds a network wide
enough to plausibly lead 3+ regions simultaneously for 4 consecutive
quarters, the win condition's own requirement. Flagged here rather than
silently absorbed: `BaselineAi` now has three real design gaps compounding
— M26's recurring lease cost, M32's action-budget competition, and the
resulting near-total loss of win-condition reachability — all pointing at
the same underlying need for a materially smarter `BaselineAi` (real
buy-vs-lease judgment, or a growth heuristic that stops once load factor
drops rather than growing unconditionally), a decision for the next
design pass, not resolved here.

---

## M33 — Gateway slots gate route/aircraft assignment ✅

**`LandingRightsBoard` gained per-airline slot tracking** — `SlotsGranted`
stayed as the city-wide total (still needed for "how many are left to
contest"), and a new `IReadOnlyDictionary<(string AirlineName, string CityId), int> SlotsHeld`
was added, populated by `LandingRightsResolver.ResolveQuarter` alongside
it (the resolver already knew the winning airline via `pending.Airline` —
it was just being discarded after the transient `NegotiationOutcome`). A
new `SlotsHeldBy(airline, city)` reader and `AvailableSlotsByCity(airline,
currentRoutes)` — the airline's real per-city credit balance, held minus
in-use — landed on `LandingRightsBoard` itself, computed fresh from live
route state (`AircraftCount` summed per gateway city touched) each call
rather than a separately persisted ledger, so a route closing or
shrinking frees capacity automatically with no extra bookkeeping.

**`QuarterEngine.RunQuarter` gained `IReadOnlyDictionary<string, int>? availableSlotsByCity = null`**
— null (the default) preserves fully permissive behavior for every
pre-existing call site, confirmed directly (the full pre-existing suite
passed unmodified before any new tests were added). A `RouteOrder`
touching a city with non-null `GateCapacity` is rejected if the balance is
spent, checked independently at **both** `Origin` and `Destination` — a
route occupies capacity at every gateway endpoint it touches, not just
one — tracked via a running per-quarter spend counter, the same pattern
`cashSpent` already established.

**`BaselineAi` became slot-aware**, mirroring the existing cash/fleet
"never attempt what will predictably fail" principle: a growth or
new-pair candidate touching a gateway with no available slot is skipped
outright, not searched around for an alternative (this AI's existing
"single best candidate, no fallback search" character, unchanged). A new
local `slotsReservedByCity` map — the same idea as the existing
`reservedUnitIds` set — prevents the growth step and new-pair step from
double-spending the same city's balance within one planning pass.

**Deliberately left permissive: `AeroNext.Sim`'s M13 loop.** That loop
never wires `NegotiationOrder`s into `LandingRightsResolver` at all — a
pre-existing, already-documented gap (`FullGameRunner.cs`'s own doc
comment names it) — so it never builds a real `LandingRightsBoard` to
compute a genuine balance from. Passing `null` there (both calls)
preserves its existing behavior rather than gating every gateway route
outright against an always-empty board; building the missing negotiation
wiring for M13 specifically was judged separate, unrequested scope.
Confirmed by re-running the full M13 sweep: byte-identical output to
M32's, since this code path is never exercised there.

**One real correctness wrinkle, caught before it shipped:** M18's
guaranteed quarter-1 route has no way to pre-acquire a negotiated slot —
nothing in the starting-conditions design grants one — so gating it by
slots would occasionally break the "always accepted" invariant
`FullGameRunner.cs` already checks, whenever a seat's starting or
guaranteed-destination city happened to be a gateway. Fixed by exempting
quarter 1's guaranteed route specifically (`availableSlotsByCity: null`
for that one call), leaving every quarter from 2 onward genuinely gated.

**Done when:** the pre-existing call sites pass unmodified; dedicated
tests cover a won negotiation populating `SlotsHeld` for the winning
airline specifically (not the loser, not the city-wide total alone),
`AvailableSlotsByCity`'s held-minus-in-use arithmetic (including the
gateway-as-destination case, not just origin, and flooring at zero rather
than going negative), `QuarterEngine` rejecting at zero balance and
correctly spending down a shared balance across two orders in one batch,
a non-gateway city never gated regardless of the dictionary's contents,
both endpoints independently gating when both are gateways, and
`BaselineAi` never proposing what the engine would reject either for
growth or for a new pair. `LandingRightsResolver`'s existing negotiation
tests (bidding, standing, partial payoff) all passed unmodified,
confirming `SlotsHeld` is genuinely additive to that system, not a
behavior change to it.

Done. 385 tests total (16 new — 7 `LandingRightsBoard`/`Resolver`, 5
`QuarterEngine`, 4 `BaselineAi`). 100% line/branch coverage on every
touched/new class (`QuarterEngine`, `LandingRightsBoard`,
`LandingRightsResolver`, `BaselineAi`). Overall project coverage
**99.83%/99.51%**, at the established baseline. 150-game QA harness
sweep at 244 quarters: 150/150 clean, 0 invariant violations, 0 crashes.

**Bankruptcy moved earlier again in the QA harness sweep — a real,
compounding finding, not silently absorbed.** Bankruptcy now averages
quarter 5.8-9.6 across regions (versus M32's 10.6-20.4), win rate stayed
at **0/150**. Root cause: an un-negotiated gateway is now a genuine wall
for `BaselineAi` — a route it could previously open or grow instantly now
has to wait 2-4 quarters for a negotiation to land *and* spend an action
on it, on top of every constraint M26 and M32 already added. This is the
mechanic working exactly as designed (CLAUDE.md's own "landing slots are
finite; taking one denies a rival" pillar finally has real teeth — winning
a gateway negotiation now visibly matters), but it's the third milestone
in a row where a legitimate, individually-correct mechanic has made this
particular un-tuned heuristic AI's campaign trajectory worse. `BaselineAi`
needs a real rework before M26/M32/M33's mechanics can be fairly judged
on campaign feel or win-condition reachability — no longer a "maybe
later," a clear prerequisite for the next piece of work on this game.

---

## M45 — Universal slot capacity + idle-slot recontestability ✅

**Origin:** the user asked directly for four connected changes, requested
in one message: (1) every city, not just the 15 tagged gateways, should
require a landing slot for a route to operate there; (2) slot consumption
must correlate with flight volume — adding another aircraft can't add
capacity if the city has none left; (3) the same rule stated as its
failure mode — a `RouteOrder` that would push a city over capacity is
rejected; (4) **new** — a slot an airline holds but leaves unused for 4
consecutive quarters becomes contestable by other airlines, though the
owner keeps using it freely as long as nobody else has bid on it.

Investigation found (2) and (3) were **already fully built**, but scoped
only to the 15 `IsGateway` cities — M33's own `LandingRightsBoard.AvailableSlotsByCity`
already summed `Route.AircraftCount` per city for a live "held minus
in-use" balance, and `QuarterEngine.RunQuarter`'s `availableSlotsByCity`
parameter already rejected a `RouteOrder` that would exceed it. The user
confirmed (via a clarifying question) real scope should extend to **all
99 cities**, capacity derived from each city's own `demandWeight`. This
also directly closes a real, already-flagged gap in
`docs/FINANCIAL_MODEL_ROADMAP.md`: "no ongoing gate-holding cost — a won
slot is free to hold forever" — item (4) is exactly that fix, scoped to
the gateway/negotiated mechanic where hoarding is actually possible.

**Two different mechanisms for two different kinds of city, not one
mechanism stretched over both.** CLAUDE.md's own action-economy taxonomy
already separates "Enter a new city-pair" (**Market** — no shared object)
from "Negotiate landing rights" (**Contested** — a scarce object multiple
airlines bid on). Making all 99 cities require a silent, 2-4-quarter
blind negotiation before a single route can open there would convert
nearly every Market action into a gated Contested prerequisite, wrecking
the 90-second-turn pacing constraint for routine expansion. The user's own
idle-then-contestable rule also only makes sense where *ownership*
exists — "owned, but unused... can be bid on" — and ordinary demand-driven
capacity has no owner to hoard it; it already frees the instant a route
shrinks (`AvailableSlotsByCity` is live-derived from current routes, not a
stale ledger), a strictly faster release than any 4-quarter clock.

**A: non-gateway cities (84) — automatic, ownerless, city-wide capacity.**
New `City.AutoCapacity` field, mutually exclusive with `GateCapacity`
(`CityCatalog` now throws if a city ever carries both). Derived once and
written into `data/cities.json`: `Max(1, Round(DemandWeight / 25.0))` —
weight 15→1, 50→2, 95→4, a deliberately tighter range than the curated
gateway range (2-3) so ordinary cities never read as *more* generous than
real chokepoints. No ownership, no negotiation — the constraint is a
shared ceiling on total `AircraftCount` from **every** airline's routes
touching the city, checked independently at Origin and Destination, same
per-endpoint shape the gateway check already has. `QuarterEngine.RunQuarter`
gained `cityWideSlotsRemaining` (null-permissive, same convention as
every other optional enforcement parameter here) mirroring
`availableSlotsByCity`'s own rejection/spend-tracking shape exactly.
Because this balance has no per-airline owner, the caller must recompute
it fresh immediately before *each* airline's own `RunQuarter` call within
a quarter (`AiToolkit.ComputeCityWideSlotsRemaining`, from routes as they
stand at that point in the loop) — otherwise two airlines could each
independently see spare room and collectively overshoot a hard cap; a
small, honest first-come-by-processing-order property this introduces,
called out rather than hidden. `StrategyContext` gained the matching
`CityWideSlotsRemaining` field and a new `AiToolkit.HasRoomAtEveryCity` /
`ReserveCityWideSlots` pair, wired into `RapidExpansionStrategy` and
`DomesticProfitOptimizerStrategy` alongside their existing
`HasSlotAtEveryGateway` checks — the AI never proposes what the engine
would reject, same principle M33 established.

**B: gateway cities (unchanged 15) — idle-then-contestable ownership.**
`LandingRightsBoard` gained `IdleStreak` (consecutive quarters a held
slot's "held minus in-use" balance has been positive) and
`AdvanceIdleTracking`, run once per quarter from live route state — the
same "never a separately persisted ledger" philosophy `AvailableSlotsByCity`
already established. `IdleExpiryThreshold = 4`. `LandingRightsResolver.ResolveQuarter`
gained an optional `routesByAirline` parameter (null-permissive): when a
city's fresh `SlotsRemaining` is spent but idle-expired unused inventory
exists, a ranked challenger can win a **transfer** instead of a fresh
grant — the idle owner's `SlotsHeld` decrements, the winner's increments,
`SlotsGranted`'s city-wide total is untouched (ownership moved, it never
vacated). The idle owner is never auto-entered as a bidder for their own
slot — passive retention only, exactly "used by the owner if there is no
current bids"; a self-transfer (the owner actively re-bids and the
auction draws from their own idle-expired unit) nets to zero change by
construction, not a special case. `AiToolkit.FindBestIdleContestableGateway`
(plus a region-restricted sibling in `DomesticProfitOptimizerStrategy`)
gives AI strategies a way to actually reach this inventory — otherwise it
would only ever be reachable by a human bidding by hand, a real gap per
CLAUDE.md's "the sim never runs with fewer than 4 competing networks."
`FullGameRunner.cs` is the only real loop wired end to end, matching
M33's own precedent of leaving `AeroNext.Sim`'s M13 loop permissive for
gateway-slot enforcement.

**Done when:** every pre-existing call site compiles and passes
unmodified (`availableSlotsByCity`/`homeCity`/`ResolveQuarter`'s existing
signatures all kept null-permissive defaults); dedicated tests cover the
`AutoCapacity` formula and data completeness, `QuarterEngine` rejecting at
a zero city-wide balance (both endpoints independently, cross-order
same-batch spending, a gateway and an AutoCapacity endpoint gating
independently on the same route), `LandingRightsBoard.AdvanceIdleTracking`'s
increment/reset arithmetic, `LandingRightsResolver`'s idle-expired
transfer (winner gains, owner loses, `SlotsGranted` unchanged, owner never
auto-bids, self-transfer nets to zero), and both real strategies never
proposing what the engine would reject for either new mechanism.

Done. 653 tests total (50 new — 6 `QuarterEngine`, 5 `CityCatalog`, 16
`LandingRightsBoard`/`Resolver`, 15 `AiToolkit` (new file), 8
`RapidExpansionStrategy`/`DomesticProfitOptimizerStrategy`). 100%
line/branch coverage on every touched/new class; the only remaining gaps
project-wide are pre-existing ones already documented (`QuarterOrder`,
`BriefingGenerator`, `NetworkPlanner`) or confirmed untouched by this
milestone via `git diff` (`AiToolkit.TryOrientFromValidOrigin`'s own
already-documented M44 gap, `BaselineAi`, neither modified here). Overall
project coverage 99.76%/99.43%. 300-game QA harness sweep at 244 quarters:
300/300 clean, 0 invariant violations, 0 crashes.

**A large campaign-pacing effect, measured and isolated, not just
observed.** Overall win rate **83.3% (M44) → 36.3%** (109/300, avg
quarter 20.9 vs. 23.0). Region-by-region (bankrupt / win share,
M44 → M45): NorthAmerica 8.4%/87.6% → 8.4%/83.5% (essentially unchanged —
its dense 22-city local network absorbs the new capacity ceiling without
strain); Europe 86.3%/1.0% → 85.8%/0.0%; EastAsia **14.1%/5.5% →
59.7%/0.0%** (the single largest regional swing); SouthWestAsia
21.0%/24.8% → 22.9%/16.5%; AustralAsia 56.7%/6.2% → **33.0%**/0.0%
(bankruptcy actually improved here); SouthAmerica 88.2%/4.2% →
**66.7%**/0.0% (also improved). Not a uniform "thin regions get hurt more"
story — bankruptcy moved in *both* directions by region, but win share
collapsed almost everywhere, meaning the win condition (3+ regions led 4
consecutive quarters) got harder to satisfy for anyone, not that any one
region's collapse mechanically handed NorthAmerica more wins.

**Isolated by ablation** (temporarily disabling each new mechanism in
turn, `FullGameRunner.cs` only, reverted before shipping — same
instrument-then-revert discipline as M43's own investigations), same 300
seeds: both mechanisms off reproduces M44's own recorded numbers
*exactly* (83.3%, 250/300, avg quarter 23.0 — confirms the ablation
methodology itself is sound, not a coincidence). AutoCapacity alone
(idle-recontest off): **56.7%** (170/300). Idle-recontest alone
(AutoCapacity off): **74.7%** (224/300). Both together (real M45):
**36.3%**. The combined drop (47.0 points) is larger than the sum of
each mechanism's own isolated drop (26.6 + 8.6 = 35.2 points) — a real
compounding interaction between the two, not two independent effects
added together: AutoCapacity caps how far any single airline's organic,
M44-contiguous growth can run through shared local cities, while
idle-recontest adds a new way an airline can *lose* ground it already
built, and together they make it markedly harder for any one airline to
build and hold the kind of multi-region lead the win condition requires.

**Flagged plainly, not smoothed over, matching M32/M33's own precedent**
(both of *those* milestones also made this same un-tuned heuristic AI's
campaign trajectory measurably worse) — this is CLAUDE.md's own "landing
slots are finite; taking one denies a rival" pillar finally applying
everywhere, not just the 15 curated gateways, and it visibly costs the
AI's own reachability of the win condition. `BaselineAi`/successor
strategies needing a real rework before the *cumulative* effect of
M26/M32/M33/M45's mechanics can be fairly judged on campaign feel remains
the standing conclusion, now with one more compounding data point.

---

## M46 — Periodic, demand-driven slot-capacity expansion ✅

**Origin:** the user asked directly for cities to gain slots over the
campaign as demand grows, on a fixed periodic cadence — modeling real
airport terminal/runway expansion (discrete, infrequent projects, not a
continuous dial). This is the exact gap `docs/HISTORICAL_DYNAMICS_ROADMAP.md`'s
own "Kept in mind" section already named: "Gate capacity... growing via
terminal expansions... structurally bigger than a demand-weight
schedule... touches `LandingRightsBoard`." That same bullet also names a
*different*, harder problem — "a city that wasn't a gateway becoming one
later" — which this milestone deliberately does not attempt; M46 is only
about each city's own *existing* capacity field (`GateCapacity` or M45's
`AutoCapacity`) growing in place.

**Both mechanisms grow, gateway and `AutoCapacity` alike** — one shared
formula, not two, since growth is a passive, ownerless fact about the
city itself, upstream of however its capacity then gets allocated (unlike
M45's own gating mechanisms, there's no Market-vs-Contested distinction
to preserve here).

**New `CityCapacityBoard`** (`src/AeroNext.Core/Negotiations/CityCapacityBoard.cs`) —
a separate board from `LandingRightsBoard` rather than a field folded
into it, since it applies to every city, not just the 15 negotiated
gateways. `Initialize(cities, campaignStartDemandModel)` computes each
city's total demand (every `PairDemand.PassengersPerQuarter` touching it,
via `GravityDemandModel.AllPairs`) once, at quarter 1/year 1955 — the
fixed reference point every later check measures growth against.
`AdvanceQuarter(quarterNumber, cities, demandModel)` is a no-op unless
`quarterNumber % ExpansionPeriodQuarters == 0` (20 quarters = 5 years,
`(Proposed, tunable)`).

**Round 2, per the user's own direct follow-up request:** growth is not
one flat fraction shared by every city — each city gets its own
`GrowthBreakpoints` schedule, sized to its own starting capacity
(`GateCapacity` or `AutoCapacity`, M45's own mutual-exclusivity rule).
Breakpoints are `BaseGrowthIncrementFraction x ReferenceBaseCapacity /
startingCapacity` apart (both `(Proposed, tunable)`: 0.5 and 2
respectively — a city exactly at `ReferenceBaseCapacity` reproduces
round 1's original flat-50%-per-slot pace exactly), and there are always
exactly `startingCapacity` of them — a city that starts with 1 slot gets
one single breakpoint, at *double* its own baseline demand, and can never
earn more than that one bonus slot regardless of how much further demand
grows; a city that starts with 4 gets four, more closely spaced, and can
earn up to four more. Every city's own *last* breakpoint always lands at
exactly double baseline, by construction — only how many chances it gets
on the way there differs by size. On a check quarter, a city's bonus
becomes `Max(existingBonus, GrowthBreakpoints(city).Count(bp => currentDemand >= baseline * bp))`
— still a pure function of (fixed baseline, current demand, its own
schedule, previous bonus), still a ratchet, same reasoning as round 1.
**Directly closes round 1's own real gap**: a flat fraction let a small
city rack up just as many bonus slots as a major hub from a large
*relative* swing alone, even though its *absolute* volume never
remotely justified a real terminal expansion — exactly the failure mode
the user named. No cap on a *large* city's total bonus beyond its own
schedule length, matching CLAUDE.md's own "chokepoints obsolete as range
improves" precedent for scarcity fading late-campaign at cities that
actually earn it.

**Consumed via two small optional parameters on paths that already read
capacity:** `LandingRightsBoard.SlotsRemaining(city, capacityBonus = 0)`
adds the bonus onto `GateCapacity` before subtracting what's granted;
`LandingRightsResolver.ResolveQuarter` and `AiToolkit.ComputeCityWideSlotsRemaining`
each gained a matching optional `capacityBonusByCity` dictionary
parameter, null-permissive by default, threaded straight through.
`FullGameRunner.cs` initializes the board once before its quarter loop
and calls `AdvanceQuarter` at the *start* of each quarter (unlike M45's
`AdvanceIdleTracking`, which must run at quarter-*end* to see that
quarter's own route changes first, expansion depends only on demand, so
resolving it early makes a check quarter's new capacity visible to every
airline's own decisions that same quarter) — the only real loop wired
end to end, matching M33/M45's own precedent of leaving `AeroNext.Sim`'s
M13 loop permissive.

**Deliberately out of scope:** briefing/exception-queue surfacing of a
capacity-expansion event — `BriefingGenerator.Generate` operates on one
airline's own `QuarterResult`, with no natural home for a world-level
event, the same kind of gap `docs/WIN_CONDITIONS_ROADMAP.md`'s own "Kept
in mind" already accepts for win-streak progress.

**Done when:** every pre-existing call site's default/null behavior is
unchanged (confirmed: the full pre-existing suite passed unmodified);
dedicated tests cover `Initialize`'s baseline computation, `GrowthBreakpoints`
itself (schedule length matches starting capacity, the reference-capacity
case reproduces round 1's own two-breakpoint schedule exactly, every
breakpoint lands between 1x and 2x, the last always at exactly 2x, a city
with neither capacity field returns an empty schedule), `AdvanceQuarter`'s
no-op off-cadence, a small city never earning more than its own starting
capacity even under enormous growth, a small city earning nothing from a
swing that would have earned a bonus at `ReferenceBaseCapacity`, the
ratchet, a zero-baseline city never dividing by zero, and cities
evaluated independently of each other; `SlotsRemaining`/`ResolveQuarter`/
`ComputeCityWideSlotsRemaining` each tested with and without a bonus.

Done. 680 tests total (27 new over M45's own baseline — 19
`CityCapacityBoard` (new file, including round 2's own dedicated
`GrowthBreakpoints` tests, replacing several of round 1's flat-formula
tests in place), 6 `LandingRightsBoard`/`Resolver`, 2 `AiToolkit`). 100%
line/branch coverage on every
touched/new class (`CityCapacityBoard`, `LandingRightsBoard`,
`LandingRightsResolver`); `AiToolkit`'s own gaps are pre-existing and
already documented (M44's `TryOrientFromValidOrigin`), confirmed
untouched by this milestone via `git diff`. Overall project coverage
99.77%/99.44%. 300-game QA harness sweep at 244 quarters: 300/300 clean,
0 invariant violations, 0 crashes.

**Measured, both rounds, ablation-isolated throughout.** Round 1 (flat
50%-per-slot, no cap): overall win rate **36.3% (M45) → 38.7%** (116/300,
avg quarter 20.9 → 23.0), and bankruptcy rose in *every single region* —
NorthAmerica **8.4% → 27.0%** the sharpest, Europe 85.8% → 99.0%, EastAsia
59.7% → 61.4%, SouthWestAsia 22.9% → 36.2%, AustralAsia 33.0% → 68.0%,
SouthAmerica 66.7% → 92.0%. Isolated by ablation (`AdvanceQuarter`'s own
call temporarily commented out in `FullGameRunner.cs`, reverted before
shipping): with M46 disabled, the sweep reproduces M45's exact recorded
numbers byte for byte — confirmed the round-1 effect was real, not
sampling noise, and directly motivated the round-2 redesign above.

**Round 2 (per-city schedule) — the same 300 seeds, directly against
round 1:** overall win rate essentially unchanged, **38.7%** (116/300,
avg quarter 23.0 — identical win count to round 1). Region-by-region
(bankrupt, round 1 → round 2): NorthAmerica **27.0% → 23.7%**, Europe
99.0% → 99.0% (unchanged), EastAsia 61.4% → 60.7%, SouthWestAsia
36.2% → 32.4%, AustralAsia 68.0% → 64.9%, SouthAmerica 92.0% → 92.0%
(unchanged). Win share barely moved either (NorthAmerica 84.5% both
rounds, SouthWestAsia 15.5% both rounds). A genuine, if modest,
improvement in the intended direction — every region that moved at all
moved *toward* round 1's own pre-M46 (M45) numbers, never past them —
but not a large one: capping small cities' own runaway growth measurably
helps without being the dominant lever on this AI's own campaign
trajectory.

**The standing mechanism-grounded read carries forward, largely
unrevised by round 2:** by 2015 the global demand trend alone reaches
4.5x its 1955 level (`data/demand_growth.json`); at `ReferenceBaseCapacity`
(2) that's still ~7 bonus slots' worth of relative growth room from the
global curve alone (now genuinely earnable only by cities *at or above*
that reference size — round 2's whole point), before any city/region
event on top. `BaselineAi`/successor strategies still have no real
profitability judgment (the standing conclusion from M32/M33/M45/M46
alike) — they grow whenever legally possible, so a growing ceiling still
reads as more legal room to keep piling on leased aircraft and debt, now
just correctly withheld from cities too small to have earned it. The
same "legitimate, individually-correct mechanic makes this un-tuned
heuristic AI's trajectory worse" pattern as M26/M32/M33/M45, still
present, now measurably softened rather than eliminated. Whether
`ExpansionPeriodQuarters`/`BaseGrowthIncrementFraction`/`ReferenceBaseCapacity`
should be tuned further, or whether this remains squarely `BaselineAi`'s
own reachability problem regardless of the specific numbers, is a call
for the user, not decided here — all three constants are marked
`(Proposed, tunable)` in code precisely so this can be revisited without
a structural change.
