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

## M62 — `NetworkPlanner.FindBestHub`: quality + loyalty + substitution ✅

Hub selection was pure shortest-total-great-circle-detour before this
milestone — `FindBestHub` now ranks candidates by an "effective detour"
that also subtracts a quality bonus (the candidate's own `HubQuality`), a
loyalty bonus (this airline's own route-count-share at that city — the
proxy decided in `docs/HUB_DEMAND_DESIGN.md`'s Decisions section, pending
the deferred real hub-designation system tracked in `docs/OPEN_WORK.md`),
and adds a substitution penalty for sitting close to a *different* hub the
airline already treats as loyal (the New York/Boston case). All three
terms are bounded well below realistic intercontinental detour deltas —
they can tilt a genuinely close choice, never make a transcontinental
detour look cheaper than a nearby direct hub.

**Changes:**
- `src/AeroNext.Core/Airlines/NetworkPlanner.cs` — `Plan` precomputes
  `loyaltyShareByCityId` and the sorted `loyalHubCities` list once per
  call (not once per `FindBestHub` call inside the O(cities²) double loop
  — the same class of redundant recomputation `GravityDemandModel`'s own
  M21 fixed once already). `FindBestHub`'s ranking gained the three terms;
  its public API (`Plan(routes, allCities, demandModel)`) is unchanged, so
  every one of the ~10 real call sites across `Core`/`Sim`/`QaHarness`
  needed zero changes.
- Five new `public const double` weights, each with a doc comment stating
  the shared ablation-lever convention ("set to 0.0 to reproduce pre-M62
  pure-detour selection"), matching M45's own established discipline.

**Tests** (`tests/AeroNext.Core.Tests/NetworkPlannerTests.cs`, 5 new):
`Plan_HigherHubQuality_PreferredWhenDetourNearlyEqual`,
`Plan_HubQuality_DoesNotOverrideLargeDetourAdvantage`,
`Plan_LoyaltyBonus_PrefersHubAirlineAlreadyUses`,
`Plan_SubstitutionPenalty_PenalizesHubNearAnAlreadyLoyalHub`,
`RealDataset_CombinedHubBonusStaysBelowASingleIntercontinentalLeg`. All
verified geometrically before writing (a small scratchpad tool computed
real `GreatCircle.DistanceKm` values for every fixture before committing
to specific coordinates, rather than hand-calculating great-circle math).
All ~12 pre-existing `NetworkPlannerTests.cs` tests needed **zero edits** —
confirmed by running them before writing any new code: with `HubQuality =
0` (M61's default) the quality term is always 0, and every pre-existing
multi-candidate fixture happens to be either single-candidate or, in the
one genuine tie-break test, exactly symmetric (equal detour AND equal
route count on both candidates), so the new terms cancel out identically
on both sides rather than needing to be zero.

**A real mid-milestone finding, and the retune it drove:** the first,
more conservative constants (`HubQualityKmPerPoint = 25`,
`LoyaltyBonusMaxKm = SubstitutionPenaltyMaxKm = 800`) passed every unit
test and produced zero sweep regression — but diagnostic instrumentation
on a real 50-game sample found they **never once changed a real hub
choice**: of 529,165 real `FindBestHub` calls, only 1,714 (0.3%) even had
more than one valid connecting candidate, and across all of them the
non-geometric terms flipped the winner **zero times** — even though ~40%
of those close calls had a real `HubQuality` city in the running. Root
cause, confirmed by further instrumentation: whichever hub wins on pure
geometry also tends to already hold the airline's strongest existing
route presence there (network growth is self-reinforcing), so
`LoyaltyBonusMaxKm` was reinforcing the incumbent rather than ever
challenging it — `HubQualityKmPerPoint`, a flat per-city constant immune
to that feedback loop, was the one lever that could realistically
overcome it. Presented to the user as a real decision (ship the safe-but-
inert version, retune more aggressively, or reconsider the loyalty metric
itself) — the user chose to retune. Final constants:
`HubQualityKmPerPoint: 25 → 100` (4×, the disproportionate increase),
`LoyaltyBonusMaxKm: 800 → 1000`, `SubstitutionPenaltyMaxKm: 800 → 1000`.
The real-dataset bound test's own threshold was correspondingly loosened
(from "under half a transatlantic leg" to "under one full transatlantic
leg," ~5,540km) — still backed by the dedicated ~37,000km-margin
does-not-override test, which needed no change at any constant value
tested.

**Verification:**
- `dotnet build`, `dotnet test`: 815/815 passing (810 shipped as of M61 +
  5 new `NetworkPlannerTests`), zero edits to any pre-existing test.
- `dotnet test --collect:"XPlat Code Coverage"`: `NetworkPlanner.cs` at
  100% line-rate and 100% branch-rate. One genuinely dead branch was
  found and removed rather than papered over with a contrived test — a
  defensive `totalRoutes > 0 ? ... : 0.0` guard that can never actually
  run, since the dictionary it guards can only have entries when
  `totalRoutes` is already positive.
- **Retuned-constants 300-game QA sweep**: real, measured behavioral
  change — 446 of 6,406 real multi-candidate `FindBestHub` decisions
  (7.0%) now flip away from pure geometry because of quality, loyalty, or
  substitution. **Macro win-rate stays byte-identical to the M59/M61
  baseline** (117/300, 39.0%, avg quarter 83.5, NorthAmerica 88.9% win
  share, identical per-region breakdown) — expected and correct: M62
  changes *which route* carries a connecting passenger stream, not the
  size of that stream (`ForPair`, M63's own concern), so a real, verified
  routing-level change doesn't move a coarse win/bankruptcy metric. Zero
  regression, real effect, honestly reported as both.

**Done.** Branch `m62-network-planner-hub-selection`, to be merged to
`main`.

## M63 — `GravityDemandModel.ForPair`: bounded, additive, off-by-default hub bonus ⚠️ implemented, real call sites left disabled

The design doc's literal recommended formula, following the exact
`schedule`/`year` off-by-default convention `GravityDemandModel` already
established: a new optional `enableHubDemandBonus = false` constructor
parameter, resolved once into `_hubDemandBonusEnabled`; `ForPair` swaps in
a new `EffectiveWeight(city) = DemandWeight + HubQuality` (when enabled)
for the raw `DemandWeight` on each side of the gravity product. Default-off
proof: every pre-existing call site (all of `GravityDemandModelTests.cs`
included) omits the new param, so `HubQuality` is never read — confirmed
byte-identical, all 822 pre-M63 tests passing unmodified.

**Tests** (7 new, `GravityDemandModelTests.cs`):
`ForPair_HubDemandBonusDisabledByDefault_MatchesPreExistingFormula_ByteIdentical`,
`ForPair_HubDemandBonusEnabled_ButHubQualityZero_MatchesDisabledOutput_ByteIdentical`,
`ForPair_HubDemandBonusEnabled_BoostsPassengersOnHighHubQualitySide`,
`ForPair_HubDemandBonusEnabled_BothSidesHaveHubQuality_MultipliesBothRatios`,
`RealDataset_HubDemandBonus_NeverExceedsBoundedRatio`,
`RealDataset_AllPairs_HubDemandBonus_TotalWorldDemandStaysWithinBoundedMultiplier`,
`RealDataset_HubDemandBonusEnabled_MajorMarketStillDwarfsChokepointPair`.

**Mandatory pre-sweep static verification** (a scratchpad tool, matching
the `m59analysis`/`m62check` pattern) found everything bounded and sane
against the real dataset: max per-pair ratio 2.218 (theoretical cap 2.56,
confirming `CityCatalog`'s own ratio-cap validation), total world demand
inflation 1.157x (a sane, modest figure — contrast M60's reverted
exponent-discount attempt, which measured 31x on the same kind of check).
The MIA/HAV-vs-JFK/MIA relationship the original M60 investigation started
from is **unchanged** by design — M63 was never meant to re-rank
close-pair-vs-hub dominance (that's `NetworkPlanner`'s job, M62).

**A real, serious finding when actually enabled**: `dotnet test`,
coverage (100% line+branch on `GravityDemandModel.cs`), and every static
check all passed — but flipping the 5 real call sites to
`enableHubDemandBonus: true` and running a real 300-game sweep collapsed
the win rate from 39.0% (117/300) to **7.0% (21/300)**, avg win quarter
83.5→59.0, and completely inverted regional dominance (NorthAmerica
88.9%→9.5% win share, EastAsia 6.8%→85.7%). A follow-up 40-game topology
diagnostic (temporary instrumentation on `FullGameRunner`, since reverted)
found the real cause: **hub-and-spoke topology does not emerge** — the
clearest concentration signal (an airline's max route count at any single
city) stayed essentially flat in every region, on or off. What changes is
that AI strategies open far more routes *touching* a hub-quality city
(EastAsia's hub-touching route share: 0.21→0.69) without that translating
into consolidated network structure — confirmed by reading the real
ranking code: `BaselineAi.FindBestUnservedPair` and its live-strategy
equivalents rank every unserved pair in the world by raw
`PassengersPerQuarter`, with zero concept of "prefer a pair that
reinforces a city I already operate from." M62's own loyalty concept only
affects itinerary *routing* through an existing connection, not which
*new* direct route gets opened — a completely different decision point.
EastAsia benefits disproportionately from the reshuffling because it sits
geographically next to several boosted hubs (its own HKG/ICN plus
SouthWestAsia's DXB and AustralAsia's SIN).

**Status: implemented, tested, verified safe in isolation, but the 5 real
call sites are left disabled (`main`'s exact pre-M63 form) pending the
real fix — M64.** This is a genuine, honest interim state, not a silent
regression: shipping M63's code without enabling it costs nothing (it's
provably inert, same guarantee M61 gave `HubQuality` itself) and keeps the
tested, bounded formula on `main` ready for M64 to build on.

**Done, in this qualified sense.** Branch `m63-forpair-hub-demand-bonus`,
merged to `main`.

## M64 — route-opening consolidation bonus: the real fix

See `docs/HUB_DEMAND_DESIGN.md`'s Decisions and the M63 section above for
full context. Not yet started.
