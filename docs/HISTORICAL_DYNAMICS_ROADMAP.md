# Historical Dynamics Roadmap

**Status: not started.** Builds on the completed sim core
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

## M20 — Generalize the demand schedule to region and city scope

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

---

## M21 — Author real historical trends and events

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

- **Dubai** — near-zero relevance through the 1970s, a real inflection
  point once its free-trade-zone/aviation-hub strategy took hold in the
  1980s–90s, continuing to climb sharply through the 2000s. The single
  clearest "this is exactly the gap this roadmap exists to close" case,
  named directly in the conversation that started this roadmap.
- **China's opening (Beijing, Shanghai)** — muted through the Cultural
  Revolution era, a real inflection after 1978's opening-up reforms,
  continuing to climb through the 1990s–2000s boom.
- **A global shock** — the 1973 oil crisis is the obvious first
  candidate: a real, dateable, global (not city-specific) demand
  contraction, testing the *global*-scope path of the same mechanism
  M20 built, not just city/region scopes.
- Further candidates deliberately left open rather than pre-committed —
  each should be picked and authored the way each aircraft/city round
  was: one real, well-documented story at a time, verified against the
  campaign's actual measured output before being called done, not
  batch-authored speculatively.

**Done when:** at least one city/region-scoped story and the one
global-scope story are authored, each with a real, checkable historical
basis (not an invented number), and the M13 campaign's measured trajectory
visibly reflects them — e.g., Dubai's route(s) carrying negligible traffic
early and materially more by the campaign's later decades, confirmed by
actually running it and reading the numbers, not assumed from the input
data alone.

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
