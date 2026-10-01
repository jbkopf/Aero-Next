# Multi-Stop Routes: Design

**Status: Phases 1 and 2 implemented; Phase 3 not started.** Written
2026-09-30 at the user's direct request. Phases are built and measured
in order; each phase's section gets a "Result" note once it ships.

## The problem

CLAUDE.md's chokepoint pillar says early aircraft can't cross oceans, so
routes need refuelling stops (Gander, the Azores, Wake, Honolulu), and
those cities matter because they gate access. The sim doesn't model that
today. A route is always a single nonstop leg, so the only way to use
Gander is to fly *to* Gander - a route whose own demand is almost nothing
(Gander's demand weight is 2).

Measured on 600 Hard games (seeds 1 and 301, 2026-09-28), that's the
single largest source of early-game losses: AI airlines that win a
chokepoint slot put an aircraft on the busiest pair touching it.
**676 such routes lost $2.6B a quarter, 81% of all early-game route
losses,** each averaging a $3.8M quarterly loss. Gander alone accounted
for 248 of them, Wake 77.

A throwaway probe (real 1955 data, `NetworkPlanner` + `FareCalculator`)
then checked whether chokepoints can pay at all. Modelled as two separate
routes that passengers connect between (Paris-Gander + Gander-Chicago),
418 of 2,190 combinations needing a stop were profitable, the best about
$2.2M a quarter. But only when each leg used a different aircraft type
sized so both legs offered similar seats. Same type on both legs, the
shorter leg flies more often, runs half empty, and the pair loses money.
That's not how airlines of the era worked: one aircraft flew Paris to
Chicago and stopped at Gander for fuel.

## The idea

A **multi-stop route** is one route between two endpoints that happens to
make intermediate stops for fuel. Opening Paris-Chicago with a DC-7 that
can't fly it nonstop means opening Paris-Gander-Chicago: one route, one
aircraft type, one action, one slot at each airport it touches including
Gander. Passengers are Paris-Chicago passengers. The aircraft's time and
the airline's cost cover every leg flown.

As range improves, the stop stops being needed. Swapping the route to a
longer-range type can drop the stop and free its slot, so a chokepoint
loses value naturally across the campaign - CLAUDE.md's "chokepoints
obsolete as range improves."

## Decided scope

Answered directly by the user (2026-09-30), recorded so the reasoning
survives this conversation:

- **The order names the stops.** A route order lists its stops in
  sequence (Paris -> Gander -> Chicago). A helper proposes the shortest
  valid stop path; the AI uses it, and a future UI can offer it as the
  default. The player chooses which chokepoint to fight over (Gander or
  the Azores).
- **Only gateway cities can be stops.** Gateways are the cities with
  hard gate caps (`City.IsGateway`): Gander, the Azores, Wake, Honolulu,
  Anchorage and Dakar, plus the large gateways (New York, London, etc.).
- **A type swap sets the route's new stop list,** which may be empty.
  Dropping a stop frees its slot.
- **Demand, cost and profit are for the endpoints.** Only Paris-Chicago
  passengers fly the route; nobody boards or leaves at Gander. Time and
  distance are counted across every leg.
- **Passenger choice comes from real price and journey time.** No flat
  penalty for having a stop. When rivals compete for a city pair, share
  is weighted by each offer's actual fare and journey time compared with
  the rivals' (Phase 2). A multi-stop route's longer distance and extra
  landings make it cost more and take longer, and that's what costs it
  passengers.

## Phase 1: multi-stop route mechanics

### Route model

`Route` gains an ordered stop list (default empty, so every existing
route and test is unchanged). Everything derived from distance or time
moves from "the endpoint pair" to "the legs flown":

| Property | Today | Multi-stop |
|---|---|---|
| `DistanceKm` | origin to destination | sum of all legs |
| Round-trip hours | 2 × distance / speed + 2 landings | 2 × total distance / speed + 2 × (stops + 1) landings |
| `SeatsPerQuarter` | from round trips | unchanged formula, fewer round trips |
| `IsWithinRange` | distance ≤ range | **every leg** ≤ range |
| Operating cost | seats × distance × cost per seat-km | same, total distance |
| Suggested fare | cost + 30% | same formula, so higher (longer distance, fewer seats to spread salary over) |
| Demand | gravity on the pair | unchanged: gravity on the **endpoints** |

`FlightPerformance.MaxWeeklyRoundTrips` takes the landing count, so each
stop costs one more turnaround per direction
(`TurnaroundHoursPerLanding`, 1 hour).

**Implementation note: `Route` must keep value equality.** It's a record
used as a dictionary key (`NetworkPlanner.Allocate`, `CompetitiveMarket`).
A plain `IReadOnlyList<City>` member would compare by reference and break
that silently. The stop list needs a small wrapper type with structural
`Equals`/`GetHashCode`.

### Identity

A route is still identified by its endpoint pair
(`QuarterEngine.NormalizedPairKey`). An airline has at most one route per
pair, and every aircraft on it flies the same stops, exactly as every
aircraft on a route already shares one type. Fare, marketing, route
history, closure and growth logic all keep working per pair, unchanged.

### Orders

- **`RouteOrder`** gains the stop list (default empty).
  - Opening a new pair: the stops are validated (below) and become the
    route's stops.
  - Adding an aircraft to an existing route: the stops must match the
    route's current stops, just as the type must match today. A
    mismatch is rejected.
- **`AdjustRouteOrder`** (type swap) gains the new stop list. It's
  validated against the new type. Stops dropped free their slots; stops
  added need a free slot. The existing "a swap resets to 1 aircraft" rule
  is unchanged.
- **`CloseRouteOrder`** is unchanged in shape; removing an aircraft frees
  its slot at every stop as well as the endpoints.

### Validation

A stop list is valid when:

1. every stop is a gateway city;
2. no city appears twice, and no stop is either endpoint;
3. every leg is within the aircraft's range.

(Trade restrictions limit which aircraft an airline can buy, by its home
city, not where it can land, so they don't apply to stops.)

A stop that isn't strictly needed (the aircraft could fly nonstop) is
allowed. It's the player's choice, and its cost is real. The AI never
proposes one.

### Slots and capacity

"One slot per aircraft operating through a gateway" now counts stops.
Each aircraft on Paris-Gander-Chicago uses one Gander slot, the same as
an aircraft flying to Gander. In practice:

- `QuarterEngine`'s gateway slot check (`GatewayEndpoints`) checks
  stops as well as endpoints.
- `LandingRightsBoard.AvailableSlotsByCity` counts an airline's used
  slots at a city from every route touching it, including as a stop.
- Stops are gateways, which have gate caps rather than automatic city
  capacity, so the city-wide capacity check (`AutoCapacityEndpoints`)
  only ever applies to endpoints. No change there.

Gander has 2 gates in total, so at most two aircraft in the world can
stop there. That scarcity is the point: it's CLAUDE.md's "three gates
and four airlines guarantees someone goes home empty."

### Network and passengers

- `NetworkPlanner` treats a multi-stop route as a link between its
  **endpoints only.** Paris and Chicago are nodes; Gander isn't, so no
  connecting itinerary routes through Gander on the strength of a stop.
  An airline that also flies a separate route into Gander still gets
  connections there in the normal way.
- Network-contiguity rules (a new route must touch the airline's
  network) apply to endpoints. A stop doesn't make a city part of the
  network.
- Regional dominance counts the endpoint passengers, as now. A stop at
  Gander adds no North American traffic.

### Done when

- An order for Paris-Gander-Chicago on a DC-7 is accepted; the same order
  without the stop is rejected as out of range.
- The route's distance, round trips, seats, operating cost and suggested
  fare reflect both legs; its demand is the Paris-Chicago gravity demand.
- It uses one Gander slot per aircraft and is rejected when Gander has
  no free slot for this airline.
- A swap to a type that can fly nonstop with an empty stop list frees
  the Gander slot.
- Every existing test passes unchanged; the QA harness reports no
  invariant violations.

### Result (2026-09-30)

Built as described. Where it lives:

- `StopList` (`src/AeroNext.Core/Airlines/StopList.cs`): a struct, so
  `default` is the empty list and every existing `Route`/`RouteOrder`
  construction is unchanged; equality compares city ids in order.
- `RoutePath`: shared leg geometry (cities, leg distances, total distance,
  per-leg range, stop validity) used by `Route` and both route orders.
  Nonstop routes take a fast path that never builds a leg list.
- `Route` gains `Stops` and `Touches(cityId)`; `FlightPerformance`
  takes a landings-per-direction count (default 1).
- `QuarterEngine`: invalid stops rejected; growing a served pair must
  match its stops (checked in both fleet-aware and permissive mode,
  tracked across the batch like the committed type); slots checked and
  spent at stops; a swap needs a slot only at a stop the route didn't
  make before; applied routes keep their stops.
- `LandingRightsBoard` counts an aircraft's slot at every city its route
  touches, stops included. `RouteAdjustmentOptions` checks swap range
  against the route's current stops.
- `BriefingGenerator` names stops in order descriptions
  ("cdg-ord via yqx") and reports invalid stops as their own rejection
  kind, `OrderRejectedInvalidStops`.

Tests: `MultiStopRouteTests.cs` (31), including the real-data example
above. Every pre-existing test passes unchanged, and every changed file
is fully line-covered. No AI proposes stops yet, so game outcomes are
identical to before.

## Phase 2: passenger choice by price and journey time

Today, when rivals compete for a city pair, each airline's share is
weighted by ([`CompetitiveMarket.cs`](../src/AeroNext.Core/Competition/CompetitiveMarket.cs)):

```
capacity × quality (incl. marketing) × fare multiplier
```

The fare multiplier compares an airline's fare only with **its own**
default fare. So a multi-stop route at its default fare looks exactly as
attractive as a cheaper, faster nonstop at its default. Price and time
never compare across rivals.

### Journey cost

Each competing offer on a pair gets a **journey cost per passenger**:

```
journey cost = fare paid + value of time × journey hours
```

- **Fare paid:** the route's fare. For today's one-stop connecting
  itinerary (two separate routes), the two legs' fares added together.
- **Journey hours:** flying time over every leg (distance / cruise speed)
  plus 1 hour per intermediate landing for a multi-stop route, or a
  connection time at the hub for a one-stop itinerary.
- **Value of time:** one proposed, tunable constant in dollars per hour,
  set by measurement. It must be large enough that a stop visibly costs
  passengers and small enough that fare stays the main lever.

### Share

The fare multiplier in the contested weight is replaced by a
journey-cost multiplier relative to the cheapest offer on the pair:

```
journey multiplier = (cheapest journey cost on the pair / own journey cost) ^ elasticity
weight = capacity × quality × journey multiplier
```

Elasticity reuses `FareCalculator.FareDemandElasticity` (1.5). The
cheapest offer gets 1.0; a 10% dearer offer gets about 0.87. The existing
ceiling (an airline never carries more than its own capacity allows) is
unchanged.

This replaces, rather than stacks on, the existing own-fare multiplier in
the contested branch. Stacking would count a fare change twice. The
uncontested branch's demand adjustment (a discount grows a monopoly
route's demand, a premium shrinks it) is untouched, so fare is still a
lever where nobody competes.

It applies to every contested pair, not only multi-stop ones: a
one-stop connection against a nonstop, two nonstops on different
aircraft types, and so on. Contested pairs are rare today (0.46% of
route-quarters, `OPEN_WORK.md` item 5), so the effect on existing play
should be small, but it's measured on its own before Phase 3.

### Done when

- On a contested pair, the offer with the lower journey cost wins a
  larger share at equal capacity and quality, and a pure fare cut still
  moves share as before.
- A multi-stop route loses share to a rival nonstop on the same pair,
  by an amount that follows from its fare and time, not a flat penalty.
- Measured: 2×300 Hard sweeps with no regression in bankruptcies or win
  timing beyond what the change explains.

### Result (2026-10-01)

Built as described:

- `JourneyCost` (`src/AeroNext.Core/Competition/JourneyCost.cs`): fare
  paid (both fares on a connection) plus journey hours at
  `ValueOfTimePerHour` = **$20/hour** (proposed, tunable). Hours are
  flying time over every route's full distance, 1 hour on the ground per
  refuelling stop, and `ConnectionHours` = **2 hours** per change of
  aircraft. At these values a 1955 DC-7 Paris-Gander-Chicago journey is
  about $1,120 fare + 12.7 hours (~$1,380 total), and a 1958 707 nonstop
  about $890 + 7.4 hours (~$1,035): the 707 keeps 1.0 of its weight, the
  DC-7 about 0.65.
- `CompetitiveMarket`'s contested weight uses
  `(cheapest / own journey cost) ^ 1.5` in place of the old own-fare
  multiplier, which is removed. Costs are floored at one cent so a zero or
  negative player fare can't break the split. The uncontested demand
  adjustment is unchanged.

Tests: `JourneyCostTests.cs` (9): the cost arithmetic, and contested
splits that follow the formula exactly for a dearer fare, a slower
aircraft and a refuelling stop, plus even splits for equal offers and a
safe split for a negative fare. Every pre-existing market test passes
unchanged, including the discount/premium ones, and both changed files
are fully line-covered.

Measured on 2x300 Hard games (seeds 1 and 301) against the same code
without it: bankruptcies, wins by region and the average win quarter are
identical on both seeds (15/21 games with a bankruptcy; wins at quarter
186.6/188.7); cash moved by under 1%. Expected, since contested pairs
are rare today - the change matters once routes actually compete, which
is Phase 3's job.

## Phase 3: AI uses multi-stop routes

Starts with `RapidExpansionStrategy` (the cross-region strategy of the
early game, and the source of the measured losses); other strategies can
reuse the same search once it's proven.

### Stop path search

A new toolkit helper, given a pair and an aircraft type, returns the
shortest stop path through gateway cities where every leg is within
range: nonstop if possible, otherwise the fewest stops, then the
shortest total distance, ties broken by city id. It's capped at **2
stops**, which is what the 1955 Pacific needs (Los Angeles - Honolulu -
Wake - Tokyo).

### Choosing and building a multi-stop route

- New-route searches consider pairs that need a stop: each candidate
  (pair, type) gets its stop path, and is scored like any other route:
  endpoint demand against the route's seats, the same load-factor screen
  and profit check. The longer distance and fewer seats come through
  `Route` itself, so no special case is needed.
- **Opening needs a slot at every stop.** The AI is stateless between
  quarters, so each quarter it re-finds its best multi-stop route and
  takes the next step:
  - a stop without a slot: negotiate for that stop;
  - slots held but no aircraft: buy or lease the type;
  - slots and aircraft ready: open the route.
- **Gateway bidding follows the plan.** Rapid Expansion's gateway
  negotiation targets a stop its best multi-stop route needs, rather than
  any reachable gateway by size.
- **No more lone legs into chokepoints.** The won-gateway step
  (`AiToolkit.FindBestUnservedPairTouchingWonSlot`) goes through the same
  profit check `BalancedGrowthStrategy` already applies
  (`AiToolkit.EvaluateCandidate`), so a won Gander slot waits for a
  multi-stop route instead of flying an unprofitable Gander leg.
- **Range upgrades.** When a held type can now fly a route nonstop, the
  existing type-swap logic considers swapping to it with an empty stop
  list, which frees the stop's slot.

### Done when, and how it's measured

2×300 Hard sweeps (seeds 1 and 301, 244 quarters) plus the early-route
trace used for the 2026-09-28 finding:

- **Targets:** multi-stop routes get built; the $2.6B-a-quarter loss from
  lone chokepoint legs largely disappears.
- **Guardrails:** bankrupt airlines, games with a bankruptcy, average win
  quarter, and win share by region.
- **The pillar check:** how often routes are contested
  (`OPEN_WORK.md` item 5). Chokepoint slots should become something
  airlines actually fight over.

## Deliberately out of scope

- **Passengers boarding or leaving at a stop.** A stop is for fuel only.
  A later "tag" option could sell stop-city seats too.
- **More than 2 stops.**
- **Stops at non-gateway cities.** The user chose gateways only; adding a
  stop city later (Shannon, Reykjavik) means marking it a gateway in
  `data/cities.json`.
- **Changing a route's stops without a type swap.** Close and reopen, or
  swap to the same type with different stops if the swap rules allow it;
  revisit only if play shows a need.
- **A UI.** The order shape and the stop path helper are what a future
  route-planning screen would use.
