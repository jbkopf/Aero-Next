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

**The actual effect plugs directly into the existing competitive-share
mechanism**, no new demand-splitting logic: `CompetitiveMarket.Resolve`'s
weight formula (`bottleneck-leg seats x Quality`, used only when a pair
has more than one real competitor) becomes `bottleneck-leg seats x
Quality x campaign multiplier` — `1 + RouteCampaignQualityBoost` (0.5,
`(Proposed, tunable)` — the "good effect") when this specific pair is in
the airline's own active route campaign, `1 + NetworkCampaignQualityBoost`
(0.15, `(Proposed, tunable)` — the smaller, everywhere effect) if it has
an active network-wide one instead, else unboosted. Deliberately
consistent with how `Quality` itself already behaves: the solo
(uncontested) branch never reads `Quality` at all today, so a campaign
only visibly matters on a genuinely contested route — not a new gap
this introduces, the same shape the mechanism it plugs into already
has, confirmed by a dedicated test (`Resolve_MarketingCampaign_UncontestedPair_HasNoEffect`).

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

Done. 713 tests total (33 new — 6 `MarketingCostCalculator` (new file),
15 `QuarterEngine`, 7 `CompetitiveMarket`, 5 `FinancialResolver`). 100%
line/branch coverage on every touched/new class except
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
