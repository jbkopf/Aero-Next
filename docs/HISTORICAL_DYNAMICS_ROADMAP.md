# Historical Dynamics Roadmap

**Status: M20 done, M21 round 1 done (Dubai), M21 round 2 done (global
curve redesign — real 1955-2015 growth/shock history), M21 round 3 done
(trend/event split — a real mechanism extension, not just content;
found and fixed a genuine event-composition bug during its own
verification), M21 round 4 done (every Olympic Games 1956-2015 with a
host city in the dataset; found and fixed a real performance regression
— a 150-game sweep that looked hung now runs in 53s), M21 round 5 done
(the remaining 14 Games, mapped to the nearest same-country dataset
city; 1984 Sarajevo skipped on the user's own direction — no dataset
city is in Bosnia/former Yugoslavia), M21 round 6 done (Suez Crisis
1956, first window of a 13-window sweep of 1955-2015 built to cover
every year with no gaps), M21 round 7 done (Cuba embargo 1962, window
2 — the first permanent, real-dataset event in the sweep; a real
regional shift measured in the QA sweep), M22 not started.** China's
opening and the 1997 Asian Financial Crisis remain named, not-yet-
authored future rounds. Builds on the completed sim core
(`docs/SIM_CORE_ROADMAP.md`, M0–M14) and game setup system
(`docs/GAME_SETUP_ROADMAP.md`, M15–M19, plus its ongoing city-expansion
series). Continues the shared milestone numbering from M20, since this is
the next thing built on the same headless core, not a separate project.

**The gap this exists to close:** confirmed directly against the code —
`City.DemandWeight` is a static field, set once in `cities.json` and never
touched again for the rest of a campaign. The only thing that varies
demand by year is `GravityDemandModel`'s `globalMultiplier`
(`Timeline.DemandMultiplierForYear`, reading `demand_growth.json`'s
piecewise-constant breakpoints — 1.0x in 1955 rising to 4.0x by 2015), and
that multiplier is applied identically to *every* city pair in the world.
Dubai and Dakar grow at exactly the same relative rate as New York and
London. There is no way today to represent a city that was a non-factor in
1955 and a mega-hub by 2015 (Dubai, most obviously — already named
directly during the city-expansion series as a real gap this exposed), or
a region whose relative importance shifted sharply mid-campaign, or a
discrete historical shock (an oil crisis, a war, a market opening) hitting
a specific place at a specific time rather than the whole world evenly.

**Scope: AeroNext.Core and AeroNext.Sim only**, same as every prior
roadmap. No Godot, no UI. Demand first — `City.DemandWeight` is the
concrete, already-identified gap — with two other static per-city
"economic and other game factors" named explicitly as related but
out-of-scope-for-now extensions, not silently forgotten (see "Kept in
mind" at the bottom): gate capacity/gateway status growing over time, and
`RegionalEconomics.Factor` becoming time-aware (it doesn't need to — see
that section for why).

**Design choice made before writing any milestone, not left implicit:**
"trends" (smooth per-city/region growth curves) and "events" (discrete,
dated, bounded shocks) are **the same mechanism at different time
resolutions**, not two separate systems. A slow-building trend is just a
schedule with several breakpoints tracing a curve; a sharp event is a
schedule with a brief spike or dip breakpoint and a reversion breakpoint
afterward. Building two parallel systems for what's structurally the same
idea would be exactly the kind of invented complexity CLAUDE.md's
legibility pillar warns against — one generalized schedule mechanism,
authored differently depending on the story being told, per
CLAUDE.md's "legibility over simulation depth."

Each milestone lands as its own small commit, with tests alongside the
logic, per CLAUDE.md working preferences. A milestone is done when
`dotnet test` covers it, a coverage check
(`dotnet test --collect:"XPlat Code Coverage"`) has been read and every
gap run to ground, and `AeroNext.Sim` can demonstrate it from the command
line.

---

## M20 — Generalize the demand schedule to region and city scope ✅

Extend the existing global-only mechanism rather than build a parallel
one. `DemandGrowthPoint` (`AeroNext.Core.World`) currently has just `(int
Year, double GlobalMultiplier)`. Add an optional scope: a breakpoint can
apply globally (today's only behavior, unchanged for every existing
entry), to one `Region`, or to one city id — mutually exclusive, validated
at load time the same way `DemandGrowthCatalog` already validates
duplicate years and non-positive multipliers today.

At a given year, a city's effective multiplier is the *product* of
whichever scopes apply to it: the global schedule (always), its region's
schedule (if one exists, else 1.0), and its own city-specific schedule (if
one exists, else 1.0). This composes cleanly with the gravity formula
without changing its shape — `GravityDemandModel.ForPair` already
multiplies `weightA × weightB`; making weight a function of `(city, year)`
instead of a constant is additive to the existing formula, not a rewrite
of it.

**The real architectural question to resolve here, not glossed over:**
`GravityDemandModel` is currently constructed *once per year* in the M13
campaign loop with a single `globalMultiplier` baked into its constructor
(`new GravityDemandModel(globalMultiplier: Timeline.DemandMultiplierForYear(growthSchedule, year))`).
A per-city-aware model needs a different query shape — either the model
takes the schedule and current year and resolves each pair's multiplier
at query time, or something upstream pre-computes effective weights per
city per year before the model ever sees them. Which is cleaner is a real
implementation decision, not resolved here — investigate both against the
existing call sites (the M13 campaign loop, the QA harness's
`FullGameRunner`, every earlier milestone's demo section that constructs
a `GravityDemandModel` directly) before picking one, since whichever shape
is chosen has to work for all of them, not just the campaign loop.

**Backward compatibility is the regression discipline here, same as every
aircraft/city round before it:** every existing `demand_growth.json` entry
has no scope today, so under the new schema it's implicitly global,
meaning the *default* behavior for a city or region with no override must
be byte-identical to today's flat global-only multiplier. This is
directly testable and should be, not just assumed.

**Done when:** a city or region given a bespoke schedule entry produces a
demand trajectory that measurably diverges from the global-only baseline
over the campaign (verified by comparing early-campaign vs. late-campaign
relative demand for a scripted example, the same "measure, don't assume"
standard every prior milestone in this project has held to); a city or
region with no override is provably unaffected — re-run the full M13
244-quarter campaign and confirm byte-identical output to the current
pinned figures in `docs/SIM_CORE_ROADMAP.md`'s M13 section before
considering this milestone done, exactly as the city-expansion series has
done at every step.

Done. **A real ambiguity in this section's own text, resolved with the
user before writing any code:** "the product of global × region × city
scopes," read literally, makes *global* a per-city factor too — for an
unscoped pair that turns today's flat `global` effect into `global²`
(2015's 4.0x would become 16x), breaking this very section's own
backward-compatibility requirement. Settled: global stays exactly what
it is today — one flat, pair-level multiplier, unchanged; region/city
scopes are a genuinely new, *separate* per-city factor (1.0 when
unscoped) multiplied in on top, once per city — trivially byte-identical
when nothing is scoped, and a pair between two boomed cities still
compounds (the real "Dubai took off" story M21 wants).

**Query-shape decision**, investigated rather than assumed: the model
resolves per-city multipliers at query time, not via a precomputed
rescaled city list. Checked every real `GravityDemandModel` construction
call site first (`Program.cs`, `FullGameRunner.cs`, `StartingConditions.cs`,
plus 104 call sites across 6 test files) — a rescaled-city-list approach
would need every function in the sim that takes `IReadOnlyList<City>` to
know to use a special per-year list instead of the canonical static one,
and `City.DemandWeight` is an `int` that would compound rounding loss if
rescaled repeatedly. Resolving inside `GravityDemandModel` itself keeps
the blast radius to one class plus its 4 real construction call sites;
the 104 test call sites that don't pass a schedule are untouched.

`DemandGrowthPoint` gained `Region?`/`CityId?` (mutually exclusive,
validated); `DemandGrowthCatalog` also now requires at least one global
breakpoint to exist (`Timeline.DemandMultiplierForYear`'s own fallback
relies on one always being there) and treats a duplicate year as a
same-*scope* duplicate, not a same-year one, since two breakpoints can
now legitimately share a year in different scopes. `GravityDemandModel`
gained optional `schedule`/`year` constructor parameters superseding
`globalMultiplier` when both are provided; `Timeline.DemandMultiplierForYear`
now correctly filters to the global-scoped portion only (a real
correctness fix this generalization required, not optional) so a
region/city breakpoint can never be mistaken for the global figure.

561 tests total (18 new, across `TimelineTests`/`GravityDemandModelTests`),
numerically pre-verified before writing — a region-scoped breakpoint
measurably changes only pairs touching that region; a city-scoped one
only pairs touching that city; both endpoints scoped compounds (6x from
2.0×3.0, not 2x or 3x alone); region+city on the same city both apply;
piecewise-constant resolution holds per scope independently. The core
backward-compatibility proof: a schedule carrying only a single global
entry produces **byte-identical** `PassengersPerQuarter` to the existing
flat-`globalMultiplier` constructor path, not just close. 100% line
coverage on every touched file; the one remaining partial branch
(`DemandGrowthCatalog`'s all-scoped check) is the same class of
confirmed structurally-unreachable short-circuit case M39-M41 already
documented — a `||`'s left-true branch collapses two theoretical slots
into one real outcome regardless of the right operand's value. Overall
project coverage **99.89%/99.20%**.

**Re-verified, not assumed:** `AeroNext.Sim`'s M13 loop re-run and
confirmed byte-for-byte identical to its own established current output
(quarters 1/4/7/10, Continental Wings wins at quarter 10) — **not**
`docs/SIM_CORE_ROADMAP.md`'s own M13 section, which predates M39
entirely (still describes the old M8 fixed-bump heuristic and a
quarter-19 bankruptcy) and was already stale for reasons unrelated to
this milestone; that section's own "Done when" byte-identical target is
no longer a meaningful baseline to check against and should be
re-pointed at `BASELINE_AI_ROADMAP.md`'s current figures whenever that
doc is next touched, not silently treated as this milestone's own
target. The 150-game QA harness sweep came back identical to M41's own
numbers too (118/150 wins, avg quarter 25.3) — expected and confirmed
directly: `demand_growth.json` itself still carries no region/city
entries (M21's job), so nothing behavioral could have changed yet.

---

## M21 — Author real historical trends and events (round 1: architecture + Dubai; round 2: global curve redesign; round 3: trend/event split; round 4: every Olympic Games + a real perf fix; round 5: remaining Games via same-country substitution; round 6: Suez Crisis, window 1 of a 13-window historical sweep; round 7: Cuba embargo, window 2) ✅

Content, not mechanics — the same shape as the aircraft-catalog and
city-expansion series, and expected to become its own ongoing round-by-round
series rather than one milestone that tries to cover the whole 1955–2015
span at once. Populate `data/demand_growth.json` (or a renamed/split file,
decided when this is actually built — extending the existing file keeps
one source of truth, but a dedicated `historical_events.json` might read
more clearly once city/region-scoped entries are mixed in with the
existing global ones; not resolved here) with real, well-known stories,
starting with the ones already named as gaps during the city-expansion
series:

- **Dubai** ✅ (round 1, this milestone) — near-zero relevance through
  the 1970s, a real inflection point once its free-trade-zone/
  aviation-hub strategy took hold in the 1980s–90s, continuing to climb
  sharply through the 2000s. The single clearest "this is exactly the
  gap this roadmap exists to close" case, named directly in the
  conversation that started this roadmap.
- **China's opening (Beijing, Shanghai)** — *not yet authored, a future
  round.* Muted through the Cultural Revolution era, a real inflection
  after 1978's opening-up reforms, continuing to climb through the
  1990s–2000s boom.
- **A global shock** ✅ (round 2) — redefined from "just the 1973 oil
  crisis" to the whole baseline curve, on the user's own direction: not
  one isolated event layered onto the old smooth 4-point line, but the
  curve's real shape end to end — jet-age growth, the 1973 oil crisis,
  the 1979-81 second shock, the 1991 Gulf War, 2001, and 2008-09 all
  included. See round 2's own writeup below.
- **1997 Asian Financial Crisis** — *not yet authored, a future round.*
  Real and dateable, but concentrated enough in impact
  (EastAsia/AustralAsia) that it reads better as a region-scoped round
  (`data/historical_events.json`) than diluted into the global curve —
  named here during round 2's own research so it isn't lost.
- **China's opening (Beijing, Shanghai)** — *not yet authored, a future
  round.* Muted through the Cultural Revolution era, a real inflection
  after 1978's opening-up reforms, continuing to climb through the
  1990s–2000s boom.
- Further candidates deliberately left open rather than pre-committed —
  each should be picked and authored the way each aircraft/city round
  was: one real, well-documented story at a time, verified against the
  campaign's actual measured output before being called done, not
  batch-authored speculatively.

**Done when (per round, not all-at-once — matching the "own ongoing
round-by-round series" framing above, not the original single-shot
reading of this bar):** each round authors one real story with a real,
checkable historical basis (not an invented number), and the M13
campaign's measured trajectory visibly reflects it — confirmed by
actually running it and reading the numbers, not assumed from the input
data alone. **This round (round 1)** also had to establish the
round-by-round *architecture* itself — a repeatable, low-friction shape
every future round (China, the oil crisis, and whatever else gets
picked) can just reuse without re-deriving it. China and the 1973 oil
crisis remain real, named, *not-yet-authored* candidates for their own
future rounds — not silently dropped, and not required for this round's
own "done."

**The repeatable recipe, for the next round to just follow:**
1. Pick one real, well-documented story with a checkable historical
   basis — a city/region inflection goes in `data/historical_events.json`
   (`cityId` or `region` scope); a global shock goes in
   `data/demand_growth.json` itself (global scope — the file this
   round's own architecture deliberately kept as the small, rarely-touched
   baseline curve).
2. Author 2+ breakpoints tracing the real story's shape — a slow trend
   is a handful of points; a sharp event is a dip/spike breakpoint plus
   a reversion breakpoint afterward (this roadmap's own stated design,
   settled before M20 was built). Give each entry a short `name` — pure
   documentation, but it makes the file self-explaining on its own.
3. Run `AeroNext.Sim`'s M13 loop and/or a direct `GravityDemandModel.ForPair`
   check (`DemandGrowthCatalog.LoadCampaignSchedule` loads everything
   merged) and confirm the story's effect is actually visible in the
   numbers — not assumed from the authored data alone.
4. Add a test proving the multiplier resolves correctly at each
   breakpoint (mirrors this round's own `RealDataset_Dubai_...` test).
5. Re-run the 150-game QA harness sweep and record whatever moved, the
   same "measure it, don't assume no change" standard this round used.
6. Write the round's own entry into this section (mark the bullet ✅,
   note the real measured effect), commit.

Done (round 1). **The architecture:** `data/historical_events.json` is a
new, dedicated file for authored region/city-scoped stories —
`data/demand_growth.json` stays exactly the small global-only baseline
curve it always was. `DemandGrowthPoint` gained an optional `Name` (pure
documentation, mirroring `cities.json`/`aircraft.json`'s own `id`/`name`
convention — never read by resolution logic). New
`DemandGrowthCatalog.LoadCampaignSchedule(dataDirectory)` is the one
entry point real callers use — loads both files and validates the
*combined* result once (not per-file — `historical_events.json` alone
will always have zero global entries, which M20's own "at least one
global breakpoint" rule would wrongly reject in isolation).
`AeroNext.Sim`/`AeroNext.QaHarness`'s `Program.cs` both switched to it;
both `.csproj` files already glob `data\**\*.json`, so the new file
needed zero build-config changes.

565 tests total (4 new). `RealDataset_Dubai_NegligibleEarly_MaterialyHigherLate`
proves Dubai's own real trajectory against the actual repo data (not a
synthetic city) — 1955's 0.3x factor vs. 2015's 10.0x is confirmed
directly, a wide margin, not a borderline check.
`LoadCampaignSchedule_MergesBaselineAndEvents_ValidatingTheCombinedResult`
proves the merge-before-validate ordering is load-bearing, not
incidental — an all-city-scoped `historical_events.json` would fail
validated alone. 100% line coverage on every touched file; the one
remaining partial branch (`DemandGrowthCatalog`, unchanged from M20) is
the same confirmed structurally-unreachable case already documented
there. Overall project coverage **99.89%/99.20%**.

**Measured, not assumed:** `AeroNext.Sim`'s M13 loop came back
byte-for-byte identical to its established current output — a real
result, not a compatibility check that was expected to hold: this loop's
own game resolves by quarter 10 (1957), decades before Dubai's own first
authored inflection (1985), so there was nothing yet for Dubai's story
to change *in this specific loop*, confirmed by actually running it
rather than assumed from the shape of the data.

The 150-game QA harness sweep, which runs the full 244-quarter campaign
and gives Dubai's later breakpoints real room to matter, moved for real
— and surfaced a genuine, unflattering side effect worth reporting in
full, not smoothed over. Overall win rate held (118/150, avg quarter
25.3→26.4), and EastAsia/AustralAsia/SouthAmerica moved only modestly.
**SouthWestAsia — Dubai's own home region — got measurably worse:**
bankrupt seats nearly doubled (17/55→33/55), average cash collapsed
($105.3M→$22.1M), and wins dropped (18→15). The cause, traced rather
than guessed: `dxb` is SouthWestAsia's own top-`DemandWeight` eligible
city, so it's that region's default AI starting city
(`HomeRegions.EligibleStartingCities`) — and Dubai's real, authored
"near-zero relevance through the 1970s" (0.3x, *below* the pre-M21
neutral baseline) now actively penalizes demand at exactly the city
every SouthWestAsia seat's earliest, highest-bankruptcy-risk routes fly
through (M41's own diagnosed danger window is quarters ~1-25; Dubai's
own multiplier doesn't clear 1.0x until 1985, quarter 120). A real,
historically accurate story authored correctly still has a real,
harsh knock-on effect on this simulation's own starting-city mechanics
— flagged here as a genuine finding for a future round (should
region-appropriate starting-city selection ever weight a city's own
demand trajectory, not just its 1955 snapshot?), not silently absorbed
into this round's own narrower scope.

### Round 2: the global baseline curve, redesigned against real history ✅

The user's own direction for this round: look at `data/demand_growth.json`
itself and how it should change over time, not just insert one more
event on top of the old smooth 4-point line
(`1955:1.0 → 1970:1.5 → 1990:2.5 → 2015:4.0`, no real historical basis
at all, not even the roadmap's own already-named 1973 oil crisis).

**Researched real global commercial aviation demand history first, not
assumed from memory** — confirmed via web search: world scheduled air
passenger traffic grew ~14%/year 1950-1970 (17.4B→237.4B passenger-miles,
ICAO), "exceeding 20% a year in the first post-war decade," badly
understating the old curve's own flat 1955→1970 span; 1970 itself saw a
real, documented **+31.1% single-year spike attributed to the Boeing 747**
(Pan Am's inaugural JFK-LHR 747 flight, Jan 22 1970 — contemporary
accounts describe the 747 as not just serving demand but *creating* it);
the 1973 oil embargo/1974-75 recession, the 1979-81 second oil shock
(Iranian Revolution), the 1991 Gulf War, and the 2008-09 financial
crisis are all real, dateable global contractions; **2001's 9/11** was
"the second time in history passenger air transport experienced a year
of negative growth" (a global loss of 35 million passengers 2000→2001)
— the sharpest single shock in the whole span, more severe than either
oil crisis by this measure.

**The design tension, resolved explicitly, not glossed over:** literally
tracking real ~14%/year 1950s-60s compounding would blow the curve's
endpoint up by orders of magnitude beyond what `GravityDemandModel`'s
scale/exponent were ever tuned against — the existing 1.0→4.0 envelope
was itself a gameplay-balance choice ("tuned by inspection... so load
factor isn't trivially pinned at 100% everywhere"), not a literal
economic model, same as every other placeholder in this codebase.
Resolved the same way Dubai's own round was: **real events set the
curve's shape and timing; magnitudes stay deliberately modest, in the
existing envelope's own neighborhood, each still (proposed, tunable).**

`data/demand_growth.json` replaced entirely — 17 global-scope
breakpoints (1955:1.0 through 2015:4.8), each with a `Name` citing its
real basis: early jet age (1958-59) → the 747 spike (1970) → the 1973
oil crisis dip (1975) → the second oil shock dip (1981) → recovery
(1985, landing the same real year Dubai's own story inflects) → the
Gulf War dip (1991) → 9/11, the sharpest dip in the span (2001) →
recovery (2004) → the financial crisis dip (2009) → continued growth to
2015. The existing `DemandGrowthCatalog_LoadFile_LoadsRepoSchedule_Increasing`
test asserted the repo schedule was monotonically increasing — no longer
true by design (the dips are the entire point) — rewritten to assert
what should actually hold instead: starts at 1.0, ends higher than it
started, years strictly ascending, and contains at least one real dip.

565 tests total (the rewritten test, no net-new count — a data-only
round). 100% coverage unaffected (no new source lines).

**Measured, not assumed — a real, substantial, mostly-positive shift,
for the first time in this series.** `AeroNext.Sim`'s M13 loop came back
byte-for-byte identical again — not a bug, confirmed by reasoning
through *why*: this loop's game resolves entirely within 1955-1957
(quarters 1-10), and both the old and new schedules share the exact same
first breakpoint (1955:1.0) with no other breakpoint before 1960 in the
new schedule either — so for this specific loop's short window, the two
curves are identical by construction, not coincidence.

The 150-game QA harness sweep, which runs the full 244-quarter campaign,
moved substantially:

| Metric | Round 1 (Dubai only) | Round 2 (curve redesigned) |
|---|---|---|
| Clean games with a win | 118/150 (avg quarter 26.4) | **123/150 (avg quarter 19.2)** |
| NorthAmerica bankrupt / avg cash / wins | 120/144, $4.0M, 13 | 117/144, **$23.8M**, 15 |
| Europe bankrupt / avg cash / wins | 79/95, $2.1M, 4 | 76/95, **$17.0M**, 2 |
| EastAsia bankrupt / avg cash / wins | 14/142, $113.0M, 92 | 12/142, $139.5M, 94 |
| SouthWestAsia bankrupt / avg cash / wins | 33/55, $22.1M, 15 | 28/55, **$104.4M**, 17 |
| AustralAsia bankrupt / avg cash / wins | 22/45, $68.8M, 7 | 21/45, **$236.7M**, 9 |
| SouthAmerica bankrupt / avg cash / wins | 61/119, $62.8M, 19 | 59/119, $59.3M, 13 |

Win rate rose and games resolve faster (avg quarter 26.4→19.2) — a
direct, causally-explainable consequence of the new curve's much
stronger early-campaign growth (1970's real multiplier is 2.6x here,
versus the old flat curve's 1.5x): every airline's early revenue is
higher, softening M41's own diagnosed early-bankruptcy danger window
(quarters ~1-25) across nearly every region. Notably, **SouthWestAsia's
own cash recovered substantially** (from round 1's Dubai-penalized
$22.1M average back up to $104.4M) even though Dubai's own early 0.3x
penalty is still fully in effect — the stronger overall baseline growth
partially offsets, without erasing, the harsh starting-city side effect
round 1 already flagged. The regional-dominance imbalance M39-M41 first
flagged is still present (EastAsia still wins the most, 94/123) and
remains open for a future round, not addressed here.

### Round 3: split trend from event — a real mechanism extension, not just content ✅

Round 2 authored real history into one merged curve, but merging it
was itself the wrong shape: every `DemandGrowthPoint` resolves by
"whichever breakpoint is latest wins," once per scope — so a base-growth
breakpoint and a shock-event breakpoint sharing a scope would silently
*clobber* each other, never compose. The user's own direction: split
them into two genuine concepts — an underlying base growth **trend**
or, later, growth or a shock **event modifier**, applicable globally
or per city, that multiplies on top of the trend rather than replacing
it.

**New `DemandGrowthPointKind` enum** (`Trend`/`Event`) on
`DemandGrowthPoint`, defaulting to `Trend` (every entry authored before
this round — the base curve, Dubai's own 4 points — needed zero data
migration). Resolution changed from one flat "latest wins" pool per
scope to two, resolved and composed independently:
```
pair-level global factor = globalTrend(year) x globalEvent(year)
per-city factor(city)    = regionTrend(city.Region, year) x regionEvent(city.Region, year)
                          x cityTrend(city.Id, year)       x cityEvent(city.Id, year)
```
Trend resolution is exactly M20's own unchanged behavior (falls back to
the earliest trend breakpoint if queried before the schedule starts).
Event resolution reuses the same "latest breakpoint at or before year"
lookup but always defaults to neutral (1.0) when none is active, at
every scope including global — this is what lets an event revert
cleanly (a second event breakpoint at `1.0`) or stay permanent (no
reversion breakpoint at all, for a real structural shift like the 747).

**Two validation fixes the split actually required, not incidental:**
"at least one global breakpoint" now specifically means at least one
global *Trend* — a schedule with only global Events (exactly what this
round adds) would otherwise wrongly satisfy the old, looser check, and
`Timeline.DemandMultiplierForYear`'s own fallback would have nothing
real to fall back to. The duplicate-(year, scope) check now includes
`Kind` too — 1970 legitimately carries both a global Trend breakpoint
(the base curve) and a global Event breakpoint (the 747) the same year,
which the old check would have wrongly flagged as a duplicate.

**The data, redistributed from round 2's own research** (no
re-research needed): `data/demand_growth.json` is now a clean 4-point
smooth *trend* only (`1955:1.0 → 1970:2.2 → 1990:3.2 → 2015:4.5`, no
shocks baked in). `data/historical_events.json` keeps Dubai's own 4
city-`Trend` points unchanged (a sustained multi-decade shift is
genuinely a trend, not a temporary event — correctly classified
already) and gains 11 new global `Event` entries: the 747 (1970, a
permanent +15% step-up, no reversion), the 1973 oil crisis (dip,
recovers 1976), the 1979-81 second oil shock (dip, recovers 1984), the
1991 Gulf War (dip, recovers 1993), 9/11 (2001, the sharpest dip in the
set, recovers 2005), and the 2008-09 financial crisis (dip, recovers
2011) — each a *relative* modifier now (e.g. 0.75 for 9/11's -25%)
rather than round 2's absolute replacement values.

573 tests total (14 new). Numerically pre-verified before writing, same
discipline as every round: trend and event compose multiplicatively,
not additively or by replacement; an event with an explicit reversion
returns *exactly* to the trend-only value, not approximately; an event
with no reversion stays active decades later; a city can carry both its
own trend and its own event, composing; the real repo files load with
both a global trend and at least one global event present. 100%
coverage on every touched file — the trend/event split's own validation
fix (`ThrowsWhenOnlyGlobalEventsExist`) closed a pre-existing partial
branch in `DemandGrowthCatalog` that predated this round, not just kept
it at parity. Overall project coverage **99.89%/99.34%**.

**Measured, not assumed.** `AeroNext.Sim`'s M13 loop stayed
byte-for-byte identical again — reasoned through why, not just
observed: that loop resolves entirely within 1955-1957, and neither
the new trend curve nor any authored event has a second breakpoint
before 1970, so the effective multiplier for this loop's whole window
is unchanged at 1.0.

**A real bug was found and fixed during this round's own QA harness
verification, not glossed over.** The first version of this split
resolved global Events from one flat "latest breakpoint wins" pool —
functionally identical to the exact bug the trend/event split was
built to fix, just moved down one level (event vs. event, instead of
trend vs. event). Confirmed directly (not assumed) by computing the
multiplier trajectory through a scripted scenario: the 747's permanent
+15% (`1970`, no reversion) was silently *erased* the instant the 1973
oil-crisis breakpoint fired, and never returned — not even after the
crisis's own 1976 reversion, which reset all the way back to the plain
baseline instead of back to the 747-boosted figure. **Fixed with a new
required `EventGroup` field** on every Event breakpoint
(`DemandGrowthCatalog` now validates it's present) — independent events
are grouped and resolved separately, then every group's own result
multiplies together, rather than sharing one pool. Verified the fix
against the same scripted scenario before re-measuring anything: the
747's own effect now correctly persists through and after the oil
crisis. 4 more tests added for this specifically (577 total, up from
573), including a direct regression test for the exact bug
(`ForPair_TwoIndependentGlobalEvents_ComposeInstead_OfOneOverwritingTheOther`).

Re-measured the 150-game QA harness sweep with the fix in place:

| Metric | Round 2 (single curve) | Round 3 (trend/event, fixed) |
|---|---|---|
| Clean games with a win | 123/150 (avg quarter 19.2) | 113/150 (avg quarter 19.9) |
| NorthAmerica bankrupt / avg cash / wins | 117/144, $23.8M, 15 | 119/144, $4.4M, 14 |
| Europe bankrupt / avg cash / wins | 76/95, $17.0M, 2 | 80/95, $4.1M, 5 |
| EastAsia bankrupt / avg cash / wins | 12/142, $139.5M, 94 | 14/142, $138.3M, 88 |
| SouthWestAsia bankrupt / avg cash / wins | 28/55, $104.4M, 17 | 29/55, $113.4M, 17 |
| AustralAsia bankrupt / avg cash / wins | 21/45, $236.7M, 9 | 23/45, $83.6M, 7 |
| SouthAmerica bankrupt / avg cash / wins | 59/119, $59.3M, 13 | 62/119, $63.7M, 19 |

Win rate and NorthAmerica/Europe's own average cash both dropped versus
round 2 — expected, not a regression to chase: round 2's single merged
curve had every authored shock permanently *raise the floor* by
construction (each recovery breakpoint was an absolute value at or
above the previous peak, e.g. 2001's dip recovered to a *higher*
absolute figure by 2004 than 1990's own value), while round 3's events
are genuine *relative* dips that multiply the trend back down and then
release it, a more historically honest shape but a less generous one
game-balance-wise. Comparing round 3's own before/after-fix numbers
directly confirms the fix moved things in the expected direction without
changing the overall picture much (e.g. NorthAmerica cash $3.8M→$4.4M,
AustralAsia $75.9M→$83.6M) — most QA-harness games apparently resolve
(via bankruptcy or a win) before reaching a point where two overlapping
global events would have mattered as much as the isolated debugging
scenario suggested, so the bug was real and worth fixing correctly, but
wasn't the single dominant driver of this round's own headline numbers.

### Round 4: every Olympic Games, 1956-2015 — a stress test of the architecture ✅

The user's own framing: "let's test the system" — a genuinely large batch
of real, independent, city-scoped events (rather than one at a time),
to prove the round 3 architecture (and its own `EventGroup` fix) holds
up at scale, not just in a hand-picked example or a small scripted
scenario.

**Real, checkable basis, cross-referenced against the actual repo
dataset, not assumed:** every Summer and Winter Olympic Games from 1956
through 2015 (real, well-known dates and host cities), checked against
`data/cities.json`'s own 99-city list. **14 of 15 Summer Games host
cities are present** (Melbourne, Rome, Tokyo, Mexico City, Munich,
Moscow, Los Angeles, Seoul, Barcelona, Atlanta, Sydney, Athens,
Beijing, London — only 1976 Montreal is missing from the dataset).
**Only 2 of 16 Winter Games host cities are present** (Sapporo 1972,
Vancouver 2010) — this dataset models major air-travel hubs, and most
Winter host cities are smaller resort towns (Cortina d'Ampezzo, Squaw
Valley, Innsbruck, Grenoble, Lake Placid, Sarajevo, Calgary,
Albertville, Lillehammer, Nagano, Salt Lake City, Turin, Sochi) that
were never in scope for the city-expansion series. **Reported honestly
as a real, checkable data gap, not silently patched over with a
nearby-but-wrong substitute city** — 15 Games (1 Summer, 14 Winter) go
unauthored, named here so they aren't lost.

**16 events authored** (`data/historical_events.json`), each a
city-scoped `Event`, its own `EventGroup`
(`"olympics-{year}-{summer|winter}"`), a real +20% *(proposed, tunable
— a modest, deliberately uniform figure across every era and city, not
individually researched per Games, since the trend curve already
carries how much bigger overall demand is by any given year; the event
represents relative excitement over that year's own baseline, which
should be roughly comparable era to era)* boost in the host year,
reverting the following year — 32 breakpoints total. Directly exercises
scenarios round 3's own tests only checked individually: **two
Games can share a scope-adjacent year without colliding** (Munich
Summer and Sapporo Winter both 1972, different cities, no issue,
confirmed by the catalog's own validation passing); **a host year can
overlap a live global event** (Beijing 2008's own reversion year, 2009,
is also when the global financial-crisis event begins — different
scopes, compose independently, no interference); **a host year can
land exactly on another event's own reversion year** (Los Angeles 1984
is precisely when the global 1979 oil-shock event reverts to neutral —
both apply correctly in the same year, LA's boost stacking cleanly on
top of the oil shock's own release).

New test `RealDataset_OlympicsEvents_BoostHostYearOnly_NotAdjacentYears`
checks four real host cities spanning the full era range (Melbourne
1956, Los Angeles 1984, Beijing 2008, London 2012) against the actual
repo data, confirming each host year's demand exceeds both the year
before and the year after reversion — not a synthetic scenario. 578
tests total (1 new — the data itself needed no new source code, proving
the architecture really is data-only for this kind of addition, exactly
what "easily adding events" was supposed to mean). 100% coverage
unaffected.

**Measured, not assumed.** `AeroNext.Sim`'s M13 loop stayed
byte-for-byte identical yet again — even though the 1956 Melbourne
Games falls inside this loop's own quarter-1-10 (1955-1957) window this
time, reasoned through why rather than just observed: none of the 4
competing regions this loop's own seed produces happen to route through
Melbourne specifically before Continental Wings' own quarter-10 win.

**A second real bug was found — a severe performance regression this
time, not a correctness one — and it's exactly the kind of thing
stress-testing at scale was for.** The 150-game QA harness sweep looked
hung: 20+ minutes with no output, versus every prior round's own
sub-2-minute runs. Checked whether it actually was hung rather than
assuming either way — Windows process inspection showed the sweep's own
process had accumulated barely 1.5 seconds of *CPU* time across 25+
minutes of wall time, which reads as blocked, not computing, so this
got treated as seriously as a real hang until proven otherwise.
Bisected by game count (`--games 1` through `5`) to find the exact
seed that stalled, then reproduced it in isolation with a scripted
`FullGameRunner.Run` call and per-quarter/per-seat diagnostic output.
That confirmed it wasn't actually infinite — one specific `DecideOrders`
call for one seat, at one quarter, was taking upward of a minute on its
own (confirmed by simply giving it more time and watching it eventually
return), while every other call across 20+ prior quarters took a
fraction of a second.

**Root cause, traced rather than guessed:** every one of
`GravityDemandModel`'s trend/event factors depends only on the
schedule and the year, both fixed for a model instance's entire
lifetime — never on which two cities a given `ForPair` call is about.
The round 3 implementation recomputed them (a `Where`/`GroupBy` scan of
the *whole* schedule) on **every single `ForPair` call** anyway.
`AllPairs` alone calls `ForPair` O(cities²) times a quarter (99 cities
→ 4,851 pairs), and `BaselineAi.FindBestUnservedPair` — called once per
*idle fleet unit* inside `AiToolkit.ApplyManagementToolkit`'s own
stranded-unit check — calls `AllPairs` itself, sometimes several times
a quarter. Quadrupling `historical_events.json`'s size (16 new Olympics
entries on top of the existing 15) was enough to push the accumulated,
needlessly-repeated cost over the edge for specific, unlucky
combinations of network size and idle-fleet state.

**Fixed by computing each factor exactly once per model instance,
in the constructor, into a couple of small dictionaries** (`ForPair`
itself becomes plain lookups) — not a change to any resolution rule or
fallback behavior, confirmed by the full 578-test suite passing
completely unchanged and the M13 loop staying byte-for-byte identical.
The real 150-game sweep, previously indistinguishable from hung, now
completes in **53 seconds** — back in line with every prior round.

Final, correctly-measured 150-game QA harness sweep, this round's real
content (all 16 Olympics events) live:

| Metric | Round 3 (no Olympics) | Round 4 (+ Olympics) |
|---|---|---|
| Clean games with a win | 113/150 (avg quarter 19.9) | 113/150 (avg quarter 19.8) |
| NorthAmerica bankrupt / avg cash / wins | 119/144, $4.4M, 14 | 119/144, $4.5M, 14 |
| Europe bankrupt / avg cash / wins | 80/95, $4.1M, 5 | 80/95, $4.1M, 5 |
| EastAsia bankrupt / avg cash / wins | 14/142, $138.3M, 88 | 14/142, $140.5M, 88 |
| SouthWestAsia bankrupt / avg cash / wins | 29/55, $113.4M, 17 | 29/55, $113.4M, 17 |
| AustralAsia bankrupt / avg cash / wins | 23/45, $83.6M, 7 | 22/45, $92.5M, 7 |
| SouthAmerica bankrupt / avg cash / wins | 62/119, $63.7M, 19 | 62/119, $63.6M, 19 |

Overall shape essentially unchanged — expected, and a sign the
individual Olympics events are correctly modest in scale, not that
they're inert: **AustralAsia moved the most** (bankrupt 23→22, avg
cash $83.6M→$92.5M, median cash flipped from -$22K to +$3.1M), the one
region hosting *two* of the authored Games (Melbourne 1956, Sydney
2000) — a real, small, explainable effect, not noise, sized correctly
relative to the region's own much larger existing drivers (starting
economics, strategy choice, the global trend/event backdrop).

### Round 5: the 14 remaining Games, via same-country/nearest-city substitution ✅

Round 4 named 15 Games with no host city in the dataset (1 Summer, 14
Winter) rather than silently drop them. This round closes 14 of those
15 — every real host city was cross-referenced against the actual
99-city dataset (with each existing city's real-world country inferred
directly, not guessed) and mapped to the nearest city **in the same
country**, computed via the exact same great-circle formula
`GreatCircle.cs` itself uses, not eyeballed:

| Games | Host (country) | Substitute | Distance |
|---|---|---|---|
| 1956 Winter | Cortina d'Ampezzo (Italy) | Milan (`mil`) | 257 km |
| 1960 Winter | Squaw Valley (USA) | San Francisco (`sfo`) | 256 km |
| 1964 Winter | Innsbruck (Austria) | Vienna (`vie`) | 386 km |
| 1968 Winter | Grenoble (France) | Nice (`nce`) | 205 km |
| 1976 Summer | Montreal (Canada) | Toronto (`yyz`) | 521 km |
| 1976 Winter | Innsbruck (Austria) | Vienna (`vie`) | 386 km |
| 1980 Winter | Lake Placid (USA) | New York (`jfk`) | 405 km |
| 1988 Winter | Calgary (Canada) | Vancouver (`yvr`) | 681 km |
| 1992 Winter | Albertville (France) | Nice (`nce`) | 229 km |
| 1994 Winter | Lillehammer (Norway) | Oslo (`osl`) | 135 km |
| 1998 Winter | Nagano (Japan) | Tokyo (`hnd`) | 189 km |
| 2002 Winter | Salt Lake City (USA) | Denver (`den`) | 620 km |
| 2006 Winter | Turin (Italy) | Milan (`mil`) | 126 km |
| 2014 Winter | Sochi (Russia) | Rostov (`rov`) | 404 km |

**1984 Sarajevo stayed unauthored, on the user's own explicit
direction** — no city anywhere in the dataset is in Bosnia or the
former Yugoslavia, so the "same country" rule this round otherwise
used throughout has nothing to anchor to there; the fallback
("geographically closest, any country" → Vienna, 509 km) was computed
and offered, but skipped rather than assumed acceptable. Still named
in the doc so it isn't silently lost from the record. Each substitute
event's own `name` field states plainly that it's standing in for a
host city not in the dataset, and by how far — self-documenting, not
requiring a reader to cross-reference this table to understand why
(say) Vienna carries three separate Olympics-shaped bumps across the
campaign (1964, 1976, and none of its own).

14 new events, 28 breakpoints, same +20%/host-year/reverts-next-year
shape as round 4 — still entirely data, no source changes. 578 tests
unchanged, coverage unaffected (confirmed, not assumed, same as every
prior data-only round).

**Measured — and for the first time in this whole M20-M21 series,
`AeroNext.Sim`'s M13 loop actually moved.** Quarters 1 and 4 (1955)
stayed byte-identical, but quarter 7 (1956) diverged for real: Atlas
Air's carried traffic rose from 108,472 to 111,384 pax/qtr and Skyward's
from 185,328 to 188,760 — the Cortina-d'Ampezzo-via-Milan proxy event
(active for all of 1956) reaching a route this loop's own seed happens
to fly through Milan on, the first time any round in this series has
been visible in that specific loop rather than resolving too early or
missing every relevant city. The win condition itself was unaffected —
Continental Wings still wins at quarter 10.

The 150-game QA harness sweep (54s, no perf regression from doubling
the event count again) also moved for real this time, unlike round 4's
own near-parity comparison to round 3:

| Metric | Round 4 (16 events) | Round 5 (30 events) |
|---|---|---|
| Clean games with a win | 113/150 (avg quarter 19.8) | 113/150 (avg quarter 19.9) |
| NorthAmerica bankrupt / avg cash / wins | 119/144, $4.5M, 14 | 118/144, **$14.8M**, 15 |
| Europe bankrupt / avg cash / wins | 80/95, $4.1M, 5 | 80/95, $4.1M, 4 |
| EastAsia bankrupt / avg cash / wins | 14/142, $140.5M, 88 | 14/142, $140.6M, 88 |
| SouthWestAsia bankrupt / avg cash / wins | 29/55, $113.4M, 17 | 29/55, $113.4M, 17 |
| AustralAsia bankrupt / avg cash / wins | 22/45, $92.5M, 7 | 22/45, $92.6M, 7 |
| SouthAmerica bankrupt / avg cash / wins | 62/119, $63.6M, 19 | 62/119, $63.6M, 19 |

**NorthAmerica moved the most, by a wide margin** (avg cash more than
tripled, $4.5M→$14.8M) — the one region that picked up *five* new
proxy events on its own cities this round (Toronto, New York, Vancouver,
San Francisco, Denver — Squaw Valley/Lake Placid/Calgary/Montreal/Salt
Lake City all substitute onto NorthAmerica cities), versus Europe's
three (Milan ×2, Vienna ×2, Nice ×2 — six event-slots but only three
distinct cities) and no change at all for the regions that picked up
none (EastAsia, SouthWestAsia, SouthAmerica essentially flat). A real,
explainable, region-count-proportional effect, not noise.

### Round 6: the 5-year historical sweep, window 1 (Suez Crisis 1956) ✅

Rounds 1-5 authored events opportunistically (Dubai, then whatever the
global curve's own research turned up, then a single stress-test batch
of Olympics). Before continuing further, the user asked whether a
5-year window is a reasonable unit to search for real, travel-relevant
history without leaving gaps — confirmed yes, then this round built the
actual list: every one of the 61 campaign years (1955-2015) sorted into
13 five-year buckets, so the series has a map instead of continuing to
pick events ad hoc.

| Window | Candidate(s) | Status |
|---|---|---|
| 1955-59 | Suez Crisis (1956) | Authored round 6 — Cairo (`cai`), city-scoped |
| 1960-64 | US embargo on Cuba (1962) | **Authored this round** — `hav`, permanent step-down |
| 1965-69 | Six-Day War → Suez Canal closed (1967-75) | Named, spans into windows 4-5; scope not yet decided |
| 1970-74 | (747, Munich/Sapporo Olympics, first oil crisis) | Already covered by rounds 3-4 |
| 1975-79 | Suez reopens (1975); Camp David / Egypt-Israel peace (1979) | Named — Cairo bookend + boost |
| 1980-84 | Iran-Iraq War (1980-88) | Named, spans into window 7 — `thr`, `bgw` |
| 1985-89 | Chernobyl (1986); Berlin Wall falls (1989) | Named — Europe-wide dip; `ber` trend-shift |
| 1990-94 | USSR dissolves (1991) | Named — `mow` |
| 1995-99 | Hong Kong Handover (1997) | Named — `hkg`, flagged as historically mild |
| 2000-04 | SARS (2003); Iraq War (2003) | Named — first real **Region**-scope candidate (EastAsia); `bgw` |
| 2005-09 | — | No standout beyond what rounds 3-4 already cover (Beijing, financial crisis) |
| 2010-14 | Eyjafjallajökull eruption (2010); Arab Spring (2011) | Named — Europe-wide airspace shutdown; Cairo/North Africa |
| 2015 | — | Campaign's final year — no room for a reversion arc, not worth authoring |

China's opening and the 1997 Asian Financial Crisis stay reserved for
their own dedicated rounds (named since round 1/2), not folded into
this sweep. Windows are implemented one at a time, on the user's own
direction — this round closes window 1 only.

**Suez Crisis (1956):** Egypt nationalizes the canal in July, the
Anglo-French-Israeli invasion and blockade follow, the canal reopens
after UN clearance in April 1957. Modeled as a **city-scoped** event on
Cairo (`cai`) rather than global — a deliberate departure from every
prior shock in this series (oil crises, wars, 9/11, the financial
crisis), all of which were genuinely worldwide. Suez was not: it was an
Egypt-centered nationalization and invasion, so diluting it into a
global multiplier would misrepresent its real reach. -20% (`0.80`) for
1956, reverting to neutral in 1957 — the same sharp-shock/explicit-
reversion shape used for every other temporary event this series has
authored, sized the same as the Olympics boost for consistency (event
magnitudes throughout this series are *(proposed, tunable)*, not
individually researched — the base trend curve already carries how
much bigger overall demand is by any given year).

One new proof test,
`RealDataset_SuezCrisis_DipsCairoIn1956_RecoveredBy1958`: confirms 1956
dips below 1955 for a Cairo pair, and — since the trend curve itself is
flat between 1955 and 1970 with no breakpoint in between — 1958 lands
back at *exactly* the same value as 1955, proving the reversion is
complete, not partial. 579 tests, up from 578, all passing; coverage
gate re-run and unchanged (same accepted baseline gaps as every prior
round: `BriefingGenerator`, `QuarterOrder`, `AiToolkit`, `BaselineAi`,
`RapidExpansionStrategy`, `NetworkPlanner` — `GravityDemandModel`,
`DemandGrowthCatalog`, `Timeline` stay at 100%).

**Measured, and genuinely unchanged this time — a real "no effect"
result, not an unmeasured assumption.** Both the M13 loop and the
150-game QA sweep came back byte-for-byte identical to round 5's own
numbers:

- M13 loop: quarters 1, 4, 7, and 10 (1955-57) match round 5 exactly
  (Atlas Air still 111,384 pax/qtr at quarter 7, Continental Wings
  still wins at quarter 10) — this loop's own deterministic early
  routes don't happen to touch Cairo.
- 150-game sweep: 113/150 clean wins (avg quarter 19.9), and every
  regional row (NorthAmerica 118/144 bankrupt $14.83M avg cash 15
  wins; Europe 80/95 $4.11M 4 wins; EastAsia 14/142 $140.60M 88 wins;
  SouthWestAsia 29/55 $113.44M 17 wins; AustralAsia 22/45 $92.58M 7
  wins; SouthAmerica 62/119 $63.61M 19 wins) matches round 5 exactly.
  Africa (Cairo's own region) isn't among the 6 regions this breakdown
  even lists — none of the 4 competing airlines in these particular
  150 seeded games happen to call it home or route through it, so a
  Cairo-scoped dip has nothing to move. Consistent with this project's
  own chokepoint design pillar: a gate only matters when someone is
  actually contesting it.

### Round 7: the sweep, window 2 (Cuba embargo, 1962) ✅

**US embargo on Cuba:** Kennedy's February 1962 trade proclamation
collapsed the previously-dominant US-Cuba passenger market. Unlike
every event authored so far, it never reverted within the campaign's
own timeframe — commercial air service between the US and Cuba stayed
cut off in real history until well past 2015, the campaign's own end
year. Modeled with **no reversion breakpoint at all**, the same
permanent shape already established by the 747's own step-up: a single
`Event` breakpoint at 1962 (`0.65`, city-scoped to `hav`), and nothing
after it.

Magnitude (-35%, permanent) is sized larger than the sweep's own
temporary-shock convention (Olympics ±20%, Suez -20%) deliberately —
this event is different in *kind*, not just degree: a structural,
permanent collapse of a city's dominant market, not a multi-month
shock with a recovery arc. Still *(proposed, tunable)*, not
individually researched to the point, matching every other magnitude
in this series.

New proof test,
`RealDataset_CubaEmbargo_PermanentlyDipsHavanaFrom1962_NeverReverts`:
confirms 1962 dips below 1961, then proves permanence against a
same-year *reference schedule with the embargo's own event group
removed* (not just an earlier year, since the trend curve itself keeps
moving) — the with-embargo/without-embargo ratio stays pinned at the
authored 0.65 factor as late as 2015, decades after onset, rather than
drifting back toward 1.0. 580 tests, up from 579, all passing;
coverage gate re-run and unchanged (same accepted baseline as every
round: `BriefingGenerator`, `QuarterOrder`, `AiToolkit`, `BaselineAi`,
`RapidExpansionStrategy`, `NetworkPlanner`; `GravityDemandModel`,
`DemandGrowthCatalog`, `Timeline` stay at 100%).

**Measured — the M13 loop stayed byte-identical (expected: it only
prints checkpoints through quarter 10, 1957 — five years before the
embargo even begins, so there's no way for this event to reach it),
but the 150-game QA sweep, which runs the full 244-quarter campaign
through 2015, moved for real, concentrated entirely in NorthAmerica
(Havana's own region):**

| Metric | Round 6 (Suez only) | Round 7 (+ Cuba embargo) |
|---|---|---|
| Clean games with a win | 113/150 (avg quarter 19.9) | 113/150 (avg quarter 19.9) |
| NorthAmerica bankrupt / bankrupt-quarter / avg cash / wins | 118/144, 14.5, $14.83M, 15 | 117/144, 14.0, **$19.61M**, **16** |
| Europe bankrupt / avg cash / wins | 80/95, $4.11M, 4 | 80/95, $4.11M, **3** |
| EastAsia / SouthWestAsia / AustralAsia / SouthAmerica | unchanged | unchanged |

NorthAmerica's own average cash rose by a third and it picked up a win
that Europe lost — a real, explainable, if slightly counterintuitive
effect: cutting off a comparatively low-value city (Havana,
`demandWeight` 35, well below the dataset's larger NorthAmerican hubs)
stops AI strategies from sinking capacity into it, freeing that fleet
for higher-value pairs elsewhere in the same region. This is exactly
the kind of causal, legible story CLAUDE.md's own design pillars ask
for — not noise, and not asserted without the number to back it. Every
other region's row is untouched, as expected: none of their own cities
carry this event.

---

## M22 — Campaign integration and re-verification

Wire the generalized, city/region-aware model into the real consumers —
the M13 campaign loop and the QA harness's `FullGameRunner` — replacing
the flat `Timeline.DemandMultiplierForYear(growthSchedule, year)` global-only
call with whichever query shape M20 settled on. This is expected to
legitimately move already-recorded figures, the same way every
city-expansion round did, not something to fight into staying
byte-identical: re-measure the M13 campaign's full trajectory (peak
quarter and value, bankruptcy quarter and value, chokepoint-relevance
checkpoints) and update `docs/SIM_CORE_ROADMAP.md`'s M13 section and
`docs/GAME_SETUP_ROADMAP.md`'s M16/M18/M19 sections with the real,
measured numbers — the same discipline the SouthAmerica/Europe/Asia/
NorthAmerica rounds already established and that this document should be
held to just as strictly.

**Done when:** the QA harness's 150-game sweep passes cleanly against the
new model (0 invariant violations — correctness, not exact figures, is
what that sweep protects, same as every round before it), and both
roadmap docs' pinned figures are re-measured and updated rather than left
stale.

---

## Kept in mind, not scheduled

- **Gate capacity / gateway status changing over time** — a real,
  richer extension of the same idea (an airport's physical capacity
  growing via terminal expansions, or a city that wasn't a gateway at all
  in 1955 becoming one later) that this roadmap deliberately doesn't
  attempt. It's structurally bigger than a demand-weight schedule: a city
  gaining gateway status mid-campaign means a brand-new M9 contested asset
  appearing out of nowhere, which touches `LandingRightsBoard`,
  `RegionalFleet`'s eligible-starting-city logic (M15), and probably the
  "route entry requires a held slot" design note already recorded in
  `docs/GAME_SETUP_ROADMAP.md`'s "Kept in mind" section — a natural
  follow-on to this roadmap, not a part of it.
- **`RegionalEconomics.Factor` staying time-independent — deliberately,
  not an oversight.** It's only ever evaluated once, at campaign setup
  (year 1955, via `StartingConditionsResolver`), to derive starting cash.
  Making city demand weights time-varying doesn't change that — a
  region's *starting* economic strength is still a fixed snapshot at the
  campaign's start year, which is exactly what M16 already computes. If a
  future mechanic ever needs a region's economic strength *mid-campaign*
  (an ongoing subsidy, a wealth-based fare-stance mechanic), that's new
  scope, not something this roadmap's per-city demand work implies on its
  own.
- **AI awareness of historical trends** — explicitly out of scope, same
  boundary the AI-differentiation notes in `docs/GAME_SETUP_ROADMAP.md`
  already draw: `BaselineAi` picks routes by current-quarter demand
  already, so a city with a real growth trajectory will naturally look
  more attractive to it once its weight actually rises — no AI change
  needed for the heuristic to *react* to this roadmap's data. Whether the
  AI should ever *anticipate* a known future trend (buy in early to
  Dubai before it takes off) is a materially different, smarter-AI
  question, filed with the rest of the AI-improvement work, not here.
