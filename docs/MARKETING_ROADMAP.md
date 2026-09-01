# Marketing Roadmap

Continues the shared milestone numbering from `ROUTE_CAPACITY_ROADMAP.md`
(M32–M33/M45–M46 issued at the time). Picks up at M47.

**Origin:** `docs/FINANCIAL_MODEL_ROADMAP.md`'s own "Kept in mind" section
named this gap directly and marked it explicitly out of that roadmap's
scope: "`Airline.Quality` remains a single placeholder knob standing in
for service level and marketing, and fare still doesn't affect demand
size or competitive share... both real, separate gaps, adjacent to
'financial model' but not this roadmap's own scope." `Airline.cs`'s own
doc comment said the same thing from the other side: "until [players
have] a way to set those postures, this is the whole model." The user
then asked directly for exactly that way: a per-route ad campaign (a
"good effect" on one route) or a network-wide one (a smaller effect, but
on every route) — mutually exclusive, up to 3 route campaigns at once,
each lasting exactly the quarter it's submitted, cost scaled by network
size or by that route's own current demand.

---

## M47 — Marketing campaigns (route-targeted and network-wide) ✅

**No new persistent state — a first for this whole recent
capacity/AI-mechanics run.** A campaign's effect lasts exactly the
quarter it's submitted in, so unlike M45/M46's own new boards, this
needed none: `QuarterEngine.RunQuarter` gates and accepts the order,
`FullGameRunner.cs` reads that same quarter's own `AppliedOrders`
straight into `CompetitiveMarket.Resolve`, done.

**Two new order types** (`NetworkMarketingOrder`, no fields;
`RouteMarketingOrder(City Origin, City Destination)`), each 1 action —
a Market action per CLAUDE.md's own taxonomy, no shared contested
object, but it competes for the same 3-action budget as everything
else. `RouteMarketingOrder` is rejected against an unserved pair using
the exact same check `SetFareOrder` already established (exact-orientation
match; a dedicated "shares only the origin" test and a "shares neither"
short-circuit test both mirror `SetFareOrder`'s own existing coverage of
that `&&`, needed to actually reach 100% branch coverage on the new
code, not just the happy path).

**Cost is a deterministic formula, never player-chosen** (unlike
`NegotiationOrder.Bid`) — new `MarketingCostCalculator`
(`src/AeroNext.Core/Finance/`, same shape as `FareCalculator`):
`NetworkCampaignCost(routeCount)` = $100,000 `(Proposed, tunable)` per
route the airline currently operates; `RouteCampaignCost(demandPassengers)`
= $50 `(Proposed, tunable)` per passenger of that pair's own current
quarterly demand. Never stored on the order — `QuarterEngine`'s own
affordability gate and `FinancialResolver`'s actual deduction each call
the same static formula independently, from state each already has
(`QuarterEngine` already receives `demandModel`; `FinancialResolver`
already receives `airline.Routes` and `CompetitiveMarketResult.Pairs`,
which already carries each pair's own `DemandPassengers`) — the two can
never drift apart since neither reads a value the other computed.

**The contested-pair effect plugs directly into the existing
competitive-share mechanism**, no new demand-splitting logic:
`CompetitiveMarket.Resolve`'s weight formula (`bottleneck-leg seats x
Quality`, used only when a pair has more than one real competitor)
becomes `bottleneck-leg seats x Quality x campaign multiplier` — `1 +
RouteCampaignBoost` (0.5, `(Proposed, tunable)` — the "good effect")
when this specific pair is in the airline's own active route campaign,
`1 + NetworkCampaignBoost` (0.15, `(Proposed, tunable)` — the smaller,
everywhere effect) if it has an active network-wide one instead, else
unboosted. (Round 2 below extends this to uncontested pairs too — see
that section for the original round 1 gap this closed.)

**QuarterEngine's own rejection rules**, all in the existing sequential
per-order loop with new local per-quarter tracking: mutual exclusivity
(a `NetworkMarketingOrder` is rejected if any `RouteMarketingOrder` was
already accepted the same batch, and vice versa — tested both orderings,
since QuarterEngine's own "first-in-batch wins" scanning means the
result depends on which order the player listed first); a new
`MaxConcurrentRouteMarketingCampaigns = 3` constant, enforced independent
of `ActionCap` (which can otherwise grow past 3 via `HireExecutiveOrder`
— a dedicated test raises the cap to 10 and confirms the 4th route
campaign is still rejected); a duplicate `RouteMarketingOrder` targeting
a pair already accepted the same batch is rejected too. Pair matching
throughout uses a new `QuarterEngine.NormalizedPairKey` (public, not
private — both `CompetitiveMarket` and the orchestration layer need the
exact same normalization), since `NetworkPlanner`'s own itinerary
construction doesn't guarantee preserving a route's stored
Origin/Destination orientation, confirmed by reading `Itinerary`/`ItineraryLoad`
before assuming otherwise.

**`FinancialResolver`/`QuarterlyStatement`**: new `MarketingCost` line,
same "broken out, not silently folded into NetIncome" legibility
treatment every prior cost has gotten since M26, appended as a trailing
optional field (never inserted mid-list, so no existing
positional-constructor test breaks).

**Deliberately out of scope, flagged as a follow-on, not built:** no
`IAiStrategy` submits either order type — a genuinely new lever with no
existing precedent to extend (unlike M45's idle-contest fallback, which
was required just to make that mechanic *reachable* in AI-vs-AI play at
all); marketing being human-only for now doesn't break anything the way
an AI-invisible *gating* mechanic would have, confirmed directly: the
300-game QA sweep reproduces M46's own numbers byte for byte, exactly as
expected with no AI ever touching this lever. Also out of scope:
briefing/exception-queue surfacing (the campaign is the player's own
deliberate action this quarter, already visible via
`AppliedOrders`/`RejectedOrders`, not a "here's what changed" surprise
the way a rejected order or a rival's route entry is).

**Done when:** every pre-existing call site's default/null behavior is
unchanged (confirmed: the full pre-existing suite passed unmodified);
dedicated tests cover both cost formulas; every `QuarterEngine` rejection
rule (unserved pair, both origin-only-shared and shares-neither
short-circuit cases, mutual exclusivity both orderings, the 3-campaign
cap independent of a raised `ActionCap`, a same-pair duplicate,
affordability both accepted and rejected for each order type, and the
fully-permissive `availableCash: null` default); `CompetitiveMarket`
applying the route boost only to the targeted pair (and correctly *not*
boosting a different pair the same airline also has a real, non-matching
campaign for), the network boost to every pair, no boost on an
uncontested pair, no boost with a null `marketingByAirlineName` or an
airline absent from it; `FinancialResolver` deducting the right cost for
each order type (including the orientation-insensitive pair lookup, and
a pair genuinely absent from `CompetitiveMarketResult.Pairs` costing
zero rather than throwing).

Done (round 1). 713 tests total (33 new — 6 `MarketingCostCalculator`
(new file), 15 `QuarterEngine`, 7 `CompetitiveMarket`, 5 `FinancialResolver`).
100% line/branch coverage on every touched/new class except
`NetworkMarketingOrder`'s own 0%-line/100%-branch reading — confirmed to
be the exact same coverlet artifact `QuarterOrder`'s own abstract,
bodyless record declaration already carries (a zero-parameter positional
record has no body line coverlet can attribute a "hit" to, even though
it's genuinely constructed and asserted on in tests) — not a real gap,
not chased further, matching this project's own established precedent
for that exact shape. Overall project coverage 99.73%/99.47%. 300-game
QA harness sweep at 244 quarters: 300/300 clean, 0 invariant violations,
0 crashes, numbers byte-identical to M46's own (expected and confirmed,
per the out-of-scope note above — no AI strategy exercises this milestone
at all).

---

## M47 round 2 — marketing on uncontested routes too, bounded by spare capacity ✅

**Origin:** the user's own direct follow-up, immediately after round 1
shipped: marketing should also raise *general demand* on uncontested
routes, not just win competitive share on contested ones — "real
tangible effects would only be felt on routes that have lower load
factors. Routes at or very near capacity will see little gain." Round
1's own solo-carrier branch never read a campaign at all (mirroring how
`Quality` itself never applied there either) — this was the real,
correctly-scoped gap round 1 left open, not a bug.

**A genuinely new mechanism for the uncontested case, not a reuse of
the contested weight formula** — there's no rival share to win on a
solo pair, so `CompetitiveMarket.Resolve`'s solo branch instead adds
real carried passengers: `RouteCampaignBoost`/`NetworkCampaignBoost`
(the *same* two constants round 1 already defined, renamed from
`...QualityBoost` to `...Boost` since they now drive two different
mechanisms) applied as a genuine fraction of the pair's own
`DemandPassengers` — "how much *additional* demand this campaign
generates" — then capped by `Math.Min` against the itinerary's own
tightest leg's real remaining spare capacity. That single `Math.Min` is
the entire "little gain near capacity" mechanic: a route already near
its own seat ceiling has near-zero spare capacity regardless of how big
the campaign's own demand figure is, while a half-empty route realizes
close to the campaign's full stated effect — exactly the load-factor-gated
behavior requested, and directly verified
(`Resolve_MarketingCampaign_UncontestedPair_LowLoadFactor_GeneratesRealAddedDemand`,
`Resolve_MarketingCampaign_UncontestedPair_AtCapacity_NoGain`).

**A running, per-airline, per-leg spare-capacity ledger**, seeded once
from each airline's own already-finalized `NetworkPlan.RouteUsage`
(`SeatsPerQuarter - SeatsUsed`) and decremented as each of that
airline's own boosted pairs actually consumes it, in the same
deterministic pair order `Resolve` already iterates in — so two
one-stop itineraries sharing a hub leg (both covered by one network-wide
campaign, say) can never combine to claim more of that leg's spare
capacity than genuinely exists
(`Resolve_MarketingCampaign_UncontestedPair_SharedHubLeg_NeverDoubleClaimsSpareCapacity`
builds exactly this scenario and checks the combined claim against the
leg's own real spare capacity directly, not just that nothing crashes).

**A deliberate, explicitly-flagged simplification, not silently
absorbed:** this ledger does **not** interact with the contested branch
— a contested pair's own ceiling is still computed from the original,
unboosted monopoly plan, so a leg shared between an uncontested boosted
itinerary and a separately contested one isn't re-checked against the
contested side after the boost consumes some of that leg's capacity.
Fully reconciling the two would mean rebuilding `NetworkPlanner`'s own
allocation with marketing-aware demand baked into its per-leg sharing
logic — correct in principle, but a much larger change for what's meant
to be a soft, tunable revenue lever, not a hard scarcity mechanic on
par with `LandingRightsBoard`'s own slot accounting. Flagged here for
the same reason every other real limitation in this codebase is: so it
can be revisited deliberately if it ever turns out to matter in
practice, not discovered by surprise.

**No new order types, no new cost, no new `QuarterEngine`/`FinancialResolver`
changes** — this is a richer *effect* of the exact same already-paid-for
campaign a player already submitted in round 1, not a new action.

**Done when:** the full round-1 suite passes unmodified (confirmed) or
is updated in place where round 1's own test asserted the *old*,
now-superseded "no effect on uncontested pairs" behavior (one test
rewritten into two, reflecting the new load-factor-gated reality rather
than patched to keep passing); dedicated tests cover the low-load-factor
case, the at-capacity case, network-vs-route campaign magnitude on an
uncontested pair, and the shared-hub-leg non-double-claiming property.

Done. 716 tests total (3 net new over round 1 — `CompetitiveMarketTests`
gained 4 and lost the 1 now-superseded test). 100% line/branch coverage
on `CompetitiveMarket`, the only class this round touched. Overall
project coverage 99.73%/99.48%. 300-game QA harness sweep: 300/300
clean, numbers byte-identical to round 1's own (expected — still no AI
strategy exercises marketing at all, on either the contested or
uncontested path).

---

## M48 — Fare gets the same demand effect marketing just got ✅

**Origin:** the user's own direct follow-up, immediately after asking
"how does fare currently affect demand?" and learning the honest answer
(it doesn't — a pure revenue lever, deliberately scoped out at M34, the
one deliberate gap left once M47 built marketing's own sibling
mechanism). The ask: fare should move demand the same *kind* of way
marketing does, but bidirectionally — a discount raises it (like
marketing), a premium *lowers* it for that airline specifically
(marketing never needed a decrease case). Effective on both contested
and uncontested routes, same as marketing. Explicit, load-bearing
constraint: "do not force decreased passenger numbers on increase of
fares... the new lowered demand could still be greater than capacity."

**New `FareCalculator.DemandDeltaFraction(Route)`** — standard price
elasticity: `-FareDemandElasticity x (FarePerPassenger / SuggestedFarePerPassenger - 1)`,
`FareDemandElasticity = 1.5` `(Proposed, tunable)` (a 10% discount off
the suggested fare yields a 15% demand increase; a 10% premium yields a
15% decrease, symmetrically). Exactly zero whenever `FareOverride` is
null — `FarePerPassenger` then equals `SuggestedFarePerPassenger` by
construction, ratio exactly 1.0 — so every route nobody has ever touched
`SetFareOrder` on is completely unaffected, with no explicit
null-permissive parameter needed the way M45/M46/M47's own new
parameters each required: the "opt-in" is already implicit in whether a
fare override exists at all, the same mechanism that already gated
whether fare affected *revenue* since M34.

**Reuses M47 round 2's own machinery rather than inventing parallel
logic** — `CompetitiveMarket`'s contested branch gains one more
multiplicative weight factor, `EffectiveFareMultiplier` (floored at
zero so an extreme premium can't make a weight negative); the
uncontested branch's `ApplyMarketingDemandBoost` is generalized into
`ApplyDemandAdjustment`, combining marketing's own boost fraction
(always ≥ 0) with fare's signed fraction (`Legs.Min` across a one-stop
itinerary, mirroring the existing bottleneck-leg convention) into one
total delta, then branching on its sign:
- **Non-negative delta** (net discount): identical to round 2's own
  growth path — `Math.Min` against the running spare-capacity ledger.
- **Negative delta** (net premium): shrinks carried, but only past
  `unmetDemand` (`max(0, DemandPassengers - PassengersCarried)` — how
  much demand was already going uncarried before this adjustment, zero
  unless already oversold). The cut is absorbed by that cushion first;
  carried only actually drops once the cut exceeds it — the entire
  mechanism behind the user's own "never force a decrease while still
  above capacity" requirement, falling out of one `Math.Max(0, ...)`
  rather than a special case. Also clamped at `PassengersCarried` itself
  (a real bug caught by this round's own tests, not shipped: an extreme
  premium against a huge demand figure could otherwise drive the
  reduction — and therefore carried — negative before this floor was
  added). A genuine reduction correctly *frees* spare capacity on the
  ledger too, for a later pair sharing the same hub leg.

**Done when:** the two pre-existing `FinancialResolverTests` that
assumed fare had zero demand effect were rewritten to reflect the new
reality rather than patched to keep passing (one dropped its
now-invalidated `FareOverride`, since the test was actually about
SeatsUsed vs SeatsPerQuarter, not about overriding fare at all; the
other moved from a 2x premium — which, it turned out, was extreme enough
to fully sell out the route into the reduction cushion, breaking its own
"same carried count both sides" premise — to a moderate 20% one,
verified to still land in the always-oversold regime before trusting the
revenue assertion built on it); dedicated tests cover
`DemandDeltaFraction` itself (zero exactly with no override, sign and
elasticity-formula magnitude for a discount and a premium, symmetry, and
a degenerate zero-suggested-fare guard engineered via a same-city,
huge-seat route rather than skipped); `CompetitiveMarket` tests cover a
low-load-factor discount increasing carried, an at-capacity premium
never decreasing it, a not-at-capacity premium genuinely decreasing it,
an extreme premium never going negative, both contested-share directions,
and marketing/fare stacking together on one pair.

Done. 730 tests total (14 new — 6 `FareCalculator`, 8 `CompetitiveMarket`,
plus 2 `FinancialResolver` rewritten in place, not counted as new). 100%
line/branch coverage on `FareCalculator` and `CompetitiveMarket`, the
only classes this milestone touched. Overall project coverage
99.74%/99.48%. 300-game QA harness sweep at 244 quarters: 300/300 clean,
0 invariant violations, 0 crashes.

**Unlike marketing, this one genuinely moved the sweep — confirmed by
ablation, not assumed.** Unlike M47 (which no `IAiStrategy` ever
touches), `AiToolkit.ApplyManagementToolkit` has submitted `SetFareOrder`s
reactively since M37 (`RaisedFare` on a saturated route, `CutFare` on an
underperforming one) — both real strategies, both real loops, already
exercising this constantly. Those existing orders suddenly having real
demand consequences is a genuine behavior change, not a dormant lever.
Overall win rate **38.7% → 40.7%** (122/300, avg quarter 23.0 → **51.6**
— games run measurably longer before a winner emerges). Region-by-region
bankruptcy moved unevenly (EastAsia 60.7% → 74.5% the sharpest move,
SouthAmerica 92.0% → 80.6% the other direction); win share barely moved
(NorthAmerica 84.5% → 85.2%, SouthWestAsia 15.5% → 14.8%). Isolated by
ablation (`FareCalculator.DemandDeltaFraction` temporarily forced to
return 0.0, reverted before shipping, same instrument-then-revert
discipline as every prior milestone here): with the effect disabled, the
sweep reproduces the pre-M48 baseline exactly (116/300, avg 23.0,
identical region table) — confirms the move is real, not sampling noise.
Not chased further here (no fix attempted, no root-cause trace beyond
identifying the mechanism) — flagged plainly, matching this project's
own established discipline, as a real interaction for whoever next works
on `BaselineAi`/successor strategies to be aware of: fare reactivity and
demand elasticity now feed back into each other in a loop neither
`RaisedFare`/`CutFare` nor this milestone was designed with the other in
mind.
