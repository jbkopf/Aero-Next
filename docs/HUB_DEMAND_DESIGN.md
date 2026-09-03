# Hub-and-Spoke Demand: Design & Feasibility

**Status: proposal, not implemented.** This is a feasibility study, not a
roadmap log — nothing here has shipped. It exists to inform a real decision
before any code is written, per the user's own direct request.

## The problem

The user's own framing, direct from a live investigation (see "What's
already been tried" below): large international carriers hub-and-spoke.
A hub's own passenger volume isn't mostly people who want to *visit* that
city — it's people *connecting through* it on the way somewhere else. Real
examples: Atlanta and Dubai are both airports where the majority of traffic
never leaves the terminal. The user wants each city to carry its own
standalone demand (tourism, economics, population — genuine reasons to fly
there), plus a separate modifier for cities that function as major
connecting points — and wants this to also stand in for the large number of
real-world small cities this game's ~99-city roster doesn't individually
model (their trips still have to go *somewhere*, typically through a hub).

## How demand actually works today

Three separate mechanisms currently exist, and understanding exactly how
they divide responsibility is the whole feasibility question. None of them
were touched by this document — this section is pure fact-finding.

### 1. `GravityDemandModel.ForPair` — the O&D (origin-destination) demand source

`src/AeroNext.Core/World/GravityDemandModel.cs`. For any two cities:

```
demand ∝ DemandWeight_a × DemandWeight_b / distance²
```

This is the *only* source of "how many real people want to travel from A to
B" in the whole codebase. It's pairwise by construction — every one of the
~4,900 city pairs gets its own independently-computed number, and
`DemandWeight` is a single field doing double duty: it's simultaneously "how
attractive is this city as a destination" *and* the only lever available
for "how much overall traffic touches this city at all."

### 2. `NetworkPlanner` — itinerary routing (this already models connections)

`src/AeroNext.Core/Airlines/NetworkPlanner.cs`. This is the part of the
picture that's easy to miss, and changes the shape of the whole problem:
**connecting-passenger routing through a hub already exists and already
works.** For every pair of cities an airline's own network *serves* (not
just cities with a direct route), `Plan` builds an itinerary: nonstop if a
direct route exists, otherwise the single-hub connection with the shortest
total great-circle detour (`FindBestHub`, lines 80-110). A Boston-Miami
passenger with no direct flight gets routed through whichever *already-built*
route pair minimizes detour distance — genuinely modeling "fly to a hub,
connect onward."

**The critical detail: the hub's own size or quality has zero influence on
any of this.** `FindBestHub` picks by pure geometry (`detourKm`, line 97) —
a small chokepoint city is exactly as eligible a hub as a major metropolis if
it happens to sit on the shortest path. And the *demand figure* being routed
is still `demandModel.ForPair(origin, destination)` — Boston's and Miami's
own weights, at the full Boston-Miami distance (`Allocate`, line 120).
Atlanta's own prominence contributes nothing extra; it's purely a waypoint.
This is allocated for real, for every airline, in real competitive
resolution (`CompetitiveMarket.Resolve`, `src/AeroNext.Core/Competition/CompetitiveMarket.cs:64`,
`104-134` — every pair's demand and every airline's own itinerary-based
`ItineraryLoad` feed directly into what actually gets carried and paid for,
not just an AI-side estimate).

### 3. `DomesticProfitOptimizerStrategy.EstimatedHubBonusDemand` — an AI-side *prediction* of #2

`src/AeroNext.Core/Ai/DomesticProfitOptimizerStrategy.cs:319-355`. This is
not a third, separate mechanism — it's one AI strategy's own estimate of
what `NetworkPlanner` would compute *if* a candidate new route were added to
its existing network, used to rank candidates before committing. It doesn't
change what "hub value" means; it just lets one strategy see it coming.

### A fourth, smaller piece: `CityCapacityBoard.TotalDemandByCity`

`src/AeroNext.Core/Negotiations/CityCapacityBoard.cs:131-142`. Sums every
`PairDemand.PassengersPerQuarter` touching a city, across `AllPairs` — a
real, already-existing "total demand at this city" aggregate. It only
drives M46's own periodic capacity-growth check today, but it's proof a
per-city aggregate is already cheap to compute from the pairwise source.

### What this means for the user's request

The user's request actually decomposes into two genuinely separate ideas,
and they map onto this architecture very differently:

- **"Connecting passengers get routed through a hub, not counted as
  visiting it"** — already true, already real, already paid for. Nothing
  to build here.
- **"A city's own quality/size as a hub should matter — Atlanta and Dubai
  aren't just any waypoint, they're built for this"** — genuinely missing.
  Hub *selection* is pure geometry; a city's own size, infrastructure, or
  real-world hub reputation has no representation anywhere.

That reframes "add standalone demand + a hub modifier" into something more
precise: **the O&D demand-generation piece (destination appeal) already
exists and is mostly fine; what's missing is a hub-*quality* concept that
actually influences hub *selection* and, if wanted, how much a city's own
"hub-ness" adds beyond what real O&D routing alone would carry.**

## What's already been tried, and why it's directly relevant here

Two designs were attempted and measured against real data in this same
investigation, immediately before this document, both targeting the
*pairwise `ForPair` formula itself* rather than the hub-selection layer:

1. **Additive catchment bonus** (`effectiveWeight = weight + strength ×
   Σ nearby-city-weight / distanceᵏ`, a standard Hansen accessibility
   index). Measured wrong, algebraically: an additive bonus is a *larger
   percentage* of a small city's own small base than of a big city's
   already-large one, so it structurally favors small cities more — the
   opposite of the goal, confirmed at every strength tested.
2. **Weight-dependent distance exponent** (`effectiveExponent = exponent -
   discount × maxWeight / referenceWeight`). This one did flip the
   intended rankings (a distant real hub could out-rank a close small
   city) — but at the strength needed to do that, total world demand
   inflated **31×**, even for purely domestic pairs with no hub
   involvement at all (a reference pair grew 41×). Tested across ten
   strength values: below the point where demand stays sane, the ranking
   never flips; above it, both effects hit at once. Shipped to a real
   300-game sweep anyway (per this project's own "verify via the real
   sweep" discipline) and collapsed the win rate from 39.0% to 0.3%.
   Reverted before this document was written.

**The lesson, and why it matters for this proposal specifically:** both
failures came from injecting a hub effect into `ForPair`'s own core
formula — the piece responsible for the *entire game's* demand baseline,
already calibrated (`DefaultScale = 7.5e6`) around the existing exponent.
Small changes there have global, multiplicative, hard-to-bound
consequences, because every one of ~4,900 pairs runs through the same
formula. **A hub-quality mechanism that instead lives in `NetworkPlanner`'s
own hub-*selection* step touches a fundamentally smaller, more contained
surface** — it only ever affects itineraries that already need a
connection (no direct route exists), never the baseline O&D figure for any
pair, and never a pair that already has a direct flight. This is the
central reason the recommendation below is shaped the way it is.

## Two paths, and a recommendation

### Path A — full replacement: trip generation + distribution

The rigorous version of "each city has its own standalone demand": adopt
the real transportation-planning "four-step model" pattern this idea
actually maps onto — separate **trip generation** (each city's own total
outbound/inbound trip count, from its own attributes) from **trip
distribution** (a second step that allocates those per-city totals across
specific pairs, typically still gravity-shaped, but now constrained so
each city's row/column sums match its own generated total — a "singly- or
doubly-constrained gravity model").

This is a real, well-established methodology, not a made-up idea. But it
is a substantially larger change than either of the two failed attempts:

- **It replaces the demand *source* itself**, not a coefficient inside it —
  every one of ~4,900 pairs' numbers changes, and the whole model needs
  fresh calibration from scratch (the existing `DefaultScale`/`DefaultExponent`
  were tuned by inspection for the *current* formula; they mean nothing
  once the generation step is separated out).
- **A constrained distribution step is iterative** (real four-step models
  typically balance row/column totals via repeated proportional fitting,
  since a single gravity pass doesn't exactly satisfy both constraints at
  once) — a real determinism and performance question for a codebase that
  already hit real O(cities²)-per-quarter performance limits once
  (`GravityDemandModel`'s own M21 fix).
- **It carries exactly the same "absolute magnitude" risk class** the two
  failed attempts just demonstrated, at a larger scale — the whole demand
  baseline moves at once, not one term inside it.

Not recommended for this round. Worth revisiting only if the smaller path
below is tried, measured, and found insufficient.

### Path B — hub-quality as a real input to hub *selection* (recommended)

Keep `ForPair`'s own O&D formula untouched. Add a new, genuinely small
concept: **hub quality**, distinct from `DemandWeight`, representing "how
good a real connecting point this city is" — independent of whether many
people actually want to visit it. Atlanta and Dubai can be modest
destinations with excellent hub quality; a scenic but remote city can be
the reverse.

**Where it plugs in — the contained surface this whole document argues
for:**

1. **`NetworkPlanner.FindBestHub`** (`NetworkPlanner.cs:80-110`) — today
   ranks candidate hubs purely by `detourKm`. Add hub quality as a real
   factor in that ranking (e.g., a candidate hub with meaningfully better
   quality wins even at a modest detour penalty, within a bounded
   tolerance — not "quality always wins regardless of geography," which
   would produce absurd routings). This changes *which* connections form
   when a real airline's network offers more than one path, and does
   **not** touch `ForPair` at all — the O&D demand for the pair being
   routed is exactly what it is today.
2. **Optionally, a bounded connecting-capacity effect**: real hubs don't
   just win the routing choice, they also handle *more* connecting volume
   than their geography alone would suggest (this is the literal Atlanta/
   Dubai point — the airport is *built* for it). If wanted, this could
   show up as a capped, per-itinerary effective-capacity or
   effective-demand bonus specifically for one-stop itineraries through a
   high-hub-quality city — deliberately **capped at a fixed ceiling** (a
   flat multiplier bound, e.g. never more than 1.5-2×), not an unbounded
   formula term, precisely because an unbounded term is what broke both
   earlier attempts. This half is a real design choice with its own risk,
   and should be built and measured as a *second*, separable stage after
   step 1 alone is shipped and measured — not bundled in from the start.

**Data needed:** a new, real per-city field (own `HubQuality` or similar),
authored the same way `AutoCapacity`/`GateCapacity` already are — not
derived from `IsGateway` (that flag is about *intercontinental chokepoint*
status, a different real concept — Atlanta isn't one) and not derived from
`DemandWeight` (conflating "attractive to visit" with "good at connecting"
is the exact problem being fixed). A reasonable starting scale: something
comparable to `DemandWeight`'s own range (roughly 1-100), authored by
inspection against real-world hub airports the same way every other city
field in `data/cities.json` already was.

## Feasibility assessment

**Path B, step 1 (hub-quality-aware selection only):**
- **Risk: low.** Touches one already-well-tested method
  (`NetworkPlanner.FindBestHubTests`-equivalent coverage already exists),
  never touches the demand baseline, and — because it only changes
  *routing choice* among itineraries that already need a connection — the
  blast radius is naturally bounded by how many real games actually have
  more than one plausible hub candidate for a given pair.
- **Effort: small-to-moderate.** New data field (with `CityCatalog`
  validation, matching every existing field's own pattern), one method's
  own ranking logic reworked, new tests for the ranking change, a real
  300-game sweep to confirm no regression — comparable in scope to a
  single one of this session's own already-shipped milestones (M45/M46's
  own scope is a reasonable reference point).
- **Determinism:** clean — a pure function of already-loaded city data,
  same pattern every other tunable constant in this codebase already
  follows.

**Path B, step 2 (bounded connecting-capacity/demand bonus), if pursued
after step 1 is measured:**
- **Risk: moderate.** Any new multiplicative term needs the same
  "absolute magnitude" scrutiny that caught both earlier failures — a
  *capped* bonus is far safer than an unbounded formula change, but still
  needs real verification (both relative ranking *and* absolute scale)
  before it ever reaches a real call site, exactly the gap that let the
  exponent-discount attempt ship to a real sweep before its magnitude
  problem was caught.
- **Effort: small**, additive to step 1's own infrastructure.

**Path A (full trip-generation/distribution replacement):**
- **Risk: high.** Full model replacement, fresh calibration, real
  determinism/performance design work for the distribution step, and the
  largest possible surface for exactly the "unexpected absolute-magnitude
  blowup" class of failure this document's own evidence is built from.
- **Effort: large.** Comparable to a genuinely new subsystem, not a
  milestone-sized round.

## Recommendation

Build Path B, step 1 alone first: a new `HubQuality` city field, wired only
into `NetworkPlanner.FindBestHub`'s own hub-selection ranking, with its own
real 300-game sweep before anything else changes. Measure whether that
alone produces the kind of network topology the user is after (real hubs
attracting real connecting traffic through better routing choices) before
deciding whether step 2's bounded capacity bonus, or Path A's much larger
rearchitecture, is actually needed.

## Open questions for the user

1. Does `HubQuality` need to be a brand-new authored field (most accurate,
   most authoring effort — every one of ~99 cities needs a real,
   inspected value), or would a cheaper starting point be acceptable —
   e.g., deriving it from `DemandWeight` with a manually-authored
   *override list* for the real Atlanta/Dubai-style exceptions (cities
   whose real-world hub role doesn't match their `DemandWeight`)?
2. Should step 1 (routing choice only) ship and be measured on its own
   before step 2 (the bounded capacity bonus) is even designed, as
   recommended above — or is the capacity-bonus half the actually
   important part to the user, worth designing together now?
3. Is Path A (the full trip-generation/distribution rearchitecture) worth
   keeping on the table for later, or should this document's own
   recommendation against it for now be treated as settled?
