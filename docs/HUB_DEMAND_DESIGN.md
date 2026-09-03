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

### Follow-up refinement: two more specific requirements

After reading a first draft of this document, the user sharpened the request
into two concrete, separate requirements — both incorporated into Path B
below rather than treated as new alternatives:

1. **Hub cities need *innate* demand, not just better routing.** The
   motivating example: someone in Buffalo who wants to fly to Miami has no
   realistic nonstop option (Buffalo isn't, and shouldn't be, a modeled
   city) — in reality they fly Buffalo→NYC→Miami, and that outward pressure
   from the whole unmodeled catchment around NYC shows up as *extra volume
   on NYC's own routes*, not as a separate Buffalo-Miami pair that doesn't
   exist in this game. That's a real property of the city (NYC pulls this
   demand whether or not any particular airline has built a Buffalo-shaped
   route), not a property of one airline's network — so unlike the
   NetworkPlanner-side ideas above, **this one has to live at the O&D level,
   the same layer `ForPair` already operates at**, not purely inside
   `NetworkPlanner`. See "Path B, revised" below for why that doesn't mean
   repeating either of the two failed formula attempts.
2. **An airline should favor hubs it already operates, and nearby hubs
   should act as substitutes for each other, not stack.** New York and
   Boston are close enough that a real airline builds around one of them,
   not both — once NYC is established as a hub, Boston's marginal value as
   a *second* hub for the same airline is low, even though Boston is a
   perfectly good city in isolation. This is a network-topology question,
   squarely inside `NetworkPlanner.FindBestHub`'s own selection logic (Path
   B, item 1 above) — but it needs two new ingredients neither of the
   document's original items had: **loyalty** (bias toward a hub the
   airline already routes through) and **substitution** (a hub geographically
   close to one the airline already uses is a worse pick than the same city
   would be in isolation, precisely because it's not adding new
   catchment coverage).

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

### Path B, revised — hub-quality as an input to *selection*, *loyalty*, and a *bounded, additive* O&D bonus (recommended)

Still keeps `ForPair`'s core `weightA × weightB / distance^exponent` shape
untouched — no exponent changes, no proportional-to-neighbor bonus, the two
specific mechanisms already proven wrong. Adds a new, genuinely small
concept: **hub quality**, distinct from `DemandWeight`, representing "how
good a real connecting point this city is" — independent of whether many
people actually want to visit it. Atlanta and Dubai can be modest
destinations with excellent hub quality; a scenic but remote city can be
the reverse. Hub quality now plugs in at three points instead of one:

1. **`NetworkPlanner.FindBestHub`** (`NetworkPlanner.cs:80-110`) — today
   ranks candidate hubs purely by `detourKm`. Add hub quality as a real
   factor in that ranking (e.g., a candidate hub with meaningfully better
   quality wins even at a modest detour penalty, within a bounded
   tolerance — not "quality always wins regardless of geography," which
   would produce absurd routings). Changes *which* connections form when a
   real airline's network offers more than one path; doesn't touch
   `ForPair`.
2. **Hub loyalty + substitution, also inside `FindBestHub`'s ranking**
   (the follow-up refinement's second point): add a bias toward a candidate
   hub the airline *already* routes a meaningful share of its network
   through (loyalty — reflects that a real airline consolidates around
   sunk infrastructure rather than re-optimizing every quarter), and a
   penalty when a candidate hub sits unusually close to a hub the airline
   *already* uses (substitution — Boston loses appeal as a second hub once
   NYC is established, not because Boston got worse, but because it isn't
   adding new catchment). Both terms are airline-specific and itinerary-
   selection-scoped, same contained surface as item 1 — still never touches
   `ForPair` or any other airline's view of the same city. Precise metric
   for "already a hub" (e.g. share of the airline's own routes touching
   that city) and for "how close counts as substitutable" are implementation
   questions, not resolved here.
3. **A bounded, additive innate-hub-demand term inside `ForPair` itself**
   (the follow-up refinement's first point — this is the one piece of Path
   B that *does* touch the O&D layer, deliberately shaped to avoid both
   ways the earlier attempts broke):
   `effectiveWeight(city) = DemandWeight + HubQuality`, used in place of
   `DemandWeight` on whichever side(s) of the product correspond to a city
   with a nonzero `HubQuality`. This is structurally different from both
   failed attempts, not just smaller:
   - Unlike the **additive catchment bonus** (which computed a bonus
     *proportional to the size of nearby cities, divided by the receiving
     city's own distance* — the exact mechanism that made it a *larger
     percentage* boost for small cities), this bonus is a single flat,
     authored number per city, added once, with no dependency on which
     partner city is on the other side of the pair or how far away it is.
     A small city's own weight is never touched by a neighboring hub's
     quality.
   - Unlike the **exponent discount** (a change to the *shape* of distance
     decay, which compounds multiplicatively and produced the measured
     31× world-demand inflation), this is a linear addition to one input
     of a product — its effect on any single pair's demand is bounded by
     a simple ratio (`(weight + HubQuality) / weight`), directly
     computable and boundable in advance by capping `HubQuality`'s own
     authored range, with no power-law surprise.
   - It is **not** a substitute for item 1/2's routing behavior — a hub
     with high `HubQuality` gets a real, if modest, boost to its *own*
     nonstop pairs (representing Buffalo-shaped catchment pressure baked
     into NYC's own O&D numbers), independent of whether any specific
     airline has built a network that routes through it. This is what
     makes it match the Buffalo→NYC→Miami example: the pressure exists in
     the world, not only inside one airline's already-built itineraries.

   Still requires the same real-data magnitude verification this document's
   own evidence insists on before it ever reaches a real call site — the
   claim above is about *why this shape is safer in principle*, not a
   substitute for measuring it. A reasonable starting cap: `HubQuality`
   authored on roughly the same scale as `DemandWeight` (1-100) but with a
   materially lower ceiling in practice (e.g. authored values clustering
   10-30, reserving 100 for `DemandWeight` itself) so a hub's *innate* bonus
   stays a fraction of, not comparable to, its own destination appeal.

**Data needed:** a new, real per-city field (`HubQuality` or similar),
authored the same way `AutoCapacity`/`GateCapacity` already are — not
derived from `IsGateway` (that flag is about *intercontinental chokepoint*
status, a different real concept — Atlanta isn't one) and not derived from
`DemandWeight` (conflating "attractive to visit" with "good at connecting"
is the exact problem being fixed). Authored by inspection against real-world
hub airports the same way every other city field in `data/cities.json`
already was — one field now serves all three plug-in points above (items
1-2 read it as a ranking input, item 3 reads it as an additive bonus).

## Feasibility assessment

**Path B, step 1 (hub-quality-aware selection + loyalty/substitution,
items 1-2 — no `ForPair` change):**
- **Risk: low.** Touches one already-well-tested method
  (`NetworkPlanner.FindBestHubTests`-equivalent coverage already exists),
  never touches the demand baseline, and — because it only changes
  *routing choice* among itineraries that already need a connection — the
  blast radius is naturally bounded by how many real games actually have
  more than one plausible hub candidate for a given pair. Loyalty and
  substitution add two more inputs to the same ranking function, not a
  new subsystem — same risk class as the base selection change.
- **Effort: small-to-moderate.** New data field (with `CityCatalog`
  validation, matching every existing field's own pattern), one method's
  own ranking logic reworked (now three terms: detour, loyalty,
  substitution), new tests for each, a real 300-game sweep to confirm no
  regression — comparable in scope to a single one of this session's own
  already-shipped milestones (M45/M46's own scope is a reasonable
  reference point).
- **Determinism:** clean — a pure function of already-loaded city data
  plus the airline's own already-committed route set, same pattern every
  other tunable constant in this codebase already follows.

**Path B, step 2 (bounded, additive innate-hub-demand term inside
`ForPair`, item 3), if pursued after step 1 is measured:**
- **Risk: moderate, not low** — this is the one piece that touches the
  demand baseline, so it inherits real scrutiny from both earlier
  failures even though its shape (flat additive bonus, not proportional-
  to-neighbor, not exponent-based) avoids the *specific* mechanisms that
  broke each of them. "Structurally safer" is an argument for trying this
  shape, not a substitute for verifying it — needs both relative-ranking
  and absolute-magnitude checks against real data before it reaches a
  real call site, exactly the step that was skipped for the exponent
  attempt until the sweep itself caught it.
- **Effort: small**, additive to step 1's own `HubQuality` field — no new
  data needed, just a new read site inside `ForPair`.

**Path A (full trip-generation/distribution replacement):**
- **Risk: high.** Full model replacement, fresh calibration, real
  determinism/performance design work for the distribution step, and the
  largest possible surface for exactly the "unexpected absolute-magnitude
  blowup" class of failure this document's own evidence is built from.
- **Effort: large.** Comparable to a genuinely new subsystem, not a
  milestone-sized round.

## Recommendation

Build Path B, step 1 alone first: a new `HubQuality` city field, wired into
`NetworkPlanner.FindBestHub`'s ranking for detour, loyalty, and
substitution (items 1-2) — no `ForPair` change yet — with its own real
300-game sweep before anything else changes. Measure whether that alone
produces the kind of network topology the user is after (real hubs
attracting real connecting traffic, airlines consolidating around one hub
per catchment region instead of stacking adjacent cities) before adding
step 2's innate-demand bonus inside `ForPair`, which needs its own separate
measurement precisely because it's the one piece touching the layer that
broke twice already. Path A stays off the table unless step 1 and step 2
together are measured and still found insufficient.

## Open questions for the user

1. Does `HubQuality` need to be a brand-new authored field (most accurate,
   most authoring effort — every one of ~99 cities needs a real,
   inspected value), or would a cheaper starting point be acceptable —
   e.g., deriving it from `DemandWeight` with a manually-authored
   *override list* for the real Atlanta/Dubai-style exceptions (cities
   whose real-world hub role doesn't match their `DemandWeight`)?
2. Should step 1 (selection + loyalty + substitution, no `ForPair` change)
   ship and be measured on its own before step 2 (the innate-demand bonus
   inside `ForPair`) is even designed, as recommended above — or is the
   innate-demand half the actually important part to the user (it's the
   piece that directly represents the Buffalo→NYC→Miami pressure), worth
   designing together now rather than deferring?
3. For loyalty/substitution: is "share of the airline's own existing
   routes touching this city" a reasonable proxy for "already treats this
   as a hub," or does the user have a more specific real-world definition
   in mind (e.g. a minimum route count, or an explicit hub designation the
   player/AI sets rather than one inferred from route counts)?
4. Is Path A (the full trip-generation/distribution rearchitecture) worth
   keeping on the table for later, or should this document's own
   recommendation against it for now be treated as settled?
