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
regional shift measured in the QA sweep), M21 round 8 done (Suez
Canal closure 1967-75, window 3 — a second, longer Cairo episode;
both loops unchanged, a genuine no-effect result), M21 round 9 done
(Camp David Peace Treaty 1979, window 5 — a permanent Cairo boost,
window 4 needed nothing new), M21 round 10 done (Iran-Iraq War
1980-88, window 6 — the sweep's first two-city event, Tehran and
Baghdad both; a small, real SouthWestAsia effect measured), M21 round
11 done (Chernobyl 1986 — the sweep's first Region-scoped event — and
the Berlin Wall falling 1989 as a permanent city Trend, window 7;
Europe's avg cash nearly doubled in the QA sweep), M21 round 12 done
(USSR dissolution 1991, window 8 — a non-monotonic Moscow Trend
(dip-then-recover); the first round to ripple across nearly every
region in the QA sweep, not just one), M21 round 13 done (Hong Kong
Handover 1997, window 9 — deliberately the smallest magnitude in the
sweep; confirmed genuinely mild, no QA-sweep effect despite Hong
Kong's own size and gateway status), M21 round 14 done (SARS 2003 —
the sweep's second Region-scoped event, EastAsia — and the 2003 Iraq
War as a second, permanent, far more severe Baghdad episode, window
10; a real, non-monotonic EastAsia ripple, SouthWestAsia unmoved in
this sample), M21 round 15 done (Eyjafjallajokull 2010 + Arab Spring
2011, window 12 — Cairo's fourth independent event layer; 2015
confirmed needing nothing — **the 13-window sweep is now complete**),
M21 round 16 done (China's opening (Beijing + Shanghai Trends) and the
1997 Asian Financial Crisis — the sweep's last two reserved
candidates; **the largest QA-sweep swing in the whole series**; a
4-attempt magnitude investigation found the swing curve-shape-
insensitive — resolved by keeping the original, historically honest
curve, with the AI's own threshold-sensitivity flagged as future
AI-tuning work), M22 not
started.**
Builds on the completed sim core
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

## M21 — Author real historical trends and events (round 1: architecture + Dubai; round 2: global curve redesign; round 3: trend/event split; round 4: every Olympic Games + a real perf fix; round 5: remaining Games via same-country substitution; round 6: Suez Crisis, window 1 of a 13-window historical sweep; round 7: Cuba embargo, window 2; round 8: Suez Canal closure 1967-75, window 3; round 9: Camp David Peace Treaty 1979, window 5; round 10: Iran-Iraq War 1980-88, window 6; round 11: Chernobyl + Berlin Wall falls, window 7; round 12: USSR dissolution, window 8; round 13: Hong Kong Handover, window 9; round 14: SARS + Iraq War 2003, window 10; round 15: Eyjafjallajokull + Arab Spring, window 12 — sweep complete; round 16: China's opening + Asian Financial Crisis, the last two reserved candidates) ✅

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
- **China's opening (Beijing, Shanghai)** ✅ (round 16) — muted through
  the Maoist/Cultural Revolution era, a real inflection after 1978's
  reforms, continuing to climb through the 1990s–2000s boom to 2015.
  See round 16's own writeup below.
- **A global shock** ✅ (round 2) — redefined from "just the 1973 oil
  crisis" to the whole baseline curve, on the user's own direction: not
  one isolated event layered onto the old smooth 4-point line, but the
  curve's real shape end to end — jet-age growth, the 1973 oil crisis,
  the 1979-81 second shock, the 1991 Gulf War, 2001, and 2008-09 all
  included. See round 2's own writeup below.
- **1997 Asian Financial Crisis** ✅ (round 16) — real and dateable,
  and concentrated enough in impact that it's a region-scoped event
  (`data/historical_events.json`, `Region.EastAsia`) rather than
  diluted into the global curve, exactly as anticipated when it was
  first named during round 2's own research. See round 16's own
  writeup below.
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
| 1965-69 | Six-Day War → Suez Canal closed (1967-75) | **Authored round 8** — Cairo (`cai`), city-scoped, spans into windows 4-5 |
| 1970-74 | (747, Munich/Sapporo Olympics, first oil crisis) | **Confirmed round 9** — already covered by rounds 3-4, nothing new needed |
| 1975-79 | Suez reopens (1975, round 8's own bookend); Camp David / Egypt-Israel peace (1979) | **Authored round 9** — Cairo (`cai`), permanent boost |
| 1980-84 | Iran-Iraq War (1980-88) | **Authored round 10** — `thr` + `bgw`, spans into window 7 |
| 1985-89 | Chernobyl (1986); Berlin Wall falls (1989) | **Authored round 11** — Europe region-wide dip; `ber` trend-shift |
| 1990-94 | USSR dissolves (1991) | **Authored round 12** — `mow`, non-monotonic trend-shift |
| 1995-99 | Hong Kong Handover (1997) | **Authored round 13** — `hkg`, confirmed genuinely mild |
| 2000-04 | SARS (2003); Iraq War (2003) | **Authored round 14** — Region-scope (EastAsia); `bgw` permanent second episode |
| 2005-09 | — | **Confirmed round 15** — already covered by rounds 3-4 (Beijing, financial crisis), nothing new needed |
| 2010-14 | Eyjafjallajökull eruption (2010); Arab Spring (2011) | **Authored round 15** — Europe region-wide shutdown; Cairo's 4th layer |
| 2015 | — | **Confirmed round 15** — campaign's final year, no room for a reversion arc, not worth authoring |

China's opening and the 1997 Asian Financial Crisis were kept reserved
for their own dedicated rounds (named since round 1/2), not folded
into this sweep — both authored in round 16, after the sweep itself
closed. Windows are implemented one at a time, on the user's own
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

### Round 8: the sweep, window 3 (Suez Canal closure, 1967-75) ✅

**Six-Day War → Suez Canal closure:** Israel's victory in the June
1967 war left Egypt closing the canal to shipping, and sustained
regional conflict (the War of Attrition, then the 1973 Yom Kippur War)
kept it shut until UN-supervised clearance let it reopen in June 1975
— a real 8-year chokepoint closure, directly resonant with CLAUDE.md's
own "chokepoints obsolete/move across the campaign" design language,
even though the sim doesn't model canal transit directly. Modeled as
the same real entity as round 6's 1956 Suez Crisis — a second, later,
much longer Cairo-scoped `Event` episode (`suez-closure-1967`), not
folded into the 1956 episode's own group (they're genuinely separate
historical episodes 11 years apart, and grouping them would have made
the 1956 reopening's own 1.0 breakpoint get silently overridden by
whichever breakpoint sorted "latest" — exactly the composition bug
round 3 found and fixed once already).

Magnitude (-25%, `0.75`) sits between the two other Cairo/Havana
events authored so far: more severe than the 1956 Crisis's -20% (a
multi-year war-adjacent state, not a single blocked-canal incident),
but well short of the Cuba embargo's -35% permanent collapse (Cairo
stayed a functioning, reachable destination throughout, unlike a full
trade embargo). 1967 onset, 1975 reversion — still *(proposed,
tunable)*, matching every other magnitude in this series.

New proof test,
`RealDataset_SuezClosure1967_DipsCairoForEightYears_RecoveredBy1980`:
since the global trend curve itself also rises across this exact span
(a step from 1.0x to 2.2x right at 1970), a raw before/after comparison
wouldn't isolate the closure's own effect — so the test instead
compares against a same-year reference schedule with *both* Suez event
groups (1956 and 1967) removed, at 1970 (mid-closure, suppressed
relative to the reference) and again at 1980 (exact equality against
the reference, proving both episodes fully and correctly resolve back
to neutral, not just "recovered somewhat"). 581 tests, up from 580,
all passing; coverage gate re-run and unchanged (same accepted
baseline as every round).

**Measured — both loops came back genuinely unchanged, a real result
in both directions for different reasons.** The M13 loop stayed
byte-identical to round 7 (expected: it resolves and stops printing by
quarter 10, 1957 — a decade before this event even starts). The
150-game QA sweep, which does run the full campaign to 2015, also came
back byte-for-byte identical to round 7's own numbers (113/150 wins
avg quarter 19.9; every regional row unchanged, NorthAmerica still
117/144 bankrupt $19.61M avg cash 16 wins) — the same honest "no
effect" result round 6 found for the 1956 Crisis, for the same reason:
Africa isn't among the home regions any of the 4 competing airlines
draw in these particular 150 seeded games, so neither Cairo event has
a route to move. Two Cairo-scoped events now compose correctly on top
of each other whenever a game *does* touch Africa — confirmed by the
proof test's own reference-schedule comparison, just not visible in
this particular sweep's own random draws.

### Round 9: the sweep, window 4 confirmed empty, window 5 (Camp David Peace Treaty, 1979) ✅

**Window 4 (1970-74) needed nothing new** — checked directly against
the sweep table rather than skipped on assumption: the 747 (round 3),
Munich and Sapporo (round 4), and the first oil crisis (round 3)
already cover this window's own real history. Confirmed, not assumed,
and recorded in the table above so the sweep's own gap-free coverage
stays visible.

**Egypt-Israel Peace Treaty:** signed March 1979, following the
September 1978 Camp David Accords — Egypt becomes the first Arab state
to formally recognize Israel, ending the state of war between them.
A genuine, permanent structural improvement in Cairo's own stability,
not a shock with a recovery arc — modeled the same permanent shape as
the 747 and the Cuba embargo: a single `Event` breakpoint
(`camp-david-1979`), no reversion, ever. +15% (`1.15`), matching the
747's own magnitude for the same reason both are used here: a
structural, permanent step, not a temporary spike sized like the
Olympics' own +20%.

This is Cairo's **third** independent event group (alongside the 1956
Suez Crisis and the 1967-75 closure) — a real exercise of the
composition architecture round 3's own bug fix protects: three
separate historical episodes on the same city, each resolved and
reverted (or not) entirely independently, then multiplied together.

New proof test,
`RealDataset_CampDavidPeace1979_PermanentlyBoostsCairo_NeverReverts`:
confirms 1978 (pre-treaty) matches a same-year reference with the
treaty's own event group removed exactly (proving the two Suez
episodes alone drive 1978's value, nothing new yet), 1979 boosts above
1978, and the boost stays pinned at the authored 1.15 ratio as late as
2015 against the same kind of event-free reference used for the Cuba
embargo's own permanence proof. 582 tests, up from 581, all passing;
coverage gate re-run and unchanged (same accepted baseline as every
round).

**Measured — both loops unchanged again, the third consecutive genuine
no-effect result on Cairo for the same reason as rounds 6 and 8: none
of these particular 150 seeded games' 4 competing airlines call Africa
home.** M13 stayed byte-identical to round 8 (resolves by 1957, 22
years before this event). The 150-game sweep also came back
byte-for-byte identical to round 8's own numbers (113/150 wins avg
quarter 19.9; every regional row unchanged, NorthAmerica still 117/144
bankrupt $19.61M avg cash 16 wins). Expected, not a concern — Africa
genuinely isn't touched by this particular sweep's random seed draws,
and the effect's correctness is already proven directly by the new
proof test's own before/after and reference-schedule comparisons.

### Round 10: the sweep, window 6 (Iran-Iraq War, 1980-88) ✅

**Iran-Iraq War:** Iraq invades Iran in September 1980; the war grinds
on for eight years — including the "War of the Cities" ballistic
missile campaign that directly targeted Tehran and Baghdad — until the
UN-brokered ceasefire (Resolution 598) in August 1988. This is the
sweep's **first event to touch two cities at once**: Tehran (`thr`)
and Baghdad (`bgw`), both real dataset cities, both in SouthWestAsia.
Modeled as two city-scoped `Event` entries sharing one `EventGroup`
name (`iran-iraq-war-1980`) — safe to share, since `GravityDemandModel`
resolves each city's own event layer from only that city's own
schedule entries; there's no cross-city grouping to worry about, only
same-city grouping (which is what `EventGroup` protects against
within one scope).

Magnitude -35% (`0.65`) for both cities, matching the Cuba embargo's
own severity class (a real war with sustained bombardment of both
capitals, more severe than the Suez closure's -25% regional tension)
but with an explicit 1988 reversion, since — unlike Cuba — the war
did actually end within the campaign's own timeframe. Same uniform
magnitude for both cities, deliberately, matching this series' own
established convention of not over-fitting per-city severity.

New proof test,
`RealDataset_IranIraqWar_DipsBothTehranAndBaghdad_RecoveredAfter1988Ceasefire`,
loops over both cities. **A real authoring bug was caught during its
own first run, not shipped**: the initial "before" year (1979)
happened to land exactly on the global second oil shock's own onset
breakpoint (`oil-shock-1979`, an unrelated event authored back in
round 3) — an entirely coincidental year collision between two
independently-researched real events, not a schedule bug. The test
failed with a real, correctly-computed numeric mismatch (not a crash),
which is exactly what a same-year "before" reference is supposed to
catch. Fixed by moving the "before" year to 1977, clear of every other
event's own window, and re-verified the fix passes for both cities.
583 tests, up from 582, all passing; coverage gate re-run and
unchanged (same accepted baseline as every round).

**Measured — M13 unchanged (resolves by 1957, 23 years before this
war starts), but the 150-game sweep moved for real this time, small
and concentrated exactly where expected:**

| Metric | Round 9 | Round 10 |
|---|---|---|
| Clean games with a win | 113/150 (avg quarter 19.9) | 113/150 (avg quarter 19.9) |
| SouthWestAsia bankrupt / bankrupt-quarter / wins | 29/55, 16.7, 17 | 29/55, 16.7, 17 |
| SouthWestAsia avg cash | $113.44M | $112.66M (-0.7%) |
| NorthAmerica / Europe / EastAsia / AustralAsia / SouthAmerica | unchanged | unchanged |

A small, real, honestly-reported effect — not a "no effect" result
like rounds 6, 8, and 9, but not a dramatic one either: Tehran and
Baghdad are comparatively minor cities within a region whose AI
strategies aren't centered on them (unlike Havana's own outsized
effect on NorthAmerica in round 7), so the dip shows up only in the
region's average cash, not in its bankruptcy rate or win count. Exactly
the kind of proportionate, explainable movement this sweep is meant to
produce.

### Round 11: the sweep, window 7 (Chernobyl 1986; Berlin Wall falls 1989) ✅

**Chernobyl — the sweep's first Region-scoped event.** The April 1986
nuclear disaster's fallout spread across much of Europe, and the
resulting fear-driven dip in inbound tourism was genuinely
continent-wide, not centered on one city (unlike every prior episode
in this sweep) — modeled directly on `Region.Europe` rather than
picked city-by-city. -15% (`0.85`), reverting in 1987 as fears
subsided — a real, moderate, short-lived shock, matching the sweep's
own "sharp dip, quick recovery" convention (Suez 1956, Olympics), not
the multi-year severity of a war or embargo.

**Berlin Wall falls — a permanent city Trend, not an Event.** The
November 1989 fall, followed by formal reunification (Oct 1990) and
the government's relocation from Bonn to Berlin (complete 1999), is a
genuine multi-decade structural shift in the city's own significance —
the same classification precedent as Dubai's own 4-point trajectory,
not a shock with a recovery arc. Four points spanning the full
campaign: `1955: 0.7` (a divided Cold War city, Wall built 1961,
isolation constrains travel), `1989: 1.0` (Wall falls, reunification
begins), `2000: 1.4` (capital of unified Germany), `2015: 1.8`
(established major European capital) — modest magnitudes throughout,
deliberately: Berlin's real story is "a divided city rejoining the
world," not Dubai's "empty desert to global mega-hub," so it shouldn't
be sized like one.

Two new proof tests. `RealDataset_Chernobyl1986_DipsEuropeOnly_
RecoveredBy1988` confirms the dip and full recovery for a Europe pair
(London, chosen since it carries no other city-scoped entry active in
this window), *and* confirms a same-region South America-South
America pair doesn't move at all across the same years — proving the
effect is genuinely region-scoped, not accidentally global.
`RealDataset_BerlinWallFalls1989_PermanentlyBoostsBerlin_
GrowingThroughReunification` confirms a real, monotonic rise across
the divided-era/reunification/reunified-capital arc. 585 tests, up
from 583, all passing; coverage gate re-run and unchanged (same
accepted baseline as every round).

**Measured — and this round is a genuine first: Berlin's own Trend
starts at 1955, quarter 1 of the campaign, unlike every prior round's
event (all starting well after the M13 loop's own quarter-10 cutoff)
— so "M13 unchanged" here is a real measurement, not a timing
triviality.** Confirmed byte-identical to round 10 anyway: Berlin
isn't touched by this loop's own specific early routes. The 150-game
sweep moved for real, and substantially, in Europe:

| Metric | Round 10 | Round 11 |
|---|---|---|
| Clean games with a win | 113/150 (avg quarter 19.9) | 113/150 (avg quarter 19.9) |
| Europe bankrupt / bankrupt-quarter / wins | 80/95, 11.7, 3 | 79/95, 11.5, 3 |
| Europe avg cash | $4.11M | **$7.69M** (+87%) |
| NorthAmerica / EastAsia / SouthWestAsia / AustralAsia / SouthAmerica | unchanged | unchanged |

Europe's own average cash nearly doubled. A real, explainable result:
Berlin's Trend spans the *entire* campaign with a sustained long-run
boost (0.7→1.8 across 60 years), while Chernobyl's own dip is a brief
2-year blip — the permanent structural story dominates the aggregate
statistic, exactly as its own much larger scope and duration would
predict. Every other region's row is untouched, as expected: none of
their own cities or regions carry either event.

### Round 12: the sweep, window 8 (USSR dissolution, 1991) ✅

**Moscow's real trajectory is genuinely non-monotonic** — a first for
this series. Every prior city Trend (Dubai, Berlin) climbs cleanly;
Moscow doesn't, because the real history doesn't: the USSR's December
1991 dissolution opened borders politically, but the immediate "shock
therapy" collapse (hyperinflation, roughly a 40% GDP contraction)
made Moscow *less* attractive for a while, not more, before Putin-era
stabilization (~2000) let real growth begin. Same classification as
Berlin — a pure city Trend, no Event layer — but shaped dip-then-recover:
`1955: 0.6` (Soviet-era Intourist/Aeroflot restrictions), `1991: 0.5`
(dissolution — the economic collapse outweighs the newly opened
borders), `2000: 1.0` (Putin-era stabilization), `2015: 1.4`
(established major destination).

New proof test,
`RealDataset_MoscowUssrDissolution_DipsIn1991ThenRecoversAndGrows`:
a raw before/after passenger comparison at 1991 actually **failed on
its own first run** — 1991 came back *higher* than 1955, not lower,
because the global trend curve (1.0x→2.2x by 1970) and the Gulf War's
own global Event (also active in 1991, entirely unrelated) both move
independently across these years and swamped the city-specific dip.
The same confound the Iran-Iraq test caught in round 10, this time on
the "before/after" comparison itself rather than the year choice.
Fixed the same way as the Cuba embargo's own permanence proof: isolate
Moscow's own ratio at each year against a same-year reference with its
schedule entries removed, then compare *those* ratios across
1955/1991/2000/2015 — confirms the dip, then two stages of recovery.
586 tests, up from 585, all passing; coverage gate re-run and
unchanged (same accepted baseline as every round).

**Measured — M13 unchanged again (Moscow's Trend, like Berlin's,
starts at 1955, so this is a real check, not a timing triviality), but
the 150-game sweep produced this series' first genuinely diffuse
result — movement in every region but one, not an isolated regional
shift:**

| Metric | Round 11 | Round 12 |
|---|---|---|
| Clean games with a win | 113/150 (avg quarter **19.9**) | 113/150 (avg quarter **19.6**) |
| Total bankrupt games | 143 | 144 |
| NorthAmerica bankrupt / wins | 117/144, 16 | 116/144, 16 |
| Europe bankrupt / bankrupt-quarter / avg cash / wins | 79/95, 11.5, $7.69M, 3 | 84/95, 10.4, **$4.55M**, 3 |
| EastAsia bankrupt / wins | 14/142, 88 | 14/142, **89** |
| SouthWestAsia bankrupt / wins | 29/55, 17 | 29/55, **16** |
| AustralAsia bankrupt / bankrupt-quarter | 22/45, 11.0 | 21/45, 10.7 |
| SouthAmerica | unchanged | unchanged |

**Reported honestly rather than smoothed over: this is a different
kind of result than any prior round, and the reason is structural, not
a bug.** Moscow is a moderately weighted city (`demandWeight` 70) whose
own trajectory now swings both down (the 1991 dip, deeper than even
the constrained Soviet baseline) and up (real growth by 2015) —
touching AI route-planning incentives in Europe throughout the entire
back half of the campaign. Over a 244-quarter simulation where each
quarter's AI decisions compound into the next, a single mid-campaign
perturbation to one city's demand curve can cascade into materially
different aggregate outcomes well beyond that city's own region —
which is exactly what a 244-quarter deterministic-but-chaotic
simulation should do with a real structural change, not evidence of a
scoping error (Moscow's own event-composition correctness is already
proven directly by the proof test's own reference-schedule ratios,
independent of any of these downstream aggregate numbers). Europe's
own avg cash dropped back down near round-10 levels ($7.69M→$4.55M) as
Moscow's dip partly offsets Berlin's own boost within the same region;
EastAsia and SouthWestAsia's win counts each moved by exactly one game
out of 150 — small, real, and plausible given how close a margin
separates them already.

### Round 13: the sweep, window 9 (Hong Kong Handover, 1997) ✅

**Deliberately the smallest magnitude authored anywhere in this
sweep**, on purpose: the July 1997 transfer of Hong Kong's sovereignty
to China was real, but genuinely mild in its own effect on travel
demand — the city's role as a global aviation and financial hub
continued essentially uninterrupted through the transition. The
original sweep-planning table flagged this candidate as "historically
mild," and this round is where that flag gets tested against real
numbers rather than left as an assertion. -8% (`0.92`), a single year
(1997), reverting in 1998 — deliberately kept separate from the
unrelated, same-year Asian Financial Crisis, which stays its own
reserved future round (named since round 1, not folded in here just
because the years coincide).

New proof test,
`RealDataset_HongKongHandover1997_MildDip_RecoveredBy1999`: both 1996
and 1998 fall within the global trend's own flat 1990-2015 step, and
neither touches any other authored event's own window, so a direct
before/during/after comparison isolates the effect cleanly, no
reference-schedule trick needed this time. 587 tests, up from 586, all
passing; coverage gate re-run and unchanged (same accepted baseline as
every round).

**Measured — and this round's own real finding is a negative result,
reported as such rather than smoothed over: M13 unchanged (resolves by
1957, 40 years before this event), and the 150-game sweep came back
byte-for-byte identical to round 12's own numbers, despite Hong Kong
being one of the highest-weighted cities in the dataset
(`demandWeight` 85) and a 3-slot gateway in EastAsia, this sweep's
single dominant region.** Confirms the "historically mild" flag
directly: an 8%, single-year dip is simply too small to register
against a region whose AI strategies are already winning 88-89/150
games by a wide margin — the same mechanism, sized correctly small,
produces correctly small (here, invisible-at-this-sample-size)
aggregate consequences. Not a bug, and not evidence the event was
wasted effort: its own correctness is proven directly by the proof
test, independent of whether this particular 150-game sample happens
to surface it.

### Round 14: the sweep, window 10 (SARS + Iraq War, 2003) ✅

**SARS — the sweep's second Region-scoped event.** The Nov 2002-July
2003 outbreak produced a sharp, short-lived collapse in East Asian air
travel during its peak months — genuinely pan-regional (originating in
Guangdong, spreading through Hong Kong, mainland China, Singapore,
Taiwan, Vietnam), the same real shape as Chernobyl's own diffuse fear
effect, just concentrated in EastAsia instead of Europe. -25%
(`0.75`), a single year (2003), reverting in 2004 as the WHO declared
the outbreak contained — sized alongside the Suez closure's own -25%
in this sweep's severity ladder (acute but resolved within the
campaign's own granularity), sharper than Chernobyl's -15% diffuse
fear, given real accounts of the outbreak's own more severe, more
concentrated impact on regional aviation specifically.

**Iraq War 2003 — Baghdad's second, independent, far more severe
episode.** The March 2003 invasion and its aftermath (occupation,
insurgency, ISIS's 2014 gains) never resolve within the campaign's own
2015 end year, unlike the 1980-88 war's clean ceasefire — modeled as a
second `EventGroup` (`iraq-war-2003`) on the same city, composing
independently on top of the already-reverted `iran-iraq-war-1980`
group, the same multi-independent-layer pattern the Cairo events
already established. -50% (`0.50`), permanent — the harshest magnitude
authored anywhere in this sweep, reflecting a near-total collapse of
normal travel/business activity, not merely a severe dip.

Two new proof tests, both using the ratio-against-same-year-reference
pattern established in rounds 10 and 12 (global events active in this
same window — 9/11 spans 2001-2005 — would otherwise confound a raw
before/after comparison, but cancel cleanly in a same-year ratio).
`RealDataset_Sars2003_DipsEastAsiaOnly_RecoveredBy2004` also confirms
a same-region South America control pair doesn't move, proving genuine
region-scoping. `RealDataset_IraqWar2003_PermanentlyCollapsesBaghdad_
SeparateFromThe1980sWar` confirms the new group is neutral before 2003
(the 1980s war having already reverted), suppressed from 2004, and
still pinned at the authored 0.50 factor as late as 2015. Both passed
on their first run — the ratio pattern paying off after two rounds of
catching real confounds the hard way. 589 tests, up from 587, all
passing; coverage gate re-run and unchanged (same accepted baseline as
every round).

**Measured — M13 unchanged (resolves decades before either event),
but the 150-game sweep produced a striking, real, non-monotonic
EastAsia result, while SouthWestAsia showed no aggregate movement at
all despite Baghdad's own harsh new event:**

| Metric | Round 13 | Round 14 |
|---|---|---|
| Clean games with a win | 113/150 (avg quarter 19.6) | 113/150 (avg quarter 19.6) |
| EastAsia bankrupt / bankrupt-quarter / avg cash / wins | 14/142, 23.0, $140.60M, 88 | 16/142, **46.4**, $133.67M, **89** |
| SouthWestAsia | unchanged | unchanged |
| NorthAmerica / Europe / AustralAsia / SouthAmerica | unchanged | unchanged |

EastAsia's own bankrupt-quarter more than doubled (23.0→46.4) — the
airlines in that region that do fail are now failing much later on
average — alongside two more bankruptcies (14→16) and a *higher* win
count (88→89), a mixed, non-monotonic signal rather than a clean
"worse across the board" one, the same character as round 12's Moscow
ripple: a real structural perturbation to a dominant region's demand
curve, propagating through 244 quarters of compounding AI decisions,
doesn't have to move every statistic in the same direction to be a
real, correctly-computed effect. SouthWestAsia staying flat despite
Baghdad's own -50% permanent collapse is the same "correctness proven
directly, invisible at this sample size" result as Hong Kong in round
13 — Baghdad, like Tehran, is a comparatively minor city within a
region whose AI strategies aren't centered on it.

### Round 15: the sweep, window 11 confirmed empty, window 12 (Eyjafjallajökull + Arab Spring), 2015 confirmed empty — sweep complete ✅

**Window 11 (2005-09) needed nothing new** — checked directly, same
discipline as window 4 in round 9: the Beijing Olympics (round 4) and
the financial crisis (round 3) already cover it.

**Eyjafjallajökull — the sweep's third Region-scoped event, and the
first purely aviation-driven one.** The April 2010 eruption shut down
European airspace for over a week — unlike Chernobyl's fear-driven
tourism dip or SARS's outbreak-driven collapse, this was flight
operations themselves being grounded. -12% (`0.88`), a single year
(2010), reverting in 2011 — a real but brief disruption, spread across
a whole year's demand at this sim's own annual granularity, so sized
more modestly than the day-level severity might suggest.

**Arab Spring — Cairo's fourth independent event layer**, alongside
the 1956 Suez Crisis, the 1967-75 closure, and the 1979 Camp David
boost — the deepest real exercise yet of the multi-independent-group
composition architecture round 3's own bug fix protects. Mubarak's
ouster (Jan-Feb 2011) and the sustained political instability that
followed (transitional military rule, the 2013 coup) left Egyptian
tourism collapsed without meaningful recovery within the campaign's
own 2015 end year — modeled the same permanent shape as the 2003 Iraq
War: -30% (`0.70`), no reversion.

**2015 confirmed needing nothing** — the campaign's own final year,
no room for a reversion arc, same conclusion the original
sweep-planning table already reached, now checked directly rather
than left an assertion.

**This closes the entire 13-window sweep** first built in round 6:
every one of the 61 campaign years (1955-2015) has now been checked
against real, travel-relevant history, with 9 windows authored (1-3,
5-10, 12) and 3 confirmed needing nothing (4, 11, and the 2015 tail).

Two new proof tests, both using the ratio-against-same-year-reference
pattern (2010 falls inside the unrelated global financial crisis's own
2009-2011 window, another confound this pattern cancels automatically)
— both passed on their first run, the fourth round in a row this
pattern has worked cleanly on the first try.
`RealDataset_Eyjafjallajokull2010_DipsEuropeOnly_RecoveredBy2011` and
`RealDataset_ArabSpring2011_PermanentlyCollapsesCairo_
FourthIndependentLayer`. 591 tests, up from 589, all passing; coverage
gate re-run and unchanged (same accepted baseline as every round).

**Measured — both loops came back byte-for-byte identical to round
14's own numbers, a genuine "no effect" result for both events at this
sample size.** M13 unchanged (resolves decades before either event).
The 150-game sweep: Europe's own row stays flat despite
Eyjafjallajökull, and Cairo's fourth event layer doesn't move Africa's
numbers either — the same pattern rounds 6, 8, and 9 already
established for Cairo (Africa isn't among the home regions these
particular 150 seeded games draw), and the same "correctness proven
directly, not visible in this sample" story as Hong Kong (round 13)
and Baghdad's own 2003 event (round 14).

### Round 16: China's opening + the 1997 Asian Financial Crisis — the last two reserved candidates ✅

**China's opening.** Deng Xiaoping's reforms (Third Plenum, Dec 1978)
through Special Economic Zones (Shenzhen, 1980), the 1992 "Southern
Tour" that broadened reform nationally, WTO accession (2001), and
continued growth to 2015 — the same real story authored as a permanent
city Trend on **both** real Chinese cities in the dataset, Beijing
(`pek`) and Shanghai (`pvg`), the same multi-city/same-shape pattern
already established for the Iran-Iraq War. Unlike Berlin's or Moscow's
comparatively modest magnitudes, this one deliberately climbs to
`3.5x` by 2015 — closer to Dubai's own outsized story than to Berlin's
or Moscow's, because China's real economic transformation over these
60 years was genuinely one of the most dramatic in modern history, not
a comparably modest structural shift. Beijing is also this series'
**first real case of a Trend and an Event composing on the same
city** — its own China-opening Trend alongside its existing 2008
Olympics Event — confirmed directly by a dedicated proof test rather
than assumed from the global-scope tests alone.

**1997 Asian Financial Crisis** — the sweep's fourth Region-scoped
event, on `Region.EastAsia`. The Thai baht's collapse (July 1997)
spread through Indonesia, South Korea, and Malaysia; recovery, aided
by IMF programs, took about two years. -20% (`0.80`), 1997-1999 —
deliberately kept as its own independent `EventGroup`
(`asian-financial-crisis-1997`), separate from the same-year Hong Kong
Handover (`hong-kong-handover-1997`), since they're genuinely
different real causes that happen to share a year; Hong Kong itself
now carries both, composing independently.

Three new proof tests.
`RealDataset_ChinasOpening_GrowsBeijingAndShanghai_AcrossReformEra`
loops both cities, isolating each one's own Trend via a same-year
reference with only that city's Trend entries removed (Beijing's own
Olympics Event stays in the reference, so only the Trend layer is
isolated). `RealDataset_BeijingTrendAndOlympicsEvent_
ComposeIndependently` confirms the Olympics' own authored 1.2x is
still exactly recoverable on top of whatever the Trend alone
contributes in 2008. `RealDataset_AsianFinancialCrisis1997_
DipsEastAsiaOnly_RecoveredBy1999` also confirms Hong Kong is
suppressed *further* by the region-wide crisis on top of its own
Handover dip — proof that the two independent groups compose rather
than one clobbering the other. All three passed on their first run.
594 tests, up from 591, all passing; coverage gate re-run and
unchanged (same accepted baseline as every round).

**Measured — M13 unchanged again (both cities' Trends start at 1955,
so this is a real check, not a timing triviality; confirmed
byte-identical to round 15 regardless). The 150-game sweep, though,
produced by far the largest swing in this entire 16-round series —
reported in full, not smoothed over, and flagged to the user rather
than quietly accepted:**

| Metric | Round 15 | Round 16 |
|---|---|---|
| Clean games with a win (avg quarter) | 113/150 (19.6) | **98/150 (23.1)** |
| Total bankrupt games | 144 | 145 |
| NorthAmerica bankrupt / avg cash / wins | 116/144, $19.64M, 16 | 123/144, **$59.12M**, 14 |
| Europe bankrupt / avg cash / wins | 84/95, $4.55M, 3 | 86/95, **$9.25M**, 6 |
| EastAsia bankrupt / avg cash / wins | 16/142, $133.67M, **89** | 24/142, $46.83M, **63** |
| SouthWestAsia bankrupt / avg cash / wins | 29/55, $112.54M, 16 | 32/55, **$30.30M**, 18 |
| AustralAsia bankrupt / avg cash / wins | 21/45, $92.88M, 7 | 22/45, $67.75M, **16** |
| SouthAmerica bankrupt / avg cash / wins | 62/119, $63.61M, 19 | 58/119, $93.58M, **34** |

Every region moved, several dramatically: EastAsia's own win count
collapsed by nearly a third (89→63) even as its bankruptcy rate ticked
up, while SouthAmerica's wins nearly doubled (19→34) and AustralAsia's
more than doubled (7→16) — and the overall clean-win rate itself
dropped from 113/150 to 98/150, meaning **15 more games out of 150 now
end without any airline reaching the win condition** within the
244-quarter campaign. This is a real, explainable, but genuinely
different *kind* of result than any prior round: China's opening
applies a large, multi-decade multiplier to two of the highest-weight
cities in the entire dataset (Beijing 95, Shanghai 90), inside the
region that already dominated the sweep — reshaping the relative
economic balance of the whole simulated world rather than perturbing
one region's own local statistics. Correctness at the per-pair level
is proven directly by the proof tests above, independent of this
aggregate result. Flagged to the user rather than decided
unilaterally; the investigation and resolution below.

**Magnitude investigation.** Asked to dial the swing down, the first
attempt (rescaling both the 1955 floor and the 2015 peak together,
`0.5→1.7`) made the *aggregate* result meaningfully worse, not
better (win rate 98→94, EastAsia bankruptcies 24→73/142) — a genuine
surprise that warranted diagnosis before any more guessing. Traced via
a temporary instrumented run of `FullGameRunner` (added, measured,
then reverted cleanly via `git checkout`) comparing game 0's
`EastAsia Air` quarter-by-quarter under both schedules: by quarter 6
of 1956, its fleet was already 6 aircraft under the "toned down"
schedule vs 5 under the original — because `0.5` is a *higher* 1955
starting value than the original `0.3`, the dialed-back version
actually made Beijing/Shanghai *more* attractive during the
campaign's formative early quarters, not less, triggering more
aggressive debt-financed expansion that a lower eventual ceiling
(`1.7` vs `3.5`) couldn't sustain. **The late-game peak turns out to
barely matter — the early 1955 floor is what drives the aggregate
swing**, confirmed by a second attempt that kept the original `0.3`
floor with only the peak lowered (`0.3→1.7`): it landed nearly
identical to the *original* `0.3→3.5` curve (98/150, EastAsia 24/142
bankrupt, 63 wins — both numbers matching to within a few dollars of
average cash), while the `0.5`-floor attempt alone was the outlier.

A third attempt raised the floor toward neutral as directed (`0.6`,
closer to 1.0 than either prior try) with a modest peak (`1.6`) — this
did **not** reduce the disruption either: win rate stayed at 98/150,
but EastAsia's own bankruptcy count came back *worse* than the
original curve (56/142 vs 24/142), moving unpredictably rather than
monotonically as the curve was gentled. Across all four real
attempts, the win rate stayed pinned in a narrow 94-98/150 band
regardless of curve shape, while EastAsia's own bankruptcy count swung
without a consistent direction (16 baseline → 24 → 73 → 24 → 56) —
the signature of real threshold-sensitivity in the AI strategy logic
(which route/fleet decisions cross which profitability thresholds in
which quarters), not a dial that predictably shrinks with a gentler
curve. Presented in full rather than continuing to guess at more
shapes; **resolved by keeping the original, historically honest curve
(`0.3→3.5`)** — it ties for the mildest EastAsia impact of the four
real attempts, and toning the peak down bought nothing once the 1955
floor was fixed. The live repo data was returned to exactly its
originally-committed round 16 state (confirmed via `git diff`, no new
data commit needed).

This is worth carrying forward explicitly, not just resolving and
forgetting: **the AI's sensitivity to a major structural Trend on a
high-weight city is a genuine open question**, most useful to a future
AI-tuning milestone rather than to this one, which is about authoring
correct historical data, not rebalancing `BaselineAi`/`RapidExpansionStrategy`'s
own decision thresholds.

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
