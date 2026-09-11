# SouthAmerica tuning — exploratory experiment plan

Status: **plan only, nothing run.** Sibling investigation to
`docs/AUSTRALASIA_TUNING_EXPERIMENTS.md` (M65) — same rigor (OFAT ->
factorial -> confirmation, pillar-checked), a **different problem shape**,
so only two of the four AustralAsia factors carry over. Read the "Why S and
A are out of scope" section before proposing either back in.

## The problem being tuned

Canonical 300-game baseline (seed 1, 244 quarters, post-M65):

| Region | Seats | Bankruptcy | Win share | Dominance-win fraction |
|---|---|---|---|---|
| SouthAmerica | 237 | **1.3%** | **4.3%** | **38%** (5/13) |
| AustralAsia (post-M65, for contrast) | 97 | 5.2% | 4.3% | 85% |
| EastAsia | 290 | 4.5% | 48.0% | 91% |
| Europe | 197 | 7.1% | 5.3% | 6% |

**This is not a bankruptcy problem** — 1.3% is the second-best rate of any
region, and `RegionalEconomics.Factor` for SouthAmerica is 1.081 (rich; contrast
AustralAsia's pre-fix 0.755). It's a **competitiveness ceiling**, the same
shape as Europe's: solvent, but the region's win share (4.3%) doesn't come
close to matching its seat share (237/1200 = 19.75%), and more than 60% of
what wins it does get are the weak end-of-campaign-ranking kind, not real
3-region dominance.

### Root cause: Rio de Janeiro is geographically isolated within its own region, and it's 67% of the sample

By starting city, split by how the seat was generated (filler-AI seats
always pick the region's highest-weight eligible city deterministically;
"QA Human" seats pick uniformly at random — separable by airline name alone,
see `GameSetup.CreateSeats` / `GameConfigGenerator.Generate`; this rules out
"filler games have a systematically tougher field" as the explanation):

| City | Role | Seats | Win rate |
|---|---|---|---|
| **gig (Rio)** | filler-AI | 138 | **0.7%** |
| **gig (Rio)** | random-human | 21 | **0.0%** |
| bog (Bogotá) | random-human | 17 | 29.4% |
| lim (Lima) | random-human | 17 | 23.5% |
| eze (Buenos Aires) | random-human | 20 | 10.0% |
| scl (Santiago) | random-human | 24 | 4.2% |

Rio loses at essentially the same rate regardless of *how* it entered the
game — a real geographic fact, not a sampling artifact — and because it's
the region's default AI-filler city (highest non-gateway demand weight, 60),
it accounts for two-thirds of all SouthAmerica seats. One structurally weak
city is dragging the whole region's aggregate down.

**Why Rio specifically** (verified via a scratchpad demand/profit calculator,
reverted after use): the rest of SouthAmerica clusters tightly —
Buenos Aires↔Santiago (1,126 km, 17,881 pax/qtr, $762K/qtr best pair);
Bogotá↔Lima (1,892 km, 6,809 pax/qtr, $542K/qtr best pair). Rio sits alone
on the Atlantic side, 1,996–4,536 km from every other SouthAmerica city, and
its own best domestic pair (Buenos Aires, $346K/qtr) is 35–55% weaker than
what every other city enjoys with its own best partner. Rio's only real
local strength is São Paulo (`gru`, its home gateway) at 337 km and 345,099
pax/qtr — but that's contested intercontinental slot infrastructure per M15
("gateway cities stay inside the ordinary contested mechanic for everyone,
including the local airline"), not free domestic capacity Rio gets to keep.

Side finding, same shape as the pre-M65 AustralAsia guaranteed-route issue:
`StartingConditionsResolver`'s V2 fix keeps the heuristic Q1 pick whenever
it's already profitable, so Buenos Aires and Santiago's own guaranteed
routes both settle for an Il-14 earning $330,861/qtr — the real best pair
between them (DC-6, $762,008/qtr) is available but never picked because the
weaker one clears the "≥ $0" bar first. Not the main driver of the win-share
gap (Rio's own problem is worse and geography-based, not aircraft-based),
but worth knowing before over-attributing every SA number to Rio alone.

### Why S and A are out of scope here (unlike AustralAsia)

- **S (starting-cash floor)** — SouthAmerica's `RegionalEconomics.Factor`
  (1.081) already sits above the global average; Rio's airlines never go
  bankrupt. Cash is not the constraint. Not proposed.
- **A (starting-aircraft selection bar)** — measured net-negative for
  AustralAsia (bigger aircraft's higher operating cost broke previously-safe
  cities) and Rio's problem isn't its Q1 aircraft pick anyway — its whole
  regional neighborhood is thin. Not proposed.

Both stay correctly unused; this doc does not re-litigate them without new
evidence specific to SouthAmerica.

---

## Factors

Two interventions, following the exact discipline M65 established: each a
scoped, off-by-default knob so a run is a config change, not a code edit.

### D — SouthAmerica intra-region demand multiplier

- **Mechanism:** `GravityDemandModel`'s existing `intraRegionMultipliers`
  constructor param (already generic, keyed by `Region` — M65 shipped the
  AustralAsia entry; this is a second, independent entry in the same map,
  no Core change needed at all).
- **Knob:** re-add a `SouthAmericaIntraRegionDemandMultiplier` field to
  `ExperimentTuning`, exactly mirroring the removed AustralAsia-D field's
  shape. `FullGameRunner`'s two real-per-quarter construction sites merge it
  with the shipped `GravityDemandModel.AustralAsiaTunedIntraRegionMultipliers`
  map (both regions can carry a multiplier at once; AustralAsia's stays real
  and unconditional regardless of what this knob does).
- **Levels:** off (1.0) / 1.25 / 1.5 / 2.0 — the same grid M65 used, so the
  dose-response shape is directly comparable.
- **Expected effect, and the honest uncertainty:** boosts Rio's already-weak
  pairs (and everyone else's) proportionally — it doesn't close the *relative*
  gap between Rio and the eze/scl or bog/lim clusters (a flat multiplier
  scales all of them together), so this may raise the whole region's ceiling
  without fixing Rio's specific competitive disadvantage. Test it anyway:
  M65 showed D's biggest win was survival-driven cascades unlocking dominance
  runs, not pair-by-pair parity — the mechanism might still help here even
  if the intuition about "relative gap" suggests otherwise. If OFAT shows
  no real movement in Rio's own win rate specifically (not just the region
  aggregate), that confirms the intuition and points harder at C.
- **Pillar check (same stop condition as M65):** for every extra SouthAmerica
  win, is `WinnerRegionsDominated >= 3` (real dominance) or a weaker
  end-of-campaign-ranking win? Baseline is already only 38% dominance —
  watch for D making that fraction *worse* (more wins via ranking luck
  rather than real 3-region campaigns), the same regression M65 saw at D=2.0
  for AustralAsia.

### C — a real neighbor for Rio

- **Mechanism:** merge a supplementary cities file before building seats —
  no Core change, same as the AustralAsia C knob (never committed; caller-side).
- **Candidate, deliberately minimal (learn from AustralAsia's C failure —
  one well-chosen city, not seven invented ones):**
  - **Belo Horizonte (BH)** — 330 km from Rio, a real, major, well-known
    Brazilian city with a genuine air-travel history predating 1955.
    Proposed weight ~35–40 (modest — comparable to AustralAsia's Auckland/
    Brisbane at 40 in the existing dataset, not inflated to force a result).
  - **Held in reserve, only if BH alone under-corrects:** Curitiba (~660 km
    from Rio) — a second, smaller real city, tested in a later round rather
    than bundled in from the start, so any regression is attributable to one
    change at a time.
  - **Explicitly rejected as a candidate:** Brasília — founded 1960, not a
    real city in a 1955-start game. Historical-authenticity check any future
    South America city candidate against this before adding it.
- **Confounds (same three as AustralAsia's C, still apply):**
  1. Adding an eligible SouthAmerica city changes `RegionalEconomics.Factor`
     for the region → changes both SA starting cash and the AI filler-region
     ranking in `GameSetup`. Neutralize by pinning SA cash to the pre-C
     factor (1.081) whenever C is on and the (out-of-scope) S knob is off —
     same technique M65 used.
  2. A new non-gateway city becomes AI-filler-eligible; BH's weight must not
     accidentally make it the *new* highest-weight non-gateway SA city (Rio
     is 60; keep BH below that, e.g. ~35–40, so Rio stays the deterministic
     filler default and this experiment doesn't accidentally reroute the
     entire investigation's "67% of seats" city out from under itself).
  3. Run `StartingConditionsTests.Resolve_EveryEligibleStartingCity...`-style
     validation against the merged set before trusting any sweep — confirm
     BH itself doesn't land on a trap Q1 route.
- **Watch for:** BH sitting close to *both* Rio (330 km) and the gateway
  São Paulo (~490 km) could create an artificially convenient BH↔gru pair
  that mid-game AI strategies over-exploit the way the AustralAsia C cities
  did — check BH's own by-city bankruptcy/win numbers specifically, not just
  the region aggregate, exactly the diagnostic that caught AustralAsia's
  Nadi/Singapore regressions.
- **Expected effect:** the mechanism that actually matches this problem's
  shape — Rio's issue is specifically "too far from a good partner," and a
  real, close, moderate-weight neighbor is a direct structural fix, unlike D
  (which raises the ceiling everywhere without addressing Rio's relative
  isolation) or the rejected S/A (which don't touch geography at all).

---

## Metrics (response variables)

Same shape as M65's, with the by-city breakdown always keyed to Rio
specifically (this investigation's real signal), not just the region
aggregate:

**Primary**
- SouthAmerica bankruptcy rate (sanity check — must **stay** near 1.3%; a
  rise here would be a genuinely new problem, not a fix)
- SouthAmerica win share, and **Rio's own win rate specifically** (the
  aggregate can move without Rio moving, if e.g. bog/lim/eze/scl absorb all
  the gain — track both)
- Dominance-win fraction for SouthAmerica's wins (pillar check)
- Total bankruptcy rate; full per-region table (collateral check)

**Secondary**
- Avg game length
- By-starting-city breakdown, split filler-AI vs random-human pick (rules
  out the field-strength confound, as it did in the initial diagnosis)
- For factor C: BH's own bankruptcy/win numbers directly, not inferred from
  the aggregate

**Stop conditions** (identical in spirit to M65's):
- Total bankruptcy rises vs baseline.
- Any other region's bankruptcy rises meaningfully (> ~2 pts).
- SouthAmerica's own bankruptcy rises (this region doesn't have room to
  trade survival for competitiveness the way AustralAsia's D implicitly did
  — SA is already solvent, so any bankruptcy increase is a pure regression).
- SouthAmerica's dominance-win fraction drops (winning more but "less for
  real" is not a win, per M65's D=2.0 lesson).
- Factor C: any previously-safe SouthAmerica city's bankruptcy rate rises
  (the specific AustralAsia-C failure mode — Nadi/Singapore breaking when
  cities were added elsewhere in the region).

---

## Fixed parameters

- 300 games, `configSeed = 1..300`, `campaignSeed = 500_001..`, 244 quarters,
  same seed convention as every prior sweep in this investigation.
- `DistressStrategy` active; M65's AustralAsia demand tuning stays shipped
  and unconditional throughout (it is not part of what's under test here).
- Confirm the baseline (both knobs off) reproduces this doc's own table
  byte-identical before trusting any delta.

---

## Phases

### Phase 0 — Instrumentation

1. Re-add `SouthAmericaIntraRegionDemandMultiplier` to `ExperimentTuning`
   (factor D), merged with the shipped AustralAsia map at both real
   `GravityDemandModel` construction sites in `FullGameRunner.cs`. Off by
   default; "knob off = byte-identical" test, same discipline as M65.
2. Author the Belo Horizonte city entry (factor C) with real lat/lon and a
   defensible weight; validate against
   `StartingConditionsTests.Resolve_EveryEligibleStartingCity...`-style
   checks before it's used in any sweep.
3. Extend the scratchpad batch runner (already generalized to take a focus
   region — see the tool used for this doc's own diagnosis) with the two
   new combos.
4. Confirm the baseline row reproduces this doc's own numbers exactly.

### Phase 1 — OFAT

- D alone at 1.25 / 1.5 / 2.0 — report SouthAmerica aggregate *and* Rio's
  own win rate specifically, plus the dominance-fraction pillar check.
- C alone (BH added) — report the by-city table (does BH itself do well?
  does Rio's own rate improve now that it has a real, closer neighbor to
  route through/to?), plus the collateral checks.
- **Decision gate:** carry into Phase 2 only the factor(s) that move Rio's
  *own* win rate, not just the region aggregate — the aggregate can be
  moved by other cities without fixing the actual diagnosed problem.

### Phase 2 — Combination (if both factors survive Phase 1)

`2×2`: D off/best-level × C off/on. Read for interaction — does a real
nearby neighbor (C) let a modest demand boost (D) compound into something
neither achieves alone, or does fixing Rio's isolation directly (C) make D
redundant the way fixing AustralAsia's underlying problem made S redundant?

### Phase 3 — Confirmation

Re-run the winning combination at 900 games (3× replication), full
collateral table, re-confirm the dominance-win fraction didn't degrade. If
a combination wins: land it as real, unconditional behavior (mirroring how
M65's D graduated out of `ExperimentTuning` into
`GravityDemandModel.AustralAsiaTunedIntraRegionMultipliers`), write it up in
this doc's own "Outcome" section and `docs/HUB_DEMAND_ROADMAP.md` (if D) or
as a real `data/cities.json` addition with its own doc trail (if C), and
re-run the canonical 300-game sweep as the new baseline.

---

## Open question

Is there a third, more targeted mechanism worth screening if D and C both
under-deliver — e.g., a small, bounded "isolated-home-city" bonus in
`GravityDemandModel` (conceptually close to M63's `HubQuality` additive
term, but keyed to a city's *distance from its nearest same-region
neighbor* rather than an authored field)? Speculative, not designed here —
only worth pursuing if Phase 1/2 leaves Rio's specific gap materially
unclosed after both D and a real C candidate are tried.
