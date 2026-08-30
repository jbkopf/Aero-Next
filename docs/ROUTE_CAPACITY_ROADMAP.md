# Route Capacity Roadmap

Continues the shared milestone numbering from `FINANCIAL_MODEL_ROADMAP.md`
(M25-M31, the highest issued so far — M31 itself not yet built). Picks up at
M32.

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

## M33 — Gateway slots gate route/aircraft assignment

**Design already settled** (see the M32 plan this shipped from, and the
Context section above) — not yet built:

**`LandingRightsBoard` gains per-airline slot tracking.** Today
`SlotsGranted` is a city-wide total only; nothing records which airline
holds how many. Add `IReadOnlyDictionary<(string AirlineName, string CityId), int> SlotsHeld`,
populated by `LandingRightsResolver.ResolveQuarter` alongside the existing
`SlotsGranted` update (it already knows the winning airline via
`pending.Airline`, currently discarded after the transient
`NegotiationOutcome`).

**`QuarterEngine` gains `IReadOnlyDictionary<string, int>? availableSlotsByCity = null`**
— a per-city credit balance for the airline, computed by the caller each
quarter as `max(0, heldSlots(airline, cityId) - inUseSlots(airline, cityId))`,
where `inUseSlots` is summed fresh from the airline's *current* routes
(`AircraftCount` on every route touching that city, cities with non-null
`GateCapacity` only) — deliberately not a separately persisted ledger, so
a route closing or shrinking automatically frees capacity with no extra
bookkeeping. A `RouteOrder` touching a gateway (checked at **both**
`Origin` and `Destination` independently — a route occupies capacity at
every gateway endpoint it touches) is rejected if the balance is spent,
tracked per-quarter the same way `cashSpent` already is.

**`BaselineAi` becomes slot-aware** for gateway cities, same "never
attempt what will predictably fail" principle already applied to cash and
fleet.

**Not yet started.**
