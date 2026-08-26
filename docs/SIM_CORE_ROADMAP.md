# Sim Core Roadmap

Scope: **AeroNext.Core and AeroNext.Sim only.** No Godot, no client, no
networking. Godot client and online play are the eventual product — this
roadmap deliberately doesn't plan them — but every milestone below is written
so it doesn't foreclose them (see "Kept in mind" at the bottom).

Each milestone should land as its own small commit or short commit series,
with tests alongside the logic it adds (per CLAUDE.md working preferences).
A milestone is done when `dotnet test` covers it, a coverage check
(`dotnet test --collect:"XPlat Code Coverage"`) has been read and every gap
it surfaces run to ground (fixed, or confirmed dead/unreachable), and
`AeroNext.Sim` can demonstrate it from the command line.

---

## M0 — Scaffold ✅

Solution structure, `Money` (integer cents), `SimRng` (seeded, explicit).
Done.

---

## M1 — World data: cities ✅

Static city dataset: id, name, coordinates, region, population/GDP proxy,
gateway flag, gate capacity. Loaded from JSON in the repo (source: OurAirports
or OpenFlights), not baked into code.

Start with a hand-picked ~30-city set that includes known chokepoints (Wake,
Honolulu, Anchorage, Gander, Azores, Dakar) and at least one gateway per
region, since M4 needs these to mean something.

**Done when:** `AeroNext.Sim` loads the file and prints a city table.
Tests: schema validation, no duplicate ids, deterministic load order.

Done. 30 cities across the 5 regions (4 home + Africa as a bridge region),
including all 6 named chokepoints, in `data/cities.json`, loaded and
validated by `CityCatalog`.

---

## M2 — Gravity demand model ✅

Pairwise base demand between cities from population/wealth and distance
(standard gravity formula). No routing or airlines yet — just "how many
people want to go from A to B this quarter."

**Done when:** demand for a sample of pairs is printed and sanity-checks
against real-world intuition (e.g. NYC–London ≫ Gander–Dakar).
Tests: symmetry/asymmetry as designed, monotonic decrease with distance,
stable under reordering city list (determinism rule #3).

**Open question carried to M4:** inverse-square distance decay makes the
*single biggest* markets short-haul regional pairs (e.g. CDG–LHR, GIG–GRU
outrank JFK–LHR outright), which cuts against "the largest demand pools are
intercontinental" in the map design pillars. Decision: don't fake this with
a region-crossing bonus now — the pillar is being read as a claim about
*aggregate, hub-routed* demand, not any single O-D pair. M4 must check that
an intercontinental hub's total captured connecting traffic across many
spokes exceeds what a best-effort regional-only network can reach. If that
check fails, revisit this model's tuning then, with evidence instead of a
guess now.

Done. `GreatCircle` + `GravityDemandModel` in `AeroNext.Core.World`, backed
by 9 tests. The open question above is carried forward, not resolved here.
(Scale constant was later revised in M3 for an unrelated reason — see M3 —
the relative short-haul-dominance question this section describes is
unaffected.)

---

## M3 — Single airline, nonstop routes ✅

One airline, a fleet, and a set of nonstop routes with frequency. Assign
nonstop demand to capacity, compute load factor.

**Done when:** a scripted single-airline run over a few quarters prints
load factors per route.

Done, with a scope trim: this milestone prints one quarter's load factors,
not several. Nothing in the model varies quarter-to-quarter yet — that
starts at M5 (turn loop) and M12 (historical events) — so a repeating loop
right now would just print identical numbers N times. Revisit "over a few
quarters" once there's something to actually change between them.

**Also required a mid-milestone fix to M2:** the gravity model's original
scale constant (5.0e8) made demand numbers uniformly dwarf any realistic
seats-per-quarter, so every route was capacity-constrained at 100% load
factor regardless of frequency — the demand-vs-capacity assignment logic
this milestone exists to demonstrate was never actually exercised. Rescaled
to 7.5e6, which is a pure units/order-of-magnitude fix, not a change to the
relative-ordering question already deferred to M4.

---

## M4 — Connecting itineraries (the key question) ✅ (closed by M6)

Passengers may route through a player's hub across two legs when no nonstop
exists or the connection is competitive. This is the milestone that answers
the question in CLAUDE.md's "Current phase": **does hub-and-spoke strategy
emerge from the demand model, or does it have to be faked?**

**Done when:** a hub-shaped network (spokes into one city, no direct
long-haul) captures long-haul demand nonstop networks can't, without any
hub-specific bonus logic — i.e. it emerges from itinerary search over the
gravity demand, not from a hardcoded hub multiplier.

**This is a go/no-go gate.** If hub-and-spoke doesn't emerge here, the
demand/itinerary model needs rework before any milestone below is worth
building on top of it.

**Also settles the M2 open question:** confirm an intercontinental hub's
total captured connecting traffic across its spokes exceeds a best-effort
regional-only network's demand. If it doesn't, the gravity model's distance
decay needs retuning (see M2) before this gate can be called passed.

**Result: mechanism passed cleanly; the economics needed an explicit fix.**

`NetworkPlanner` in `AeroNext.Core.Airlines` builds nonstop itineraries where
a direct route exists and one-stop itineraries through the shortest-detour
hub otherwise, with nonstop demand claiming its own leg's capacity before
one-stop connections get whatever's left. Tested with a same-size (5-route,
same-aircraft) budget spent two ways: a JFK hub reaching all four other
regions, vs. the strongest same-size network obtainable within North America
alone (JFK-ORD-LAX-MIA-YYZ-SFO — the densest triangle in the dataset,
deliberately the hardest opponent, not an average one).

- **Mechanism check passed as specified:** one-stop search captured 7,866
  pax/qtr the hub's nonstop-only reading structurally couldn't reach
  (35,427 → 43,293), entirely from itinerary search over existing demand —
  no hub-specific bonus.
- **Economics check initially failed:** at every distance-decay exponent
  tested (1.0-2.0, the physically sensible range), the regional network's
  raw addressable demand beat the hub's — 7.2x at exponent 2.0, still 2.1x
  at exponent 1.0 (an unrealistically flat decay). Exponent alone cannot
  make this pillar emerge from pure population/distance gravity for this
  city dataset; going flatter to force the result would be reverse-
  engineering the curve, i.e. exactly the "faking it" this gate exists to
  catch.
- **Fix (user-directed):** added an explicit, authored cross-region demand
  multiplier to `GravityDemandModel` (default 6.0) — applied uniformly to
  any pair crossing a region boundary, whether or not a hub serves it, so
  it's a general economic rule, not a hidden hub bonus. Represents that
  intercontinental travel has no substitute (can't drive or rail across an
  ocean) while short/medium-haul competes with those alternatives.
- **Result with the multiplier:** hub reaches 91% of the strongest regional
  network's captured demand (up from ~46% unmultiplied) in single-airline
  isolation. Pushing the multiplier further to force full dominance in
  isolation would need ~35-40x, which stops being a defensible economic
  constant. **Full closure of the gap is expected from M6** (multiple
  airlines competing for the same regional pool dilutes any one airline's
  share of it — no single carrier monopolizes the NYC-LA-Chicago triangle
  the way this isolated single-airline test necessarily does). M6 should
  re-run this same hub-vs-regional comparison under competition and confirm
  the gap fully closes before this gate is called unconditionally passed.

---

## M5 — Turn loop and simultaneous resolution skeleton ✅

Quarter loop: Briefing → Decisions → Resolution, per CLAUDE.md. Even with one
airline, structure resolution as "collect all orders, then resolve" —
architectural from day one, not retrofitted later.

**Done when:** `AeroNext.Sim` runs N quarters against a scripted order list
and prints a briefing-style exception summary each quarter.

Done. `Order`, `QuarterEngine`, and `BriefingGenerator` in
`AeroNext.Core.Turns`. `QuarterEngine.RunQuarter` collects every order for
the quarter into a dictionary keyed by route pair before building the
network once — no order is applied against a partially-updated state, which
is the property M6+ needs when multiple airlines' orders can conflict.
`BriefingGenerator` reports new routes and load-factor swings ≥15 points
between quarters, capped at 5 items, sorted deterministically by route id
regardless of order-list or dictionary iteration order.

**Scope note:** the exception taxonomy is intentionally narrow — only
"route entered" and "load factor moved" exist, because those are the only
kinds of change a single, uncontested airline can produce yet. CLAUDE.md's
other examples (competitor entered a market, aircraft nearing retirement,
negotiation resolved) need M6, M9/M10, and M8 respectively before they're
meaningful; extending the taxonomy now would be inventing exceptions with
nothing behind them.

---

## M6 — Multiple airlines, shared demand pool ✅

2–4 airlines competing for the same city-pair demand. Market share split
by fare stance / service level / frequency (posture-based, not raw numbers
— see CLAUDE.md "Market" actions).

**Done when:** a scripted 2-airline scenario shows demand splitting
plausibly and one airline's frequency increase visibly eroding the other's
load factor.

**Also finishes the M4 conditional pass:** re-run M4's hub-vs-regional
comparison with 2-4 airlines competing for the NYC-LA-Chicago triangle.
Confirm competition dilutes any single regional-only airline's captured
share enough that the hub network (already at 91% in isolation) comes out
ahead once demand is actually split. If it doesn't, M4's cross-region
multiplier needs raising, with this result as the evidence.

**Done.** `CompetitiveMarket` in `AeroNext.Core.Competition` splits a pair's
demand across every airline that reaches it, weighted by
(bottleneck-leg capacity × `Airline.Quality`) and capped at each airline's
own monopoly ceiling (what `NetworkPlanner` says it could carry alone).
`Airline.Quality` is a single-knob placeholder standing in for CLAUDE.md's
fare-stance/service-level/marketing postures — a real three-axis system
belongs with M7, once players have a way to actually set those postures.

An early implementation bug is worth recording: the first version weighted
competitors by their monopoly *ceiling* (capacity capped at demand) instead
of raw capacity. That silently breaks frequency competition the moment one
airline's capacity alone already exceeds demand — the ceiling saturates and
stops growing even as real capacity keeps growing, so boosting frequency
stopped affecting anyone's share. A test written against the milestone's
own "Done when" wording (frequency increase must erode a competitor) caught
it immediately. Fixed to weight by raw bottleneck-leg capacity, ceiling
used only as the cap.

**Core mechanic confirmed:** on JFK-YQX (thin enough that raw capacity
exceeds demand for a single operator), two evenly-matched airlines split
480 pax/qtr demand ~50/50 (240/240). Boosting one airline from 1 to 3
round trips/week — with the rival unchanged — shifted the split to 360/120:
a competitor's load factor eroded by a rival's frequency decision alone.
On a capacity-constrained route (any realistic fleet on JFK-ORD-sized
demand), competition has no visible effect — both operators simply fill
their own capacity regardless of rivals. That's not a gap; it's the same
capacity- vs demand-constrained distinction M3 established, now confirmed
to hold across competitors too.

**M4's conditional pass, resolved with real numbers, not further tuning:**
re-ran the same 5-route hub vs. regional-triangle networks from M4, now
contested.
- **2 regional competitors** (both flying the identical JFK-ORD/LAX/MIA +
  ORD-YYZ + LAX-SFO network): hub carries 40,602 vs 40,787 each — a
  near-miss, 99.5% of a regional competitor's total. JFK-ORD and JFK-LAX
  demand so vastly exceeds even *two* competitors' combined capacity that
  those two legs stay capacity-pinned regardless of rivalry; only the
  triangle's thinner legs see real dilution.
- **3 regional competitors:** hub carries 38,645 vs 36,603 each — a clear,
  genuine win, entirely from competitive dilution once a third airline also
  piles into the same obviously-best regional strategy. No multiplier
  change needed.

**Conclusion:** "a home region cannot fund a win" holds once enough rivals
contest the same regional prize. This is no longer a hypothetical: CLAUDE.md
now specifies the game is *always* exactly 4 airlines (AI fills any seat a
human doesn't), so a game where 3 of the 4 airlines independently reach for
the same best-looking regional triangle isn't a corner case to hope for —
it's a normal, expected outcome the sim will actually encounter. It does
*not* hold against a single regional-only opponent in isolation (M4's
finding stands on its own, and matters for a 2-airline early game or a
region with only one serious contestant). M4's go/no-go gate is now
unconditionally closed.

---

## M7 — Action economy ✅

3-action-per-quarter budget. Classify actions as contested / market /
private per CLAUDE.md. Enforce the budget; private actions (refinancing,
retiring aircraft, hiring executives) cost nothing.

**Done when:** an over-budget scripted order set is rejected/truncated
deterministically, and executive hires raise the cap for future quarters.

Done. `QuarterOrder` is now the base for two order types: `RouteOrder`
(market action — 1 action for a pair not already served, 0 for adding
frequency to one that is, per CLAUDE.md) and `HireExecutiveOrder` (private
action, always free, raises `QuarterResult.ActionCap` starting *next*
quarter only — hiring never rescues the same quarter's own budget).
`QuarterEngine.RunQuarter` now takes the incoming action cap and scans
orders in submission order, tracking cumulative spend.

**Rejection rule, chosen deliberately over hard truncation:** an order that
would exceed the remaining cap is skipped, but scanning continues — a free
order later in the list (frequency change on an already-served pair, or any
executive hire) still applies even after an earlier, costlier order was
rejected. A strict "reject everything after the first failure" reading of
"truncated" would block actions that cost nothing for no reason; this is
covered directly by
`QuarterEngineTests.RunQuarter_FreeOrderAfterRejection_StillApplies`.

The scripted quarter loop in `AeroNext.Sim` demonstrates the full arc: 2 of
3 actions spent, then a route entry plus a free executive hire, a quiet
quarter showing the raised cap (4), a quarter where the raised cap lets 4
new routes in but rejects a 5th, and a free frequency bump.

Scope left for later, deliberately not built here: the three-axis fare
stance / service level / marketing posture system (`Airline.Quality` in
M6 remains its placeholder) and the contested action types (M9 landing
rights, M10 aircraft orders) — those cost actions too, but the mechanics
for *how* they resolve don't exist yet.

---

## M8 — Baseline AI opponent ✅

**New requirement, not in the original plan:** CLAUDE.md now states the game
is always exactly 4 airlines — any seat a human doesn't fill is played by a
computer opponent, never fewer than 4 competing networks. That means AI
isn't a later-polish feature; the sim can't run a representative game
without it, and M12's 240-quarter campaign specifically can't be hand-
scripted for 4 airlines across 240 quarters. This has to land before M12,
and makes most sense right after M7, once there's an action budget for AI
decisions to actually respect.

Scope for a *baseline* AI: a simple heuristic that picks legal, budget-
respecting orders each quarter — e.g. greedily enter the highest-demand
city-pair it can afford, add frequency to its best-performing route
otherwise. Not trying to be a good player yet, just a legal, deterministic
one (same seed ⇒ same AI decisions, per the determinism rules).

**Expect light follow-up work at M9 and M10:** each adds a new action type
(landing-rights bids, aircraft orders) the baseline AI doesn't know about
yet. Extending it to use a new action type belongs with the milestone that
adds that type, not bundled here speculatively.

**Done when:** a 4-airline scripted quarter (1 human-scripted + 3 AI) runs
through `QuarterEngine` and `CompetitiveMarket` without any hand-authored
orders for the AI airlines, and produces a legal, budget-respecting result.

Done. `BaselineAi.DecideOrders` in `AeroNext.Core.Ai`: if it can afford a
market action, enters the single highest-demand city pair it doesn't
already serve anywhere in the world (no hub-awareness — it doesn't
prioritize pairs touching its existing network); always bumps frequency on
its own most capacity-constrained existing route, since that's free. No
executive hiring yet, no contested actions (M9/M10 don't exist yet). Pure
function of current state, no RNG — deterministic by construction, not by
seeding, since a plain greedy heuristic has nothing to randomize.

`AeroNext.Sim` runs the full always-4-airlines shape: 1 human-scripted seat
and 3 identical AI seats, each resolved independently through
`QuarterEngine` (own routes, own action cap, own orders) and combined once
per quarter through `CompetitiveMarket`. Confirmed two things worth noting:
the 3 AI airlines make byte-identical decisions every quarter (expected —
same deterministic heuristic, same world, no differentiation between them
yet, noted as a known simplification rather than a bug) and, over 3
quarters, the AI trio piling into CDG-LHR (the world's densest short-haul
market, per M2/M6) out-carries the human's deliberate long-haul hub
strategy — an unplanned but fitting callback to what earlier milestones
already established about short-haul market density.

**Follow-up confirmed still needed at M9/M10**, as flagged above: extending
`BaselineAi` to bid on landing rights and order aircraft belongs with the
milestones that add those mechanics.

---

## M9 — Contested resolution: landing rights ✅

Silent-commitment bidding at gateway cities. Resolve on
money × relations × reputation × political alignment. Multiple suitors raise
price for everyone. **Losers get partial payoff** (standing improves, next
attempt cheaper) — without this the mechanism dies per CLAUDE.md.

**Done when:** a 3-suitor scripted negotiation at one gate produces one
winner and two improved-but-failed bids, reproducibly under a fixed seed.

Done. `NegotiationOrder` (a `QuarterOrder` in `AeroNext.Core.Turns`, always
1 action — contested actions have no free variant) plus
`LandingRightsBoard`/`LandingRightsResolver` in a new
`AeroNext.Core.Negotiations` namespace. Two previously-unused foundations
finally earned their keep: `Money` (built in M0) is the bid currency, and
`City.GateCapacity` (built in M1) caps how many slots a city can ever grant.
`SimRng` (also M0) got its first real production use, picking each
negotiation's 2-4 quarter resolution delay — deterministic per seed, exactly
as the determinism rules require.

**Simplification, matching the M6 `Quality` precedent:** CLAUDE.md's
"money × relations × reputation × political alignment" collapses to
`Bid.Cents × Standing`, one number per (airline, city) that rises 0.2 per
loss there. Modeling relations/reputation/political-alignment as three
independently-tuned systems isn't something the design doc specifies enough
to justify — it would be invented complexity, not a real requirement.

**Resolution logic:** negotiations due the same quarter are grouped by
target city; within a group, the top N by Score win the city's N remaining
gate slots (N = `GateCapacity` minus slots already granted, so scarcity
persists correctly across separate resolution rounds); everyone else's
Standing rises. Bids submitted in different quarters aren't forced to
coincide — if only one suitor's bid is due at a city that quarter, it wins
by default (or auto-loses if the gates are already full), which is a
faithful reading of "commit blind," not a gap: you don't know whether a
rival's differently-timed bid will even be in the room when yours resolves.
The `AeroNext.Sim` demo (real data: Gander, capacity 2) demonstrates this
directly — the smallest of four bids won outright because it resolved alone
in an earlier quarter, while a bigger, later bid auto-lost because both
slots were already gone.

**BaselineAi extended**, closing the M8 follow-up: it now also bids
(a fixed placeholder amount) on the single highest-demand gateway it
doesn't yet touch, budget-permitting, ranked behind route entry when
budget is tight. It doesn't check remaining gate capacity before bidding —
deliberately: per CLAUDE.md, negotiations are blind even for a skilled
human, so that's the mechanic working as intended, not a missing smarts.

---

## M10 — Contested resolution: aircraft orders and alliances

Aircraft orders are public and contest delivery-line position, not the
airframe; leasing is the expensive instant-metal path. Alliance courting:
a regional carrier joins exactly one alliance.

**Done when:** two airlines ordering the same aircraft type in the same
quarter produce different delivery quarters, deterministically.

---

## M11 — Fleet and range economics

Aircraft specs (range, capacity, cost, era availability). Range determines
which routes/chokepoints are viable. This is what makes chokepoints
*obsolete* as range improves across the campaign (per CLAUDE.md map design).

**Done when:** a chokepoint route that was mandatory in an early-era
scripted run becomes bypassable once a longer-range aircraft is available.

---

## M12 — Financial model and scoring

Full quarterly P&L: revenue from itineraries, operating cost, debt service.
Refinancing as a private action. This is explicitly "the scoring function,
not the point" — keep it subordinate to the map/area-control layer, not the
center of the design.

**Done when:** a scripted campaign produces a plausible cash trajectory and
a bankruptcy/failure state triggers correctly.

---

## M13 — Historical arc and timeline events

1955–2015 event data (new aircraft types entering service, historical demand
shifts) driving M11's range unlocks and M2's demand changes over time.
Data-driven (JSON), not hardcoded per-quarter branches.

**Done when:** a full ~240-quarter campaign — always 4 airlines, at most 1
human-scripted and the rest played by M8's baseline AI — runs end-to-end
and prints a summary; chokepoint relevance visibly shifts across the run.

---

## M14 — Determinism regression harness

Formalize what M0–M13 assumed informally: same seed + same orders ⇒ byte-
identical resolution output, across repeated runs — including AI decisions,
which is why M8 required AI to be deterministic from the start rather than
retrofitted later. This is the safety net that makes online play and
simultaneous multiplayer possible.

**Done when:** a test runs the same scripted campaign twice and diffs the
full output; any unordered-collection or ambient-state regression fails CI.

---

## Kept in mind, not scheduled

Not part of this roadmap, but constraints below already shape M0–M14 so
these aren't blocked later:

- **Godot client** — Core has zero Godot references (enforced since M0).
  State needed for rendering (exception queue, world map state) should stay
  representable as plain data the client can pull, not something only the
  console printer knows how to produce.
- **Online play / game server** — the M5 simultaneous-resolution skeleton
  and M14 determinism harness exist specifically so a server can replay or
  validate client-submitted orders later without redesigning resolution.
- **Split-screen local multiplayer** — M5/M6's per-airline order collection
  should stay orthogonal to *how many local players* submit orders (the
  rest are AI, per CLAUDE.md's always-4-airlines rule); the sim core
  shouldn't need to know or care how many of the 4 seats are human.
- **Turn timer** — a client/UI concern layered on top of the M5 turn loop,
  not a sim-core concept.
