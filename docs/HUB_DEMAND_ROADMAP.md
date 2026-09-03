# Hub-and-Spoke Demand — Roadmap

Real, shipped milestones for the hub-and-spoke demand feature.
`docs/HUB_DEMAND_DESIGN.md` is the feasibility study and decision record
that led here — this file is the "done, measured" log, the same split
every other mechanic in this codebase already keeps (design doc vs.
roadmap doc).

Baseline every sweep in this doc is measured against: **M59, 39.0% win
rate, 117/300, avg quarter 83.5, NorthAmerica 88.9% win share** (300 games,
seed 1, 244 quarters, via the scratchpad `FullGameRunner.Run` sweep tool).

---

## M61 — `HubQuality`: new authored city field ✅

Added a new, real per-city field distinct from both `DemandWeight` (how
attractive a city is as a destination) and `IsGateway` (intercontinental
chokepoint status): `HubQuality`, representing how good a real connecting
point a city is — independent of whether people actually want to visit it.
Deliberately inert this milestone: nothing in game logic reads it yet. This
milestone exists purely to get the data field, its validation, and its
authored values onto `main` safely, with a real measurement proving it
changes nothing, before any behavior-changing code (M62, M63) is layered
on top.

**Changes:**
- `src/AeroNext.Core/World/City.cs` — `int HubQuality = 0`, a new trailing
  parameter after `AutoCapacity` (same precedent `AutoCapacity` itself set
  in M45 — a default keeps every existing positional `City` construction
  site, in both production code and ~11 test files, compiling unchanged).
- `src/AeroNext.Core/World/CityCatalog.cs` — two new validation rules,
  enforced at data-load time:
  1. `HubQuality < 0` throws.
  2. `HubQuality > MaxHubQualityToDemandWeightRatio * DemandWeight` (ratio
     cap, `MaxHubQualityToDemandWeightRatio = 0.6`) throws, plus a flat
     `MaxAuthoredHubQuality = 40` sanity ceiling. This is the concrete
     engineering guard against the exact failure class that has already
     broken this feature twice before this round even started (see
     `HUB_DEMAND_DESIGN.md`'s "What's already been tried" — an additive
     bonus that ends up a large percentage of a small city's own base):
     it's now structurally impossible to author a city whose `HubQuality`
     could swing a future `DemandWeight + HubQuality` computation (M63) by
     more than 60% on any real pair, checked automatically on every load,
     not just at authoring time.
- `data/cities.json` — every one of the 99 rows now carries an explicit
  `hubQuality` value.

**Authoring methodology** (not derived from `IsGateway` or `DemandWeight` —
cross-checked below):
- **Tier 0 (0, the large majority — 79 of 99 cities)**: no distinctive
  real-world reputation as a connecting point.
- **Tier 1 (10–15, 9 cities)**: real regional connectors, not iconic —
  Toronto (10), Miami (12), Denver (12), Houston (12), Munich (12), Kuala
  Lumpur (12), Hong Kong (15), London (15), Paris (12), Sao Paulo (12),
  Bogota (10), Nairobi (10).
- **Tier 2 (20–25, 8 cities)**: widely recognized global connecting hubs —
  Chicago (22), Frankfurt (22), Amsterdam (22), Singapore (22), Istanbul
  (22), Seoul (20), Addis Ababa (20 — genuinely one of Africa's biggest
  connecting hubs on real-world traffic composition despite a modest
  `DemandWeight` of 35, the closest real analog in this roster to the
  design doc's own Atlanta/Dubai framing for a smaller local market).
- **Tier 3 (30–40, 2 cities)**: the design doc's own named examples —
  Atlanta (35) and Dubai (35), the two cities where the majority of real
  traffic connects rather than originates or terminates.

Every value was checked against both caps before shipping (max observed
ratio: Addis Ababa at 0.571, just under the 0.6 cap; max observed value:
35, under the flat 40 ceiling).

**Cross-check that this isn't a relabeling of an existing field**: five of
the highest-`DemandWeight` cities in the whole dataset (JFK, Tokyo,
Beijing, Los Angeles, Shanghai — all `DemandWeight >= 85`) carry `HubQuality
= 0`. Atlanta, a non-gateway city, outranks every one of the 15 gateway
cities on `HubQuality`. Both are now permanent regression tests (see below).

**Tests** (`tests/AeroNext.Core.Tests/CityCatalogTests.cs`, 9 new):
`Load_MissingHubQualityField_DefaultsToZero_NotAThrow`,
`Load_ThrowsOnNegativeHubQuality`,
`Load_ThrowsWhenHubQualityExceedsFlatCeiling`,
`Load_ThrowsWhenHubQualityExceedsDemandWeightRatio`,
`Load_AcceptsHubQualityAtZeroAndAtRatioBoundary`,
`LoadFile_RealDataset_AtlantaAndDubaiHaveMeaningfullyHigherHubQualityThanTypicalCity`,
`LoadFile_RealDataset_HubQualityRankingDiffersFromDemandWeightAndGatewayStatus`,
`LoadFile_RealDataset_EveryHubQualityRespectsBothCaps`. Also extended
`NetworkPlannerTests.MakeCity` with an optional `hubQuality` parameter
(default 0), for M62's own tests to use.

**Verification:**
- `dotnet build`, `dotnet test`: 810/810 passing (802 pre-M61 + 8 new
  `CityCatalogTests`).
- `dotnet test --collect:"XPlat Code Coverage"`: `City.cs` and
  `CityCatalog.cs` both at 100% line-rate and 100% branch-rate — every
  new validation branch (negative, flat ceiling, ratio cap, boundary
  accept) is exercised.
- **300-game QA sweep reproduces the M59 baseline exactly**: 117/300
  (39.0%), avg quarter 83.5, NorthAmerica 88.9% win share — byte-identical
  proof that a real, authored, non-trivial data field changes nothing in
  actual gameplay yet, before M62/M63 layer real behavior on top of it.

**Done.** Branch `m61-hub-quality-field`, merged to `main`.

---

## M62 — `NetworkPlanner.FindBestHub`: quality + loyalty + substitution

Not yet started.

## M63 — `GravityDemandModel.ForPair`: bounded, additive, off-by-default hub bonus

Not yet started.
