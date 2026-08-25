# Sim Core Roadmap

Scope: **AeroNext.Core and AeroNext.Sim only.** No Godot, no client, no
networking. Godot client and online play are the eventual product — this
roadmap deliberately doesn't plan them — but every milestone below is written
so it doesn't foreclose them (see "Kept in mind" at the bottom).

Each milestone should land as its own small commit or short commit series,
with tests alongside the logic it adds (per CLAUDE.md working preferences).
A milestone is done when `dotnet test` covers it and `AeroNext.Sim` can
demonstrate it from the command line.

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
by 24 tests. The open question above is carried forward, not resolved here.

---

## M3 — Single airline, nonstop routes

One airline, a fleet, and a set of nonstop routes with frequency. Assign
nonstop demand to capacity, compute load factor.

**Done when:** a scripted single-airline run over a few quarters prints
load factors per route.

---

## M4 — Connecting itineraries (the key question)

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

---

## M5 — Turn loop and simultaneous resolution skeleton

Quarter loop: Briefing → Decisions → Resolution, per CLAUDE.md. Even with one
airline, structure resolution as "collect all orders, then resolve" —
architectural from day one, not retrofitted later.

**Done when:** `AeroNext.Sim` runs N quarters against a scripted order list
and prints a briefing-style exception summary each quarter.

---

## M6 — Multiple airlines, shared demand pool

2–4 airlines competing for the same city-pair demand. Market share split
by fare stance / service level / frequency (posture-based, not raw numbers
— see CLAUDE.md "Market" actions).

**Done when:** a scripted 2-airline scenario shows demand splitting
plausibly and one airline's frequency increase visibly eroding the other's
load factor.

---

## M7 — Action economy

3-action-per-quarter budget. Classify actions as contested / market /
private per CLAUDE.md. Enforce the budget; private actions (refinancing,
retiring aircraft, hiring executives) cost nothing.

**Done when:** an over-budget scripted order set is rejected/truncated
deterministically, and executive hires raise the cap for future quarters.

---

## M8 — Contested resolution: landing rights

Silent-commitment bidding at gateway cities. Resolve on
money × relations × reputation × political alignment. Multiple suitors raise
price for everyone. **Losers get partial payoff** (standing improves, next
attempt cheaper) — without this the mechanism dies per CLAUDE.md.

**Done when:** a 3-suitor scripted negotiation at one gate produces one
winner and two improved-but-failed bids, reproducibly under a fixed seed.

---

## M9 — Contested resolution: aircraft orders and alliances

Aircraft orders are public and contest delivery-line position, not the
airframe; leasing is the expensive instant-metal path. Alliance courting:
a regional carrier joins exactly one alliance.

**Done when:** two airlines ordering the same aircraft type in the same
quarter produce different delivery quarters, deterministically.

---

## M10 — Fleet and range economics

Aircraft specs (range, capacity, cost, era availability). Range determines
which routes/chokepoints are viable. This is what makes chokepoints
*obsolete* as range improves across the campaign (per CLAUDE.md map design).

**Done when:** a chokepoint route that was mandatory in an early-era
scripted run becomes bypassable once a longer-range aircraft is available.

---

## M11 — Financial model and scoring

Full quarterly P&L: revenue from itineraries, operating cost, debt service.
Refinancing as a private action. This is explicitly "the scoring function,
not the point" — keep it subordinate to the map/area-control layer, not the
center of the design.

**Done when:** a scripted campaign produces a plausible cash trajectory and
a bankruptcy/failure state triggers correctly.

---

## M12 — Historical arc and timeline events

1955–2015 event data (new aircraft types entering service, historical demand
shifts) driving M10's range unlocks and M2's demand changes over time.
Data-driven (JSON), not hardcoded per-quarter branches.

**Done when:** a full ~240-quarter scripted campaign runs end-to-end and
prints a summary; chokepoint relevance visibly shifts across the run.

---

## M13 — Determinism regression harness

Formalize what M0–M12 assumed informally: same seed + same orders ⇒ byte-
identical resolution output, across repeated runs. This is the safety net
that makes online play and simultaneous multiplayer possible later.

**Done when:** a test runs the same scripted campaign twice and diffs the
full output; any unordered-collection or ambient-state regression fails CI.

---

## Kept in mind, not scheduled

Not part of this roadmap, but constraints below already shape M0–M13 so
these aren't blocked later:

- **Godot client** — Core has zero Godot references (enforced since M0).
  State needed for rendering (exception queue, world map state) should stay
  representable as plain data the client can pull, not something only the
  console printer knows how to produce.
- **Online play / game server** — the M5 simultaneous-resolution skeleton
  and M13 determinism harness exist specifically so a server can replay or
  validate client-submitted orders later without redesigning resolution.
- **Split-screen local multiplayer** — M6/M7's per-airline order collection
  should stay orthogonal to *how many local players* submit orders; the sim
  core shouldn't need to know or care.
- **Turn timer** — a client/UI concern layered on top of the M5 turn loop,
  not a sim-core concept.
