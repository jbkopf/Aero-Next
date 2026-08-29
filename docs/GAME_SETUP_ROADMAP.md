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

**Updated after the city-expansion work** (see "Kept in mind" below):
SouthAmerica's 2-eligible-city gap was real and got closed directly —
Santiago, Bogota, and Lima bring every home region to parity at 5 eligible
non-gateway cities each, confirmed by a new test
(`EligibleStartingCities_EveryHomeRegion_HasParity_FiveEligibleCities`).
The mechanism this milestone built (throwing for Africa, reading straight
from `cities.json`) needed no changes at all — it was already
data-driven exactly as designed.

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
Measured against the real dataset (at the time): NorthAmerica 0.948, Europe
0.963, Asia 1.143, SouthAmerica 0.865 — Asia comes out strongest (its 5
eligible cities are all genuinely high-demand: Shanghai, Singapore, Seoul,
Bangkok, Delhi), SouthAmerica weakest (only 2 eligible cities, the smallest
set of any home region). Not tuned to produce this ordering — it's what
the existing city data actually said once chokepoint noise is excluded.

**Updated after the SouthAmerica city expansion** (see "Kept in mind"
below): since the factor normalizes each region's average against the
*global* average across all 4 regions' eligible cities, adding cities to
just one region shifts every region's factor, not only SouthAmerica's —
re-measured directly: NorthAmerica 0.977, Europe 0.992, Asia 1.178,
SouthAmerica 0.853. SouthAmerica's own factor moved only slightly (0.865 →
0.853, since Santiago/Bogota/Lima's demand weights are close to its
existing average), but the other three regions all rose, because
SouthAmerica's new cities pulled the global average down. This ripple
effect — a single region's data change moving every region's number — is
inherent to how the factor is defined, not a bug; documented here so the
figures above are read as "as measured at the time," not permanently
pinned constants.

**Updated again after the Europe city expansion:** the same ripple, one
more round — NorthAmerica 0.988, Europe 0.976, Asia 1.192, SouthAmerica
0.863. Europe's own factor actually *fell* slightly (0.992 → 0.976)
despite gaining 4 new eligible cities, because Munich/Istanbul/Barcelona/
Zurich's weights (65/70/55/50) average below Europe's existing set —
adding cities to a region doesn't necessarily raise that region's own
factor, only the global denominator every region is measured against. The
other three regions all rose slightly for exactly that reason.

**Updated a third time after the Asia city expansion:** NorthAmerica
0.969, Europe 0.957, Asia 1.131, SouthAmerica 0.846 — every region's
factor *fell*, including Asia's own, worked out by hand to confirm before
writing it down rather than just eyeballing the direction. Asia's own
average dropped from 76.0 to 73.5 (the 5 new cities average 71 — Dubai
85, Mumbai 65, Kuala Lumpur/Taipei 55 each, Beijing's 95 not enough to
offset the other four), which explains Asia's own factor falling. The
other three regions' *own* averages didn't move at all (no new
NorthAmerica/Europe/SouthAmerica cities this round) — their factors fell
because the *global* denominator rose, from about 63.8 to about 65.0:
Asia's 5 new cities average 71, below Asia's own prior average but still
above the old global average, so folding them in pulls the shared
denominator up even as it pulls Asia's own numerator down. Both of those
are the same mechanism as the Europe round's note (a region's own factor
and the global denominator can move independently, and even in opposite
directions, depending on how the new cities compare to each pool they
join) — just resolved with the opposite sign this time, and worth showing
the arithmetic for since eyeballing "more/fewer cities" doesn't reliably
predict which way either number moves.

**Updated a fourth time after the NorthAmerica city expansion:**
NorthAmerica 0.979, Europe 0.959, Asia 1.133, SouthAmerica 0.848 — every
region's factor *rose* this time, the mirror image of the Asia round,
verified with the same arithmetic discipline. NorthAmerica's own average
barely moved (63.0 → 63.5 across its now-10-city set — the 5 new cities
average 64: Atlanta's 90 pulling up against Denver/Houston/Vancouver's
50s-55 pulling down). The *global* denominator fell slightly (about
65.0 → 64.9), because NorthAmerica's new cities' average (64) sits just
below the prior global average, the same mechanism as the Europe round
but in the direction that raises everyone else's factor rather than
lowering it: numerator flat, denominator down, factor up for Europe,
Asia, and SouthAmerica, plus a small direct boost for NorthAmerica's own
factor from both effects at once.

**Updated a fifth time after the Aerobiz Supersonic cross-reference
round:** NorthAmerica 0.964, Europe 0.986, Asia 1.102, SouthAmerica
0.871 — a genuinely mixed result this time (NA and Asia fell, Europe and
SouthAmerica rose), the first round to touch three regions at once
instead of one, verified region by region rather than assumed to follow
any single pattern. NorthAmerica's own average dropped meaningfully (63.5
→ 60.83 across 12 cities — Washington (60) and Havana (35) average only
47.5, well below its prior set). Europe's own average barely moved (62.21
→ 62.26 — Moscow (70) and Berlin (55) average 62.5, almost exactly
matching Europe's existing average). Asia's own average dropped (73.5 →
69.58 across 12 eligible cities — Manila (55) and Tehran (45) average
only 50; Sydney doesn't count toward this at all, since it's a gateway,
not an eligible city). SouthAmerica, untouched this round, kept its own
average fixed while the *global* denominator fell (64.87 → 63.12,
dragged down mainly by Havana's low weight) — numerator flat, denominator
down, so SouthAmerica's factor rose, same mechanism as always, just
purely a spectator this time.

**Updated a sixth time after the Aerobiz Supersonic major-tier round:**
NorthAmerica 1.011, Europe 0.951, Asia 1.060, SouthAmerica 0.943 — the
biggest single ripple in this series (24 new cities across four regions
at once), region-by-region arithmetic verified against every measured
factor before writing it down, same discipline as every round before it.
NorthAmerica's own average fell slightly too (60.83 → 59.0 across 15
cities — Seattle/Philadelphia/Phoenix average 51.67, below its prior
60.83 set), but the global denominator fell further still (63.12 →
58.33), so NorthAmerica's factor still rose (1.011) despite its own
average dropping — the denominator moved more.
Europe's own average fell more sharply (62.26 → 55.47 across 21 cities —
the 10 new cities average only 48.0, well below Europe's existing set),
enough to push its factor down (0.986 → 0.951) even against the falling
denominator. Asia's own average also fell (69.58 → 61.84 across 19
cities — the 7 new cities average 48.57) but by less than the
denominator fell, so its factor still rose slightly (1.102 → 1.060 — a
fall in absolute terms, but worth being precise: both numerator and
denominator fell here, and it's their *ratio* that matters, not either
number's direction alone). SouthAmerica, untouched again, simply
inherited the falling denominator and rose (0.871 → 0.943). **Africa's
new cities (Lagos, Nairobi, Algiers, Addis Ababa) don't appear anywhere
in this arithmetic at all** — `RegionalEconomics.Factor` only ever
considers `HomeRegions.All`, and Africa isn't a home region (M15's
bridge-region design), so its cities are real data for `BaselineAi`'s
global demand search but structurally invisible to this specific
calculation. First time this roadmap's had cities to actually
demonstrate that boundary with, rather than just state it.

**Updated a seventh time after the Aerobiz Supersonic minor-tier round:**
NorthAmerica 1.121, Europe 1.031, Asia 0.903, SouthAmerica 1.085 — **the
first time any region's factor has dropped below 1.0**, computed directly
from `cities.json` rather than assumed to follow the previous round's
direction. The global denominator fell sharply (58.33 → 50.71 across 77
eligible cities, up from 60), driven almost entirely by Asia's 13 new
entries (Perth, Fukuoka, Tashkent, Islamabad, Adelaide, Cebu, Sapporo,
Nandi, Guam, Khabarovsk, Noumea, Papeete, Saipan — Pacific/regional
stepping-stones, not demand centers), which average only 22.31, pulling
Asia's own average down hard (61.84 → 45.78 across 32 cities) — enough
that even though the global denominator fell too, Asia's own average fell
further in relative terms, so its factor crossed under 1.0 for the first
time this series. NorthAmerica (avg 59.0 → 56.88 across 16 cities, adding
only Kingston at weight 25) and Europe (avg 55.47 → 52.29 across 24
cities, adding Nice/Minsk/Rostov averaging 30) both saw their own averages
fall too, but by less than the shared denominator fell, so both factors
rose (1.011 → 1.121, 0.951 → 1.031). SouthAmerica, untouched again, simply
inherited the falling denominator and rose furthest in relative terms
(0.943 → 1.085). **Africa's two new cities this round (Tunis, Tripoli)
are, again, invisible to this calculation** — same structural boundary as
the major-tier round, holding steady as Africa keeps gaining cities every
round without ever entering `HomeRegions.All`.

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
`$20,000,000 × RegionalEconomics.Factor` (at the time: Asia's Shanghai seat
started at $22.9M, SouthAmerica's Buenos Aires at $17.3M in the real-data
demo — a direct, honest consequence of M16's measured factors, not tuned
per region; see M16's own update note for how these numbers moved after
the SouthAmerica city expansion). Aircraft quantity is a fixed 2, matching
the "enough for one real route, not a fleet" framing.

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
every real eligible starting city across all 4 home regions (17 at the
time; 77 now, after seven expansion rounds (SouthAmerica, Europe, Asia,
NorthAmerica, the Aerobiz Supersonic cross-reference, and its major- and
minor-tier follow-ups) — the test itself iterates
`HomeRegions.EligibleStartingCities` directly rather than a hardcoded
count, so it picked up 42 of the 47 non-Africa new cities automatically
across the first six rounds with no test changes needed (Sydney is the
one exception — tagged a gateway specifically so it's excluded from this
sweep, after it failed the check as a non-gateway city), then all 17 of
the minor-tier round's non-Africa new cities with zero further exceptions
(none of them are gateways) — 59 of 64 non-Africa new cities picked up
automatically overall. The 6 Africa additions across the major- and
minor-tier rounds are the other explained gap, since Africa isn't a home
region at all and `EligibleStartingCities` throws for it by design — see
this document's own "Kept in mind" entries for all three)) — every single
one resolves successfully and its guaranteed `RouteOrder` is accepted by
the
real `QuarterEngine` on quarter
1, with no data gaps needing a fix (the smallest-first search already
handled the one region, Europe, with only a single appropriate aircraft
type and no fallback available).

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
quarter — a brand-new route hasn't built up load factor yet, the same
honest "invest ahead of demand" story M12 already told for its own
hub-building demo. Nobody is bankrupt after quarter 1, in any of the three
configurations. This wasn't tuned to come out this way — it's what `$20M ×
regional factor` starting cash against one small route's first-quarter
economics actually produces.

**Re-measured twice since**, both times outside M15–M19's own milestones:
first when `AircraftType.CostPerAvailableSeatKm` landed (net income moved
from -$69K/-$146K to -$159K/-$287K — every seat's aircraft now costs what
it actually costs to fly, not one flat global rate), then again when the
round-2 pre-1965 aircraft expansion added smaller, cheaper-to-fly options
(Convair 340, Ilyushin Il-14) that `StartingConditionsResolver`'s
smallest-range-first search now picks over the DC-3 for several seats,
moving net income to roughly -$159K/-$234K. Neither change is a
regression — the guaranteed-route mechanic is correctly picking up more
realistic economics and a richer aircraft catalog as later milestones add
them. Still nobody bankrupt after one quarter in any configuration; the
finding this section exists to demonstrate is unchanged.

**A third re-measurement, after the SouthAmerica city expansion, landed
differently than the first two:** the 0-human and 1-human demo seats'
net income figures are byte-identical to before (still -$159K/-$234K) —
each of those seats' own starting city (the highest-demandWeight eligible
city per region, e.g. Rio de Janeiro for SouthAmerica) already had a
same-region neighbor within range before the expansion, so adding more
distant options didn't change what `StartingConditionsResolver` picked
for them. Only starting *cash* moved for those seats, following M16's
factor shift. The 4-human demo's hardcoded "Continental Wings" seat
(started at Buenos Aires specifically) did pick a new destination —
Santiago instead of Rio de Janeiro — since Buenos Aires gained real new
same-region neighbors the smallest-range-first search now considers; its
net income (-$224,600.84) still falls inside the already-quoted range, so
the range claim above holds without needing to widen.

**A fourth re-measurement, after the Europe city expansion, did widen the
range slightly:** Europe's own seat (starting at London, per M15's
highest-demandWeight pick — unaffected by the new cities, since none of
them out-weigh London) now finds a *closer* guaranteed destination among
the new options and picks it over Rome, moving its net income from
-$158,794.70 to -$167,863.85. Asia, NorthAmerica, and SouthAmerica's demo
seats are all byte-identical to the previous measurement (none of their
own starting cities' guaranteed-route picks were affected by cities added
to Europe). The range is now roughly **-$168K/-$234K**.

**A fifth re-measurement, after the Asia city expansion:** Asia's own AI
seat now starts in Beijing rather than Shanghai (per M15's highest-
demandWeight pick — Beijing's 95 edges out Shanghai's 90), moving net
income from -$217,927.55 to -$219,670.15 — inside the already-quoted
range, so it still holds without widening. Europe, NorthAmerica, and
SouthAmerica's demo seats are all byte-identical to the previous
measurement.

**A sixth re-measurement, after the NorthAmerica city expansion:**
NorthAmerica's own AI seat now starts in Atlanta rather than Chicago (per
M15's highest-demandWeight pick — Atlanta's 90 comfortably beats
Chicago's 70), and both the 1-human and 4-human demo's hardcoded
NorthAmerica seats (Toronto and Chicago respectively — starting cities
unaffected, since neither is the new highest-weight pick) found different,
cheaper guaranteed destinations among the newly-expanded eligible set.
All of these land at -$217K to -$233K, still inside the already-quoted
-$168K/-$234K range, so it holds without widening a fourth time. Europe
and SouthAmerica's demo seats are byte-identical to the previous
measurement.

**A seventh re-measurement, after the Aerobiz Supersonic cross-reference
round: every demo seat's net income is byte-identical to the previous
measurement, all four regions.** None of the seven new cities (Moscow,
Berlin, Washington, Havana, Tehran, Manila, Sydney) are close enough to
any of the demo's specific starting cities (Beijing/Shanghai, London,
Atlanta/Toronto/Chicago, Rio/Buenos Aires) to change what
`StartingConditionsResolver`'s smallest-range-first search picks for
them — only starting *cash* moved, following M16's factor shifts. The
-$168K/-$234K range holds unchanged for the first time in this series.

**An eighth re-measurement, after the major-tier round: the range
actually widened this time.** Europe's own AI seat (still starting at
London) now finds a real, close new destination among the 10 new
European cities — net income moved from -$167,863.85 to -$121,710.68, a
meaningfully smaller loss, becoming the new least-negative figure in the
set. Asia, NorthAmerica, and SouthAmerica's demo seats are all
byte-identical to the previous measurement. The range is now roughly
**-$122K/-$234K**.

**A ninth re-measurement, after the minor-tier round: only Asia's demo
seats moved.** Asia's own AI seat (still starting at Beijing — none of
the 13 new Asia cities out-weigh its 95) now finds a closer, cheaper
guaranteed destination among them (Fukuoka and several other new entries
sit much nearer Beijing than whatever the smallest-range-first search
used before), moving net income from -$219,670.15 to -$231,054.25 — still
inside the already-quoted -$122K/-$234K range, so it holds without
widening. Europe, NorthAmerica, and SouthAmerica's demo seats are all
byte-identical to the previous measurement — confirmed by temporarily
reverting `cities.json` to its pre-round state and re-running the demo
side by side, not just eyeballed. Starting *cash* moved for every seat
regardless, following M16's factor shifts, same as every round before it.

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
- **Route entry should require an available slot, and must grow outward
  from a city the airline already serves — noted for the
  AI-differentiation work above, not implemented now, applies to human and
  AI players identically (confirmed, not just an open question anymore).**
  Two rules, both closing gaps in today's `QuarterEngine`:

  **1. Slot requirement.** M18 already found the gap: `QuarterEngine`
  never checks landing-rights slot ownership against a `RouteOrder` at
  all today — only `NegotiationOrder` touches `LandingRightsBoard`, so
  winning a gateway negotiation currently has no mechanical effect on
  what routes can actually be flown there. Closing it means: (a)
  `QuarterEngine` gains a new rejection reason (mirroring how range/era/
  budget are each rejected and reported separately by `BriefingGenerator`
  today) for a `RouteOrder` touching a gateway city the airline holds no
  slot at; (b) `BaselineAi`'s route search
  (`FindBestUnservedPair`/`FindBestUnservedGateway`) has to become
  slot-aware — it doesn't receive `LandingRightsBoard` state at all right
  now, so this means threading that board through `DecideOrders`, not
  just changing what it does with cities it can already see.

  **2. Network contiguity — rules settled, not just flagged as open
  questions anymore:**

  - The constrained field is specifically `RouteOrder.Origin`, not "either
    endpoint" — deliberate, not an arbitrary pick: origin is where growth
    has to reach *outward from*, `Destination` is deliberately unconstrained
    (has to be, or an airline could never open anywhere new at all).
  - The airline's **home city is always a valid origin, every quarter,
    permanently** — not just exempted for the first-ever route. This is
    what actually resolves the gap flagged earlier: since home has to stay
    checkable forever, not just at setup, the airline's home-city identity
    needs to become a real, persistent field (on `Airline` or wherever it
    ends up living), not something `StartingConditionsResolver` consumes
    once and forgets. A concrete consequence worth naming: this makes home
    city permanently re-enterable even if a later quarter's route churn
    ever dropped the airline's own home-city route entirely — by design,
    not as an edge-case bug.
  - The valid-origin set for a quarter is **the cities touched by routes
    active at the start of that quarter** (plus home city, always) — a new
    route order can never chain off another new route order placed in the
    same quarter. Confirms the recommended default: matches
    `QuarterEngine.Cost`'s existing "evaluated against the quarter's
    starting routes, not other orders in the same batch" precedent exactly,
    keeping order resolution independent of submission order within a
    quarter, which the simultaneous-resolution architecture depends on
    throughout.

  Still a genuinely bigger `BaselineAi` change than it looks, unresolved by
  the above: `FindBestUnservedPair` today searches every city pair in the
  world for the single globally-best move, completely independent of the
  airline's own network — it needs to search only pairs whose `Origin` is
  in the valid-origin set defined above, which also means `DecideOrders`
  needs to know the airline's home city, not just its current routes.
- **Gateway home-city exception** — M15 deliberately keeps every region's
  gateway inside the ordinary M9 contested mechanic, including for the
  local airline. Revisit only with real evidence this makes early gateway
  negotiation uninteresting, not by default.
- **Godot client setup screen** — reads M17's structured input/output
  shapes directly; no separate setup logic should ever need to exist
  client-side.
- **Broaden the aircraft catalog well beyond M16's minimum — done, outside
  this roadmap's own milestones.** `data/aircraft.json` grew from 6 to 14
  types: Boeing 737 and Bombardier CRJ100 round out NorthAmerica; Airbus
  A300 and A320 give Europe a real mainline lineage beyond the Viscount;
  Embraer EMB-120 Brasilia (1985) and COMAC ARJ21 (2008) give SouthAmerica
  and Asia their own manufactured types for the first time — closing the
  exact gap M16 could only leave open at the 1955 start (`RegionalFleetTests`
  now locks in both, once each type's era arrives); Tupolev Tu-104 and
  Ilyushin Il-62 add real, era-available Soviet aircraft with
  `ManufacturerRegion` deliberately left `null` — the USSR doesn't map
  cleanly onto this game's 4-home-region model, and no region should get
  them handed over as a "starting" type by fiat, so they're flyable by
  anyone once available but never anyone's preferred pick. Chinese C919
  (real-world 2017+) was deliberately left out — later than the campaign's
  2015 end, so it would sit permanently unreachable.

  Verified byte-identical against every previously-recorded M13 campaign
  figure before considering this done: Il-62's 10,000 km range technically
  exceeds the Boeing 707's 9,800 km from 1967–1970, which could have
  changed which aircraft `BaselineAi` picks as "best available" during
  that window — checked directly rather than assumed safe, and it turns
  out not to matter, because M13's own campaign has all four airlines
  already bankrupt (and frozen) by quarter 22 (1960), years before Il-62
  ever becomes relevant. The full 244-quarter campaign's cash, pax-carried,
  and bankruptcy-quarter figures are unchanged; only the printed "N
  aircraft type(s) available" checkpoint count grew, as expected.

- **Per-aircraft operating efficiency — done, outside this roadmap's own
  milestones.** `AircraftType.CostPerAvailableSeatKm` replaces the flat
  global operating-cost rate `FinancialResolver` used for every aircraft
  type — the DC-3 and the 777 used to cost the same per seat-km to fly;
  now they don't (0.14 vs 0.07 in `data/aircraft.json`, trending down for
  newer types, with small regional aircraft correctly less efficient per
  seat than a contemporary mainline jet — a small deliberate deviation
  from strict chronological ordering, matching real aviation economics).
  This gives aircraft selection real operating-cost weight for the first
  time, alongside range/capacity/purchase price — explicitly requested as
  mechanics to solidify *before* touching `BaselineAi` (which still only
  picks by longest range and has no cost-awareness at all yet; that's the
  natural next step for the AI-differentiation item above, not done here).

  **Re-verified against the real dataset, not assumed safe:** since every
  named aircraft's rate differs from the old flat 0.09 default, this
  legitimately moved M13's and M19's already-recorded campaign figures —
  unlike the two changes above, byte-identical wasn't the goal or the
  result here. Re-ran the full 244-quarter campaign and updated
  `docs/SIM_CORE_ROADMAP.md`'s M13 section and this document's own M19
  section with the new numbers: M13's campaign no longer has a rise phase
  at all (DC-3/Constellation now cost more per seat-km than the old flat
  rate, so the early network runs a loss from quarter 1) and bankrupts one
  quarter earlier, at quarter 19 (1959) instead of 22 (1960) — the
  underlying finding (M8's heuristic eventually bankrupts an
  unconditionally-growing network) is unchanged, only the specific
  trajectory and timing. The 150-game QA harness sweep still passes
  cleanly (0 violations) against the new cost model.

- **Round-2 aircraft expansion: pre-1965 depth — done, outside this
  roadmap's own milestones.** `data/aircraft.json` grew from 14 to 21
  types, all focused on popular, commercially significant aircraft
  actually flying before 1965 (round 1 mostly filled in later eras):
  Douglas DC-6 (1947) and DC-7 (1953) extend the Douglas piston lineage
  between the DC-3 and the DC-8; Convair 340 (1948) gives NorthAmerica a
  second short/medium-haul option alongside the DC-3; de Havilland Comet
  (1952) — the real first commercial jet airliner, six years before the
  707 — and Sud Aviation Caravelle (1959, the first short-haul jet, whose
  rear-engine layout influenced the 727/DC-9/BAC 111) round out Europe
  beyond the Viscount; Douglas DC-8 (1959) gives NorthAmerica a direct 707
  rival; Ilyushin Il-14 (1954) is the actual Soviet DC-3-equivalent,
  joining Tu-104 (`ManufacturerRegion` left `null`, same reasoning as
  round 1).

  **The real regression risk this round, checked deliberately:** DC-6 and
  DC-7's genuine historical range (up to ~7,240 km and ~8,300 km for their
  extended/long-range sub-variants) would have exceeded the Constellation's
  4,200 km before 1955, silently swapping M13's campaign narrative and
  starting aircraft — a bigger version of the Il-62 risk from round 1.
  Resolved by using each type's *typical full-payload* range for its
  *original* 1947/1953 variant (4,000 km / 4,100 km) rather than a later
  extended-range sub-variant's record figure — a legitimate, non-fabricated
  simplification (commercial service rarely flew at max-range/min-payload
  anyway), consistent with how the existing Constellation entry was already
  a simplification of a multi-variant family. Locked in with a new test
  (`LoadFile_NoPre1958Aircraft_ExceedsConstellationsRange`) so a future data
  edit can't silently reintroduce the risk. Verified directly: the full
  244-quarter M13 campaign is byte-identical to its post-efficiency-update
  state (bankruptcy still at quarter 19/1959, same cash and pax figures) —
  the growth in aircraft options never actually changes which one
  `BaselineAi` picks before quarter 19, since none of them ever became
  "longest available."

  **M18/M19 did change, correctly:** `StartingConditionsResolver`'s
  smallest-range-first search now finds smaller, cheaper-to-fly options for
  several regions (Ilyushin Il-14 for Asia's guaranteed route, Convair 340
  for NorthAmerica's, both replacing the DC-3) — expected, not a
  regression; both roadmap docs' recorded M18/M19 figures were re-measured
  and updated. 150-game QA harness sweep passes cleanly (0 violations)
  against the expanded catalog.

- **Round-3 aircraft expansion: the 1960s/70s — done, outside this
  roadmap's own milestones.** `data/aircraft.json` grew from 21 to 28
  types, filling in what had been the thinnest era (only Il-62/737/747/A300
  covered two full decades): Boeing 727 (1963, the best-selling airliner of
  its era, 1,800+ built) and McDonnell Douglas DC-9 (1965) round out
  NorthAmerica's short-haul jets; BAC One-Eleven (1965) gives Europe a
  second short-haul jet alongside the Caravelle; McDonnell Douglas DC-10
  (1971) and Lockheed L-1011 TriStar (1972) are the era's two rival
  widebody trijets; Tupolev Tu-154 (1972, `ManufacturerRegion` null like
  the other Soviet types) was the USSR's 727-equivalent and, with 1,000+
  built, its most-produced jet airliner; Concorde (1976) is included for
  historical significance and its genuinely distinctive economics even
  without a speed mechanic — extremely expensive to buy ($60M, the
  highest in the catalog) and by far the least efficient per seat-km
  (0.25, checked directly against every other entry), reflecting why only
  two heavily-subsidized carriers ever flew it.

  **Lower regression risk than round 2, verified rather than assumed:**
  every new type is dated 1963 or later — after M13's campaign already
  freezes everyone bankrupt at quarter 19 (1959, per the efficiency-stat
  update) — so nothing in this round could touch the campaign's recorded
  trajectory, and nothing is available by 1955 either, so M16/M18/M19's
  recorded figures are untouched too. Still checked properly rather than
  assumed safe from the dates alone: DC-10 and L-1011's real widebody
  range (both close to 10,000 km) could plausibly have exceeded the 747's
  13,000 km "longest available" lead the same way DC-6/DC-7 nearly did to
  the Constellation in round 2 — confirmed neither does, and added a
  second regression test generalizing the pattern
  (`LoadFile_NoPre1970Aircraft_ExceedsThe747sRange`, alongside round 2's
  Constellation-era version) so this class of risk is now guarded at both
  "longest available" transitions, not just the one round 2 happened to
  hit. Full 244-quarter M13 campaign and M16/M18/M19 demo output confirmed
  byte-identical; 150-game QA harness sweep passes cleanly.

- **Round-4 aircraft expansion: the 1980s/90s — done, outside this
  roadmap's own milestones.** `data/aircraft.json` grew from 28 to 35
  types, deepening the era right after round 3's stopping point (only
  emb120/a320/crj100/777 had covered two full decades): McDonnell Douglas
  MD-80 (1980, DC-9 successor, 1,191 built) and Ilyushin Il-86 (1980, the
  first Soviet widebody, `ManufacturerRegion` null like the other Soviet
  types) open the decade; Boeing 767 (1982) and 757 (1983) are Boeing's
  ETOPS-twin and long-range-narrowbody answers to the widebody-trijet era
  round 3 covered; ATR 72 (1988) gives Europe a regional turboprop
  alongside its jets, complementing Embraer/Bombardier rather than
  duplicating them; Airbus A340 (1993) and A330 (1994) are Airbus's first
  modern long-range widebodies, arriving just before the 777.

  **Same "longest available" risk class as rounds 2-3, now guarded a third
  time:** A340 and A330's real-world range (both legitimately close to the
  747's 13,000 km) got the same deliberate check as DC-6/DC-7 (round 2)
  and DC-10/L-1011 (round 3) — confirmed neither exceeds it (A340 tuned to
  12,800 km specifically to stay clear, using its original 1993 variant's
  range rather than the later extended -500/600 sub-variants, the same
  "original variant, not a later upgrade" principle round 2 established
  for DC-6/DC-7). Added a third regression test extending the pattern to
  the 747→777 transition (`LoadFile_NoPre1995Aircraft_ExceedsThe777sRange`),
  so all three "longest available" transitions in the campaign's timeline
  are now permanently guarded, not just the ones each round happened to
  stress-test. Full 244-quarter M13 campaign and M16/M18/M19 demo output
  confirmed byte-identical; 150-game QA harness sweep passes cleanly.

- **Round-5 aircraft expansion: 2000-2015, the end of the timeline — done,
  outside this roadmap's own milestones.** `data/aircraft.json` grew from
  35 to 40 types, closing out the final stretch that only ARJ21 (2008) had
  covered: Embraer E190 (2004) is a modern, larger successor sitting
  alongside EMB-120 in SouthAmerica's own lineup; Airbus A380 (2005, the
  largest passenger airliner ever built — 525 seats, the catalog's biggest)
  and Boeing 787 Dreamliner (2011, the composite-airframe efficiency
  breakthrough — tuned to the catalog's single best `CostPerAvailableSeatKm`,
  0.06, matching its real-world efficiency reputation) are the era's two
  defining widebodies; Sukhoi Superjet 100 (2011, `ManufacturerRegion` null
  like the other Soviet/Russian types) carries that lineage into the
  modern era; Airbus A350 (2015) closes the catalog out, available for
  only the campaign's final few quarters by construction.

  **The "longest available" regression class, closed out rather than just
  extended again:** none of round 5's widebodies (A380 14,800 km, 787
  14,000 km, A350 15,000 km) exceed the 777's 17,000 km — checked
  directly, not assumed from round 4's pattern holding. Added a
  comprehensive closing test, `LoadFile_NoAircraft_ExceedsThe777sRange`,
  checking the *entire* catalog rather than just aircraft before a cutoff
  year — the 777 is confirmed the campaign's all-time range champion
  through 2015, so this whole regression class (four rounds, three
  transition-specific tests plus this one) is now fully closed rather than
  needing a fifth guard if a future round adds something past 2015. Full
  244-quarter M13 campaign and M16/M18/M19 demo output confirmed
  byte-identical; 150-game QA harness sweep passes cleanly.

  **The aircraft-catalog expansion arc across all five rounds is now
  complete relative to the original "Kept in mind" note's own scope:**
  American, European, Soviet, and Chinese manufacturers are all
  represented, alongside regional-jet/turboprop makers (Embraer,
  Bombardier, ATR) — the exact list that note named. 40 types span
  1935-2015 with no era thinner than roughly 5 types per decade. Further
  additions remain possible but are no longer closing a named gap.

- **City expansion, region by region — SouthAmerica (round 1 of an ongoing
  series) done, outside this roadmap's own milestones.** `data/cities.json`
  grew from 30 to 33 cities: Santiago, Bogota, and Lima bring
  SouthAmerica's non-gateway "eligible starting city" count from 2 (the
  thinnest of any home region, well below every other region's 5) to 5,
  reaching parity. All three are real, significant aviation markets, not
  padding — chosen the same way M15's original set was: genuine city
  weight, not filler.

  **A materially bigger blast radius than the aircraft-catalog rounds,
  and worth understanding why:** every aircraft round could mostly stay
  isolated by tuning `RangeKm`/`AvailableFromYear` so nothing changed
  which aircraft `BaselineAi` picks. Cities don't have that same
  isolation available, for two structural reasons. First,
  `BaselineAi.FindBestUnservedPair` searches every city pair in the world
  for its single best move, not pairs near an airline's own network — so
  new cities anywhere change every seat's candidate pool, not just the
  region they're added to. Second, `RegionalEconomics.Factor` normalizes
  each region's average against the *global* average across all 4 home
  regions' eligible cities — so adding cities to one region shifts every
  region's factor. Both were measured directly rather than assumed:
  M13's campaign still bankrupts at quarter 19 (timing unaffected) but the
  specific cash figure moved (-$2,660,674.22 → -$3,418,181.03, see
  `docs/SIM_CORE_ROADMAP.md`'s M13 section); all 4 regions' M16 economic
  factors moved, not just SouthAmerica's (see M16's own update note above).
  150-game QA harness sweep confirms correctness (no crashes, no invariant
  violations) holds regardless. This is expected to repeat for every
  future region in this series — each round's own entry should include a
  fresh measurement, not assume the last round's isolation-check habits
  from the aircraft work transfer over.

  New tests added rather than just re-measured figures: `CityCatalogTests.
  LoadFile_IncludesSouthAmericaExpansionCities` and `HomeRegionsTests.
  EligibleStartingCities_EveryHomeRegion_HasAtLeastFiveEligibleCities`
  (renamed from an exact-5 check to a floor check during the Europe round
  below, once a region legitimately grew past 5 — see that entry) — the
  latter iterates `HomeRegions.All` directly rather than hardcoding a
  per-region count, so it (and `StartingConditionsTests`' real-data sweep,
  which iterates `HomeRegions.EligibleStartingCities` the same way)
  needed zero changes to pick up the 3 new cities automatically, a small
  but real payoff of that design choice back in M15/M18.

  NorthAmerica, Europe, Asia, and Africa are unexamined by this round —
  next in the series, one region at a time, per explicit direction.

- **City expansion round 2: Europe (depth, not equalization) — done,
  outside this roadmap's own milestones.** Unlike SouthAmerica, Europe
  already had 5 eligible cities — parity, not a gap — so this round's
  goal was different: real depth, plus bringing Europe's gateway count in
  line with NorthAmerica's (jfk+lax) and Asia's (hnd+hkg) 2-flagship
  pattern, a structural asymmetry noted but deliberately not addressed
  during the SouthAmerica round. Decided explicitly rather than assumed:
  asked directly whether to add a second gateway or stay non-gateway-only
  like SouthAmerica; the answer was to add one. `data/cities.json` grew
  from 33 to 38 cities: Amsterdam joins London as Europe's second
  flagship gateway (cap 3, `demandWeight` 90 — comparable to London's 95),
  plus Munich, Istanbul, Barcelona, and Zurich as new non-gateway options,
  bringing Europe to 9 eligible cities and 3 gateways total (lhr, ams,
  and the Azores chokepoint).

  **A materially bigger ripple than the SouthAmerica round — this time it
  changed M13's campaign *timing*, not just its magnitude.** Amsterdam's
  arrival creates a dense cluster of short-haul, high-demand European
  pairs (ams-lhr, fra-muc, ams-fra, ams-cdg, muc-zrh, fra-zrh) that
  `BaselineAi` discovers and exploits far more effectively than the older,
  thinner map allowed. Re-measured directly, not assumed to hold: the
  campaign now has a real three-phase arc instead of the efficiency-stat
  update's steady decline — a mild early dip, a genuine rise to a new peak
  of $48.7M at quarter 36 (nearly 2.5x starting cash, the highest peak
  ever recorded in this project's history), then a rapid crash to
  bankruptcy at **quarter 47 (1966)** — more than double the previous
  quarter-19 timing. Full details and the reasoning for why this doesn't
  undermine M13's own finding are in `docs/SIM_CORE_ROADMAP.md`'s M13
  section, not repeated here. `AeroNext.Sim`'s checkpoint list was
  rewritten to match the new arc (1, 15, 36, 47, 121, 244) rather than
  leaving stale checkpoints that would print a now-inaccurate story.

  **A real, worth-knowing consequence for the M16 economic factors too:**
  Europe's *own* factor actually fell slightly despite gaining 4 new
  eligible cities (0.992 → 0.976), because the new cities' demand weights
  average below Europe's existing set — proof the ripple effect isn't
  simply "more cities always raises everyone's number," it depends on
  what the new cities are actually worth relative to the pool they join
  (see M16's own update note for the full before/after across all 4
  regions). The parity test from the SouthAmerica round
  (`EligibleStartingCities_EveryHomeRegion_HasParity_FiveEligibleCities`)
  legitimately failed once Europe passed 5 — not a regression, the
  intended consequence of organic per-region growth — and was rewritten
  to a `>= 5` floor check
  (`EligibleStartingCities_EveryHomeRegion_HasAtLeastFiveEligibleCities`).

  New tests: `CityCatalogTests.LoadFile_IncludesEuropeExpansionCities` and
  `Europe_HasTwoFlagshipGateways_MatchingNorthAmericaAndAsia`. 150-game QA
  harness sweep still passes cleanly (0 violations) — correctness holds
  even though the trajectory shape itself changed; the sweep's own runtime
  roughly tripled (44s → 120s) simply because campaigns now run far more
  routes before bankrupting, a real cost worth knowing about, not a
  regression. NorthAmerica and Africa remain unexamined — next in the
  series.

- **City expansion round 3: Asia (depth only, no new gateway) — done,
  outside this roadmap's own milestones.** Asia already sat at the same
  post-Europe-round baseline (5 eligible cities, 2 flagship gateways —
  hnd/hkg) before this round, so unlike Europe there was no gateway
  asymmetry to resolve; this round added depth only. `data/cities.json`
  grew from 38 to 43 cities: Beijing (95, edging out Shanghai as Asia's
  single highest-demand city — a real omission the previous roster had,
  given Shanghai was already there), Dubai (85 — one of the world's
  busiest airports by the campaign's 2015 end, tagged `Asia` for lack of a
  finer-grained Middle East region in this game's 4-home-region model),
  Mumbai, Kuala Lumpur, and Taipei, all non-gateway.

  **A comparable-scale ripple to the Europe round, same mechanism, further
  along the same trend line.** Kuala Lumpur-Singapore alone is a 375,570
  pax/qtr market — rivaling the campaign's biggest pairs — and `BaselineAi`
  folds the whole new cluster into the same rise phase the Europe round
  already opened up. Re-measured directly: the peak moved later and higher
  again (quarter 36, $48.7M → **quarter 41, $66.3M**) and bankruptcy moved
  later again too (quarter 47 → **quarter 54, 1968**), frozen at 54 routes
  and -$5,549,829.39. `AeroNext.Sim`'s checkpoint list was rewritten a
  third time to match (1, 15, 41, 54, 121, 244). Full reasoning for why
  three consecutive rounds moving the same numbers in the same direction
  still isn't a problem — each round makes the network richer and the
  road to bankruptcy longer, but has never once prevented it — is in
  `docs/SIM_CORE_ROADMAP.md`'s M13 section, not repeated here.

  **The M16 economic-factor ripple resolved with the opposite sign this
  time, worked out by hand rather than eyeballed:** every region's factor
  *fell*, including Asia's own (1.192 → 1.131) — Asia's 5 new cities
  average 71, below Asia's own prior average of 76 (pulling Asia's own
  number down) but still above the *global* average of ~63.8 across all
  eligible cities (pulling the shared denominator up, which pulls every
  other region's factor down too, even though their own averages didn't
  move at all). See M16's own update note for the full arithmetic — worth
  showing the actual numbers rather than asserting the direction, since it
  isn't predictable from "more cities" alone, as this round's opposite
  sign from the Europe round demonstrates directly.

  New test: `CityCatalogTests.LoadFile_IncludesAsiaExpansionCities`.
  150-game QA harness sweep passes cleanly (0 violations); runtime grew
  again (120s → 150s), consistent with the established pattern. Only
  NorthAmerica and Africa remain unexamined in this series.

- **City expansion round 4: NorthAmerica (depth only, no new gateway) —
  done, outside this roadmap's own milestones.** NorthAmerica already sat
  at the same baseline as the other regions (5 eligible cities, 2
  flagship gateways — jfk/lax) before this round, so — same as Asia, and
  unlike Europe — no gateway asymmetry to resolve, depth only.
  `data/cities.json` grew from 43 to 48 cities: Atlanta (90, closing a
  real, specific gap — one of the world's busiest airports for much of
  this campaign's 1955–2015 timeline wasn't represented at all, despite
  Delta's mega-hub status there), plus Dallas, Denver, Houston, and
  Vancouver, all non-gateway. Atlanta's weight now edges out even LA
  (85) for NorthAmerica's #2 spot behind New York.

  **Fourth consecutive round in the same ripple pattern, same mechanism,
  further along the same trend.** Dallas-Houston alone is a 220,797
  pax/qtr market. Re-measured directly: peak moved later and higher again
  (quarter 41, $66.3M → **quarter 47, $82.0M** — the highest ever
  recorded) and bankruptcy moved later again (quarter 54 →
  **quarter 61, 1970**), frozen at 61 routes and -$3,215,437.86.
  `AeroNext.Sim`'s checkpoint list rewritten a fourth time (1, 15, 47, 61,
  121, 244). Worth naming as a real, cumulative pattern rather than
  re-explaining each round from scratch: this is the fourth straight
  round where adding cities pushed the peak later/higher and bankruptcy
  later, and the fourth straight round where it never once prevented
  bankruptcy — see `docs/SIM_CORE_ROADMAP.md`'s M13 section for the full
  four-round streak, treated there as real, growing evidence for the
  underlying cause (M8's missing growth ceiling), not repeated here.

  **The M16 ripple flipped sign again — back to "rises," matching Europe's
  direction rather than Asia's — worked out by hand and cross-checked
  against all 4 regions' actual measured factors before writing it down:**
  NorthAmerica's own average barely moved (63.0 → 63.5 — Atlanta's 90
  pulling up against Denver/Houston/Vancouver's 50s pulling down), while
  the *global* denominator fell slightly (~65.0 → ~64.9, since
  NorthAmerica's 5 new cities average 64, just under the prior global
  average) — numerator flat, denominator down, so every region's factor
  rose a little, NorthAmerica's own by a bit more than the rest since both
  effects favor it at once. Full arithmetic, verified against all four
  regions' post-round numbers (not just the direction), is in M16's own
  update note above.

  New test: `CityCatalogTests.LoadFile_IncludesNorthAmericaExpansionCities`.
  Also fixed a hardcoded test expectation
  (`GameSetupTests.CreateSeats_ZeroHumans_PicksHighestDemandEligibleCityPerRegion`
  — Atlanta now beats Chicago for NorthAmerica's AI seat). 150-game QA
  harness sweep passes cleanly (0 violations); runtime grew again
  (150s → 232s). Only Africa remains unexamined in this series — a
  different kind of decision than the other three, since it isn't a home
  region at all (M15's bridge-region design), so "expand Africa" would
  first need deciding whether to keep it a pure chokepoint role or turn it
  into a real home region, not just add cities to an existing one.

- **City expansion round 5: cross-referenced against Aerobiz Supersonic —
  done, outside this roadmap's own milestones.** A different kind of round
  from the first four: instead of picking one region and researching real
  aviation markets for it, this one compared AeroNext's full city roster
  directly against the city list of Aerobiz Supersonic (the game this
  project is a spiritual successor to, per CLAUDE.md's own opening line)
  and added the clearest gaps found. Sourcing note, stated plainly rather
  than presented as more certain than it was: the game's own wiki page
  (the one authoritative source) returned an HTTP error on every fetch
  attempt, so the roster was cross-referenced from several secondary
  sources (web search summaries, a let's-play archive) instead — high
  confidence on the seven cities actually added, explicitly lower
  confidence on a longer list of smaller/secondary cities (Tunis, Baghdad,
  Karachi, Calcutta, Osaka, Athens, Perth, Auckland) that were deliberately
  left out of this round rather than added on weaker evidence.
  `data/cities.json` grew from 48 to 55 cities: Moscow (70) and Berlin
  (55) for Europe — Moscow in particular closes a thematically real gap,
  since the catalog already has three Soviet/Russian aircraft (Tupolev,
  Ilyushin, Sukhoi) but no Russian city at all; Washington (60) and Havana
  (35) for NorthAmerica; Tehran (45), Manila (55), and Sydney (60) for
  Asia — all three tagged `Asia` for lack of a finer Middle East/Oceania
  region, the same precedent already set for Dubai and Istanbul.

  **A real regression surfaced and fixed properly, not patched around:**
  Sydney, added as a non-gateway city like the other six, immediately
  failed `StartingConditionsTests.Resolve_EveryEligibleStartingCity_
  ProducesAcceptedQuarter1Route` — Sydney sits roughly 6,300-7,500 km from
  every other Asia-tagged city, well beyond any 1955-era aircraft's range,
  so M18's guaranteed-route search genuinely had nowhere to send it. Fixed
  by tagging Sydney a gateway instead of patching the test: it's an
  honest fit, not a workaround — Sydney really is Oceania's isolated
  entry point in the real world, the same structural role Cairo and Dakar
  already play for Africa in this dataset, and M18 already established
  that gateways stay fully flyable once an airline has long-enough-range
  aircraft (`QuarterEngine` never gates routes to gateways at all), so
  Sydney remains a real, reachable market — just not a free starting
  option, correctly reflecting how remote it actually is from 1955's
  perspective.

  **The four-round "later peak, later bankruptcy" streak broke, for the
  first time in this series — measured and documented honestly rather
  than forced to fit the established pattern.** This round touched three
  regions at once instead of one, with thinner, more scattered additions
  (Washington and Havana in particular are comparatively low-weight).
  Re-measured directly: the peak came out *smaller and earlier* (quarter
  47, $82.0M → **quarter 26, $24.6M**) and bankruptcy moved *earlier*, not
  later (quarter 61 → **quarter 55, 1968**). `AeroNext.Sim`'s checkpoint
  list was rewritten a fifth time (1, 15, 26, 55, 121, 244). Full
  reasoning for why this doesn't undermine the underlying finding — the
  streak was never a guaranteed law, just what four single-region,
  concentrated rounds happened to produce in a row — is in
  `docs/SIM_CORE_ROADMAP.md`'s M13 section, not repeated here.

  **The M16 ripple was genuinely mixed for the first time**, since three
  regions changed at once with different characteristics: NorthAmerica and
  Asia's factors fell, Europe and SouthAmerica's rose. Full region-by-region
  arithmetic (each region's own average, not just the direction) is in
  M16's own update note above — worked out the same way every prior round
  was, not asserted from a pattern.

  New tests: `CityCatalogTests.LoadFile_IncludesAerobizSupersonicCities`
  and `Sydney_IsAGateway_NotEligibleStartingCity`. 150-game QA harness
  sweep passes cleanly (0 violations); runtime grew substantially this
  time (232s → ~412s) — worth flagging as the point where a full sweep
  is starting to take real wall-clock time, not just "grew again."

- **City expansion round 6: Aerobiz Supersonic's actual full list, major
  tier — done, outside this roadmap's own milestones.** The previous
  round's cross-reference had to work from incomplete secondary sources
  (the game's own wiki 402'd on every fetch attempt); the user then
  supplied the real, complete 89-city list directly. Comparing it against
  the roster found 43 genuine gaps — every one of them confidently
  identifiable (including the list's own abbreviations: A. Ababa, K.
  Lumpur, San Fran, B. Aires, and similar), so nothing needed to be
  flagged back as uncertain, unlike the previous round's real sourcing
  gaps. Given 43 was far bigger than any round so far, asked explicitly
  how to pace it rather than guessing: by tier (major first, minor
  later) rather than by region or all at once. `data/cities.json` grew
  from 55 to 79 cities — the 24 clearly-major ones: 10 for Europe
  (Manchester, Brussels, Milan, Copenhagen, Stockholm, Vienna, Helsinki,
  Oslo, Athens, Kiev), 4 for Africa (Lagos, Algiers, Nairobi, Addis
  Ababa — Africa's first non-gateway cities ever, though see below for
  why that doesn't touch M16), 7 for Asia (Baghdad, Karachi, Calcutta,
  Osaka, plus Melbourne/Brisbane/Auckland tagged Asia for lack of an
  Oceania region, the same precedent as Sydney/Dubai/Istanbul), and 3 for
  NorthAmerica (Seattle, Phoenix, Philadelphia).

  **No repeat of the Sydney regression, verified by reasoning through the
  geography before running anything, not just gotten lucky.** Melbourne,
  Brisbane, and Auckland are just as far from mainland Asia as Sydney is
  — but unlike Sydney, they were added as a mutually-close cluster (all
  within ~1,200-2,600 km of each other), so each one has a valid
  same-region neighbor in the *other* new Oceania cities even though none
  of them reach the mainland. Confirmed directly: all 24 new cities
  passed `StartingConditionsTests.Resolve_EveryEligibleStartingCity_
  ProducesAcceptedQuarter1Route` on the first run, no fix needed.

  **A real, clean scope boundary got demonstrated for the first time
  rather than just stated:** Africa's 4 new cities are real data for
  `BaselineAi`'s global demand search, but Africa isn't a home region
  (M15's design), so they're structurally invisible to
  `RegionalEconomics.Factor` — confirmed directly in this round's M16
  arithmetic (see M16's own update note), not merely asserted from the
  existing rule.

  Peak and bankruptcy moved again — quarter 26/$24.6M → **quarter
  35/$34.6M**; quarter 55 → **quarter 52 (1967)** — landing between the
  two most recent rounds' numbers rather than establishing a new
  direction, consistent with the "no fixed law" reading the previous
  round's note already settled on. `AeroNext.Sim`'s checkpoint list
  rewritten a sixth time (1, 15, 35, 52, 121, 244). Full reasoning is in
  `docs/SIM_CORE_ROADMAP.md`'s M13 section.

  New test: `CityCatalogTests.LoadFile_IncludesAerobizSupersonicMajorTierCities`.
  150-game QA harness sweep passes cleanly (0 violations) at 79 cities;
  exact runtime wasn't captured for this specific run (it finished as a
  background task), but the previous round (55 cities) measured ~412s,
  and each round has only gotten slower as the map grows, so a future
  round is a reasonable point to reconsider `--games`'s default rather
  than just noting the growth again. The ~19 remaining
  smaller/chokepoint-tier cities from the same comparison (Nice, Minsk,
  Rostov, Tunis, Tripoli, Tashkent, Islamabad, Fukuoka, Sapporo,
  Khabarovsk, Cebu, Guam, Saipan, Perth, Adelaide, Noumea, Nandi,
  Papeete, Kingston) are the next round in this series, by the same
  explicit pacing decision.

- **City expansion round 7: Aerobiz Supersonic's actual full list, minor
  tier — done, outside this roadmap's own milestones.** The second half
  of the previous round's explicit pacing decision (major first, minor
  later): the ~19 smaller/chokepoint-tier cities left after round 6.
  `data/cities.json` grew from 79 to 98 cities — 1 for NorthAmerica
  (Kingston), 3 for Europe (Nice, Minsk, Rostov), 2 for Africa (Tunis,
  Tripoli), and 13 for Asia (Perth, Fukuoka, Tashkent, Islamabad,
  Adelaide, Cebu, Sapporo, Nandi, Guam, Khabarovsk, Noumea, Papeete,
  Saipan — Pacific islands and regional stepping-stones, not demand
  centers, matching the tier's own description). One data-entry mistake
  caught before it shipped: Tunis and Tripoli were first typed under
  `"region": "Europe"` (they're North African) — caught on review of the
  edit itself and fixed before running anything, not surfaced by a test
  failure.

  **`RegionalEconomics.Factor` crossed a threshold it hadn't before:**
  Asia's factor dropped below 1.0 for the first time in this series
  (1.060 → 0.903), driven by its 13 new cities averaging only 22.31 —
  well below every other round's new-city average — pulling Asia's own
  eligible-city average down harder than the falling global denominator
  could offset. See M16's own update note for the full region-by-region
  arithmetic, verified directly against `cities.json` rather than
  assumed from the previous round's direction.

  Peak and bankruptcy barely moved this time — quarter 35/$34.6M →
  **quarter 34/$34,270,806.67**; bankruptcy held at **quarter 52
  (1967)**, same quarter as the previous round but a different exact
  dollar figure. Small enough that spot-checking the previous round's
  checkpoints would have missed the peak's shift entirely — caught only
  because a full `Enumerable.Range(1, 60)` sweep runs every round
  regardless of how small the expected change looks. `AeroNext.Sim`'s
  checkpoint list rewritten a seventh time (1, 15, 34, 52, 121, 244).
  Full reasoning is in `docs/SIM_CORE_ROADMAP.md`'s M13 section.

  M19's demo seats mostly held steady: only Asia's AI seat moved (still
  starting at Beijing, but a closer, cheaper guaranteed destination
  turned up among the new Asia cities), net income -$219,670.15 →
  -$231,054.25, confirmed by temporarily reverting `cities.json` and
  diffing the demo output side by side rather than assumed from the
  city list alone. Europe, NorthAmerica, and SouthAmerica's demo seats
  are byte-identical. The -$122K/-$234K range holds without widening.

  New test: `CityCatalogTests.LoadFile_IncludesAerobizSupersonicMinorTierCities`
  (19 cities, region and non-gateway status both asserted). 150-game QA
  harness sweep passes cleanly (150/150, 0 violations, 0 crashes) at 98
  cities. Coverage holds at the established 99.78%/99.64% baseline, no
  new gaps. **This closes out the Aerobiz Supersonic cross-reference
  arc**: all 43 genuine gaps found against the user-supplied complete
  89-city list are now in the roster, split major/minor exactly as the
  user directed rather than by region or all at once.
