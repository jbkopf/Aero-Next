# Game Setup & Initial Conditions Roadmap

**Status: M15–M19 all complete.** Builds on the completed sim
core (`docs/SIM_CORE_ROADMAP.md`, M0–M14, all done). Continues that roadmap's
milestone numbering (M15+) rather than restarting at M0, since this is the
next thing built on the same headless core, not a separate project.

**Scope: AeroNext.Core and AeroNext.Sim only, same as the sim core roadmap.**
No Godot, no UI, no networking. "Ask the player" here means: a pure function
that takes structured input (player count, names, city choices) and returns
validated starting state — `AeroNext.Sim` supplies that input from the
command line or a scripted list, exactly how M5–M14 already simulate turns
without a UI. The eventual Godot client's setup screen calls the same
functions; it doesn't get its own logic.

**Explicitly out of scope, deferred to a later AI-improvement phase:** making
`BaselineAi` play differently per airline (personality, risk tolerance,
randomized choices). That's the *other* half of the "why did all 4 AI
airlines go bankrupt in the same quarter" finding from M13 — identical
starting conditions was the half in scope here; identical decision-making
is a separate, larger problem (retuning or replacing `BaselineAi` itself)
intentionally not touched by this roadmap.

Each milestone lands as its own small commit, with tests alongside the
logic, per CLAUDE.md working preferences. A milestone is done when
`dotnet test` covers it, a coverage check
(`dotnet test --collect:"XPlat Code Coverage"`) has been read and every gap
run to ground, and `AeroNext.Sim` can demonstrate it from the command line.

---

## M15 — Home region model ✅

**The gap this closes:** `Region` already has 5 values (NorthAmerica,
Europe, Asia, SouthAmerica, Africa), and M1's own note already called Africa
"a bridge region" rather than a home region — but nothing in code encodes
that distinction. CLAUDE.md specifies **exactly 4 home regions**, one per
player, with the map partitioning itself unless prevented. That mapping
needs to be a real, queryable thing before setup can assign one region per
airline.

Add a `HomeRegions` concept (`AeroNext.Core.World`) identifying the 4
player-assignable regions (NorthAmerica, Europe, Asia, SouthAmerica) and
excluding Africa, which stays a chokepoint/bridge region — reachable,
containing real chokepoint cities (Cairo, Dakar), but never a player's home
base. Also catalog, per home region, which of its cities are legal starting
cities: **non-gateway cities only.** A region's gateway (JFK, LHR, HND/HKG,
GRU) stays inside M9's contested-negotiation mechanic for everyone,
including the local airline — handing a player their region's flagship hub
for free would cut against "a home region cannot fund a win" by making the
home region's best asset unconditionally free. Concretely, from the
existing `cities.json`:

- NorthAmerica: `ord`, `sfo`, `mia`, `yyz`, `mex`
- Europe: `cdg`, `fra`, `fco`, `mad`, `lis`
- Asia: `sin`, `pvg`, `bkk`, `icn`, `del`
- SouthAmerica: `eze`, `gig`

**Done when:** a query "eligible starting cities for region X" returns the
non-gateway set above (or whatever the checked-in data actually contains —
this must read `cities.json`, not hardcode the list twice), and a query
"is this a home region" correctly excludes Africa. Tests: every home region
has at least 2 eligible non-gateway cities (setup needs real choice, not a
single forced pick); Africa is never returned as a home region or assigned
to a player.

Done. `HomeRegions` (`AeroNext.Core.World`) exposes `All` (the 4 home
regions, in a fixed order — never enum declaration order or dictionary
iteration, per determinism rule #3), `IsHomeRegion`, and
`EligibleStartingCities`, which throws for Africa rather than silently
returning an empty list — a caller asking for Africa's starting cities is
a bug, not a valid empty case. Confirmed against the real dataset: every
home region resolves to at least 2 non-gateway cities (NorthAmerica and
Europe get 5 each, Asia 5, SouthAmerica 2 — the smallest, since `gru` is
South America's only gateway and the dataset only has 3 South American
cities total). 11 new tests, 100% line/branch coverage on
`HomeRegions.cs`; overall project coverage unchanged at 99.75%/99.59%.
Demonstrated in `AeroNext.Sim`.

---

## M16 — Regional economics and region-appropriate aircraft ✅

Two data additions, both needed before M18 can derive anything from a
starting city.

**Regional economic factor.** CLAUDE.md's M1 already treats city
`demandWeight` as a population/GDP proxy; there's no separate per-region
number today. Rather than invent a second, independently-tuned economic
system, derive a region's economic factor from data already in
`cities.json`: sum (or average) `demandWeight` across each home region's
cities, normalize against the global mean. This keeps "starting cash
depends on regional economic factors" honest and data-driven instead of a
second hand-authored table that can drift out of sync with the demand model.

**Region-appropriate starting aircraft.** This surfaces a real gap in
`data/aircraft.json`: every aircraft in the file today is American
(Douglas, Lockheed, Boeing), and at the 1955 campaign start
(`Timeline.StartYear`) only two types even exist yet — DC-3 (1935) and
Constellation (1943), both American. "Primarily European-built aircraft for
European cities" can't be built honestly until a non-American 1955-era
aircraft exists in the catalog.

Add a `Manufacturer` and `ManufacturerRegion` (a `Region`) field to
`AircraftType`. Add real, period-appropriate non-American types available
by 1955:
- **Europe:** Vickers Viscount (British, entered service 1953 — a real
  1955-available short/medium-haul turboprop, comparable class to the
  Constellation).
- **Asia and South America:** no indigenous airliner manufacturer existed
  in either region by 1955 — inventing one would be fabricating history,
  not modeling it. Their starting aircraft comes from the same fallback
  rule described below rather than a hardcoded `ManufacturerRegion =
  NorthAmerica` — it resolves to the Douglas/Lockheed types today only
  because that's genuinely all the 1955 catalog has, which happens to match
  real history (period Asian and South American carriers really did fly
  American equipment). Document this asymmetry here rather than papering
  over it with a fictional manufacturer.
- **NorthAmerica:** unaffected — DC-3/Constellation already fit.

**Query rule, implemented as `RegionalFleet.AppropriateAircraft`:** among
era-available types (reusing `Timeline.AvailableAircraft`, the same check
`QuarterEngine` already applies to mid-campaign orders), prefer types with
`ManufacturerRegion == homeRegion`; if none exist yet, fall back to every
era-available type regardless of manufacturer ("best available import").
Deliberately not hardcoded to NorthAmerica specifically — the fallback
stays correct as the catalog grows (see "Kept in mind" below), rather than
needing revisiting the moment a second manufacturing region gets added.

This milestone adds exactly one non-American type — the minimum needed for
an honest 1955 starting aircraft per home region, not a general catalog
expansion. A full pass covering Soviet, Chinese, and regional-jet
manufacturers (Embraer, Bombardier) belongs to future work — see "Kept in
mind" at the bottom of this document, not this milestone.

**Done when:** every home region has at least one `AvailableFromYear <=
1955` aircraft type queryable as "appropriate" for it (own-manufacturer for
Europe/NorthAmerica, best-available import for Asia/SouthAmerica). Tests:
regional economic factor is computed from `cities.json` (not hardcoded) and
is strictly positive for all 4 home regions; every home region resolves to
at least one 1955-available aircraft type.

Done. `RegionalEconomics.Factor` (`AeroNext.Core.Setup`) deliberately
averages the *same* non-gateway eligible cities `HomeRegions` already
defines, not every city tagged with that `Region` — using the raw `Region`
tag would have let remote, near-zero-demand chokepoints (Anchorage,
Honolulu, Wake, Gander — all tagged `NorthAmerica`) drag that region's
average down for reasons that have nothing to do with economic strength.
Measured against the real dataset: NorthAmerica 0.948, Europe 0.963, Asia
1.143, SouthAmerica 0.865 — Asia comes out strongest (its 5 eligible cities
are all genuinely high-demand: Shanghai, Singapore, Seoul, Bangkok, Delhi),
SouthAmerica weakest (only 2 eligible cities, the smallest set of any home
region). Not tuned to produce this ordering — it's what the existing city
data actually says once chokepoint noise is excluded.

`AircraftType` gained `Manufacturer` (string) and `ManufacturerRegion`
(`Region?`), both defaulting empty/null like every other generously-
defaulted field on this record. `data/aircraft.json` now tags all 5
existing types (`Douglas`/`Lockheed`/`Boeing`, all `NorthAmerica`) plus the
new Vickers Viscount (`Europe`, 1953, 48 seats, 2,700 km range — a real
period turboprop, deliberately shorter-range than the Constellation since
it's a different, regional aircraft class, not a "later therefore better"
upgrade). `RegionalFleet.AppropriateAircraft` resolves Europe to
`[Viscount]` and Asia/SouthAmerica to all 3 available types
(`[DC-3, Constellation, Viscount]`, since none is home-manufactured) at
1955 — confirmed directly via `AeroNext.Sim`.

**A real regression risk, checked before finalizing, not assumed away:**
inserting the Viscount between the Constellation and 707 by
`AvailableFromYear` broke `AircraftCatalogTests`' existing "later aircraft
have greater range" assertion — a legitimate failure, not a test bug, since
a regional turboprop introduced after a long-haul flagship genuinely does
have less range. Fixed by narrowing that test to the long-haul lineage
specifically (`l049`/`707`/`747`/`777`), which is the actual invariant
CLAUDE.md's chokepoint-obsolescence story depends on — documented inline in
the test rather than silently relaxed. Separately confirmed the M13
244-quarter campaign's cash trajectory and bankruptcy quarter are
byte-identical to what M13 already recorded: `BaselineAi` always picks the
longest-range *available* aircraft, and the Viscount (2,700 km) never beats
the Constellation (4,200 km) while both are on offer, so it's never
actually chosen — only the printed "N aircraft type(s) available" count at
each checkpoint changed, which is expected and correct.

230 tests total (16 new), 100% line/branch coverage on every file this
milestone touched; overall project coverage improved slightly to
99.76%/99.60%. Two small pre-existing gaps outside this milestone's scope
(`BriefingGenerator.cs` at ~98%, `QuarterOrder.cs`'s abstract marker record
showing 0% on a line with no real logic) predate M16 and weren't chased
here. Demonstrated in `AeroNext.Sim`.

---

## M17 — Player setup flow: count, names, starting cities ✅

The actual "ask the player" sequence, as a pure function over structured
input (no console I/O in `AeroNext.Core` — `AeroNext.Sim` supplies the
answers, per this roadmap's scope note above).

1. **Human player count, 0–4.** Validate the range; reject anything else.
2. **For each human seat:** airline name (any non-empty string, unique
   across all 4 airlines including AI-generated ones) and a starting city,
   chosen from M15's per-region eligible set. **A city's region is claimed
   exclusively** — once one player (human or AI) has picked a city in a
   region, no other airline may pick a city in that region. This is the
   direct implementation of "four home regions... one per player"; with
   always exactly 4 airlines and exactly 4 home regions, the mapping is 1:1
   by construction.
3. **Remaining seats (4 minus human count) are AI-controlled.** Each gets an
   auto-generated name and is assigned one of the still-unclaimed home
   regions, sorted by region name (never dictionary iteration order, per
   CLAUDE.md's determinism rules) so the assignment order itself is fixed.
   **Within its assigned region, the AI's starting city is a specific,
   deterministic pick** — the eligible city with the highest
   `demandWeight`, ties broken by city id ascending — not merely "one of"
   the eligible set. Every AI seat gets exactly one region, and every
   region resolves to exactly one city, so **no two airlines — human or
   AI — ever share a starting city**: uniqueness falls directly out of
   region-exclusivity (step 2) plus this fixed per-region pick, rather
   than needing a separate uniqueness check bolted on afterward. Same
   seed/same human choices must always produce the same AI seats.

**Done when:** all four combinations at the boundary are covered —
0 humans (all 4 seats deterministically AI-assigned), 4 humans (no AI seats,
all regions player-claimed), and the two asymmetric cases in between.
Tests: player count outside 0–4 is rejected; two humans claiming the same
region (directly or via the same city) is rejected; duplicate airline names
are rejected; AI seat assignment is deterministic across repeated runs with
identical human input; **all 4 assigned starting cities are pairwise
distinct in every tested configuration**, including 0-human (an all-AI
game must not silently collide two seats onto the same city).

Done. `GameSetup.CreateSeats` (`AeroNext.Core.Setup`) takes a list of
`HumanPlayerChoice` (airline name + starting city id) — its `Count` *is*
the human player count, so there's no separate parameter that could
disagree with it; validated to reject more than 4. Returns 4
`AirlineSeat` records (name, `IsHuman`, `HomeRegion`, `StartingCity`), one
per home region, sorted by region name (`Asia`, `Europe`, `NorthAmerica`,
`SouthAmerica` — ordinal string order, not `Region`'s enum declaration
order) rather than any dictionary iteration order, per determinism rule
#3. AI seats are named `"{Region} Air"` and take the highest-`demandWeight`
eligible city in their assigned region — confirmed against real data:
Chicago (NorthAmerica), Paris (Europe), Shanghai (Asia), Rio de Janeiro
(SouthAmerica).

Validation covers every failure mode the "Done when" lists, plus two not
explicitly named there but caught while writing the tests: submitting the
exact same city twice (a stricter case of "same region," same rejection
path) and a human explicitly naming their airline the same string an AI
seat would auto-generate (e.g. `"NorthAmerica Air"`), which would otherwise
slip past the human-vs-human duplicate-name check and only collide once
the AI fill-in pass ran. 22 new tests, 100% line/branch coverage on
`GameSetup.cs`; overall project coverage improved slightly to
99.77%/99.63%. Demonstrated in `AeroNext.Sim` across the 0/1/4-human
configurations plus one deliberate rejection (two humans both reaching for
NorthAmerica).

---

## M18 — Deriving starting conditions from starting city ✅

The core of this roadmap: turn a validated (region, city) pair from M17
into real starting state, using M16's data.

- **Starting cash:** a base amount scaled by M16's regional economic
  factor, so a stronger-demand home region starts richer — CLAUDE.md's
  "population/wealth" framing applied at setup, not just to route revenue.
- **Starting aircraft:** a small fixed quantity (exact count set during
  implementation, tuned like M11's production rates were — enough for one
  real route, not a fleet) of the region-appropriate type from M16,
  respecting `AvailableFromYear` the same way `QuarterEngine` already
  enforces it for orders placed mid-campaign.
- **Guaranteed turn-1 route:** the requirement is "able to open at least
  one route with the starting allotment in the first turn," not a new
  reserved-slot object. Since only *gateway* cities are gated by M9's
  contested capacity, and M15 already restricts starting cities to
  non-gateway ones, this reduces to a data guarantee: every eligible
  starting city must have at least one other city in the same home region
  reachable within the starting aircraft's `RangeKm` and with positive
  gravity demand between them (checked against the real `GravityDemandModel`
  and `cities.json`, not assumed). If any eligible city fails this check,
  it's either dropped from M15's eligible-city list or paired with a
  longer-range starting aircraft — resolved with real data during this
  milestone, not guessed here.

**Done when:** for every (home region, eligible starting city) pair, the
derived starting state — cash, aircraft, and at least one in-range
same-region neighbor — is internally consistent, and a `RouteOrder` from
the starting city to that neighbor, using the starting aircraft, is
accepted (not rejected for range, era, or budget) by `QuarterEngine` on
quarter 1. Tests: this exact check, run for every eligible city in the
data, not just one hand-picked example.

Done. `StartingConditionsResolver.Resolve` (`AeroNext.Core.Setup`) returns
an `AirlineStartingConditions` (cash, aircraft type, quantity, guaranteed
route destination) for a given `AirlineSeat`. Cash is
`$20,000,000 × RegionalEconomics.Factor` (Asia's Shanghai seat starts at
$22.9M, SouthAmerica's Buenos Aires at $17.3M in the real-data demo — a
direct, honest consequence of M16's measured factors, not tuned per
region). Aircraft quantity is a fixed 2, matching the "enough for one real
route, not a fleet" framing.

**A real finding while implementing "guaranteed turn-1 route":**
`QuarterEngine` turns out to never check gateway/landing-rights capacity
against a `RouteOrder` at all — that's exclusively a `NegotiationOrder`
concern (M9). So M15's "starting cities are non-gateway" restriction isn't
actually load-bearing for whether a route *can* be flown today; it matters
for a different reason — keeping a player's guaranteed, un-contested
assets away from their region's flagship demand magnet, preserving the
competitive tension CLAUDE.md's "a home region cannot fund a win" pillar
depends on. `StartingConditionsResolver` deliberately keeps the guaranteed
neighbor search scoped to `HomeRegions.EligibleStartingCities` (not "any
same-region city") to stay consistent with that reasoning, rather than
relying on the coincidence that `QuarterEngine` doesn't enforce it either.

**Aircraft selection tries candidates smallest-`RangeKm`-first, not just
"the first appropriate one."** A region-appropriate aircraft set can have
more than one option (Asia/SouthAmerica get all 3 available 1955 types);
handing out the cheapest one that can actually reach a real, positive-
demand same-region city is the more defensible "starting fleet" than an
arbitrary pick, and only steps up to a longer-range type if a smaller one
can't reach anywhere — verified directly with two synthetic tests (one
confirming the small-aircraft preference, one confirming the fallback
actually triggers) rather than trusting the real dataset alone to exercise
that branch.

**Full real-data sweep, not sampled:** the "Done when" check runs for
every one of the 17 real eligible starting cities across all 4 home
regions — every single one resolves successfully and its guaranteed
`RouteOrder` is accepted by the real `QuarterEngine` on quarter 1, with no
data gaps needing a fix (the smallest-first search already handled the
one region, Europe, with only a single appropriate aircraft type and no
fallback available).

**Fleet ownership still isn't enforced against route capacity anywhere**
— the same gap M10/M11/M12 each already noted and left open.
`AircraftQuantity` is real, honest data for that future mechanic to
consume; nothing in `QuarterEngine` checks it today, consistent with how
`AircraftType.Cost` has been "real but unenforced" since M11.

258 tests total (6 new), 100% line/branch coverage on `StartingConditions.cs`;
overall project coverage improved slightly to 99.78%/99.64%. Demonstrated
in `AeroNext.Sim`: the 4-human configuration's starting cash, aircraft, and
guaranteed destination, with each guaranteed route order confirmed
"accepted" against the real `QuarterEngine`, not just computed.

---

## M19 — Integration demo and turn-1 validation ✅

Wire M15–M18 into one entry point `AeroNext.Sim` can run end-to-end, and
prove the whole chain with real numbers instead of unit tests in isolation.

**Done when:** `AeroNext.Sim` demonstrates at least three configurations —
0 human players, 1 human + 3 AI, and 4 human players — each producing 4
airlines with distinct home regions, distinct starting cities, region-scaled
starting cash, region-appropriate starting aircraft, and a confirmed
legal quarter-1 `RouteOrder` per airline, fed through the real
`QuarterEngine`/`CompetitiveMarket`/`FinancialResolver` chain (not a
separate mock path) to confirm turn 1 actually resolves cleanly for every
seat, human or AI. This is also where `BaselineAi` gets its first
non-identical inputs (different home regions, different starting fleets),
which should be noted honestly: it will still make structurally identical
*decisions* given similar inputs, since the AI logic itself is out of scope
here — the difference this roadmap produces is in what each airline starts
with, not how it decides.

Done — the roadmap's last milestone, and the smallest change relative to
what it proves, the same shape M14 had for the sim core roadmap. No new
production capability: `RunSetupThroughQuarter1` in `AeroNext.Sim` is pure
orchestration — `GameSetup.CreateSeats` → `StartingConditionsResolver.
Resolve` per seat → a `RouteOrder` through the real `QuarterEngine` per
seat → one `Airline` league through `CompetitiveMarket.Resolve` → one
`FinancialResolver.Resolve` per seat, using each seat's own
`StartingConditions.StartingCash` (not a shared placeholder) as the
opening `FinancialState`. Demonstrated for all three required
configurations plus confirmed identical for the fourth (2 human + 2 AI is
implied by the same code path, not separately special-cased).

**Real, unforced numbers, not tuned to look good:** every seat carries real
passengers on its guaranteed route (2,184–3,744 pax/qtr depending on the
destination's demand) and posts a small negative net income for the
quarter (-$69K to -$146K) — a brand-new route hasn't built up load factor
yet, the same honest "invest ahead of demand" story M12 already told for
its own hub-building demo. Nobody is bankrupt after quarter 1, in any of
the three configurations. This wasn't tuned to come out this way — it's
what `$20M × regional factor` starting cash against one small route's
first-quarter economics actually produces.

**The `BaselineAi` non-differentiation note plays out exactly as
predicted:** the 0-human demo's 4 AI seats now start with genuinely
different cash, aircraft, and destinations (Asia's carries almost double
Europe's home region's cheaper aircraft — real, seat-specific numbers, not
copies of each other for the first time since M8). What they'd each
*decide* to do starting from quarter 2 is untouched by this roadmap, per
the explicit scope decision at the top of this document.

261 tests total (3 new, `GameSetupIntegrationTests`, one per required
configuration via `[Theory]`/`MemberData` rather than three separate
copy-pasted `[Fact]`s), covering the identical resolver chain the
`AeroNext.Sim` demo exercises — the same relationship M14's
`DeterminismRegressionTests` has to its own campaign demo, so a future
refactor that breaks this wiring fails a test, not just an eyeballed
console diff. 100% line/branch coverage maintained; overall project
coverage unchanged at 99.78%/99.64% (no new production code — this
milestone is glue over M15–M18's already fully-covered logic). The same
two small pre-existing gaps noted at M16 (`BriefingGenerator.cs`,
`QuarterOrder.cs`'s empty abstract marker) remain, still out of this
roadmap's scope.

**Roadmap complete.** M15–M19 all done, tested, coverage-checked, and
demonstrated from the command line, per this document's own "done when"
bar at the top.

---

## Kept in mind, not scheduled

- **AI differentiation** — deferred by explicit decision (see scope note
  above). Once this roadmap lands, the 4 airlines will no longer have
  identical starting conditions, but will still run the identical
  `BaselineAi` heuristic — a natural, well-scoped next roadmap.
- **Gateway home-city exception** — M15 deliberately keeps every region's
  gateway inside the ordinary M9 contested mechanic, including for the
  local airline. Revisit only with real evidence this makes early gateway
  negotiation uninteresting, not by default.
- **Godot client setup screen** — reads M17's structured input/output
  shapes directly; no separate setup logic should ever need to exist
  client-side.
- **Broaden the aircraft catalog well beyond M16's minimum.** M16 only adds
  the single non-American type (Vickers Viscount) needed to make Europe's
  1955 starting aircraft honest — it deliberately doesn't attempt full
  historical coverage. A real future pass should expand `data/aircraft.json`
  to most commercially significant airliners actually flown across the
  campaign's 1955–2015 span: American (Douglas, Lockheed, Boeing, McDonnell
  Douglas), European (Airbus, British Aerospace/BAC, in addition to the
  Viscount), Soviet/Russian (Tupolev, Ilyushin — historically dominant
  across the Eastern Bloc and a natural fit if a Soviet-aligned region or
  political-alignment mechanic ever lands), Chinese (COMAC, in the later
  campaign years), and smaller regional-jet manufacturers (Embraer,
  Bombardier) whose short-hop, lower-capacity aircraft would give the
  short/medium-haul end of the fleet — currently just the DC-3 and
  Viscount — much more depth and era coverage. Not scheduled here because
  M16's own "done when" (one honest 1955 aircraft per home region) doesn't
  need it; noted so the eventual catalog expansion isn't forgotten or
  mistaken for out of scope.
