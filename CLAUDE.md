# Airline Strategy Game — Project Context

A turn-based airline management strategy game. Spiritual successor to Koei's
Aerobiz / Aerobiz Supersonic (SNES, 1992/93), modernized. Four competing
airlines, global map, quarterly turns, historical arc.

Prior art to be aware of: **SkyChart: Airline Executive** (Casey Jones Labs,
Godot 4, Steam Early Access 2026) occupies the faithful single-player
historical-sim lane. This project differentiates on **local and online
competitive multiplayer**, which SkyChart does not do.

---

## Design pillars

**This is an area-control game wearing an economics costume.** Landing slots
are finite; taking one denies a rival. The map is the board. The financial
model is the scoring function, not the point. Any feature that makes this
feel more like a spreadsheet and less like a territorial fight is suspect.

**Attention is the scarce resource, not just money.** Aerobiz's core flaw was
that every action was available every turn, so the correct play was always
"do everything." Fixed by a hard per-turn action budget.

**Legibility over simulation depth.** The player must be able to build a
causal model of why they won or lost a market. Aerobiz was a black box and
that is the main thing being fixed.

---

## Turn structure

One turn = one quarter. Three beats:

1. **Briefing** — what changed since last turn. An *exception queue*, not a
   data dump. Only surface things that crossed a threshold (load factor
   dropped, competitor entered a market, aircraft nearing retirement,
   negotiation resolved). Target 3–5 items regardless of empire size.
2. **Decisions** — player spends a budget of **3 actions**.
3. **Resolution** — all airlines' orders resolve *simultaneously*, then the
   outcome is shown.

**Target 90 seconds for a median turn.** A 1955–2015 campaign at quarterly
pace is ~240 turns. This is a hard pacing constraint, not an aspiration.

If a turn produces no exceptions and the player spends no actions, offer to
skip ahead to the next thing that matters.

---

## Action economy

Three actions per quarter. Cap rises by hiring executives, so management
capacity scales with company size (this is what stops the late game bloating).

Actions are classified by **how they resolve under simultaneity**:

### Contested (several airlines target the same scarce object)
- **Negotiate landing rights** — commit cash + diplomatic effort at a city.
  Resolves on money x relations x reputation x political alignment. Takes
  2–4 quarters to land, so you commit blind and learn late. Multiple
  suitors drive the price up for everyone automatically.
- **Order aircraft** — the contested resource is the *delivery position on
  the production line*, not the aircraft. Order late and you take delivery
  years later. Leasing is the expensive instant-metal escape hatch.
- **Court an alliance partner** — a regional carrier joins only one alliance.

### Market (no shared object, but choices feed the same demand pool)
- **Enter a new city-pair, or assign another aircraft to a pair you already
  serve** — costs an action either time. How many round trips a single
  aircraft can fly each week is derived from its real performance and the
  route's distance, not a number the player picks; growing a route's
  capacity means committing another aircraft to it, not turning a dial.
- Fare stance, service level, marketing — free to set (they're postures, not
  numbers; see below).

### Private (resolves against nothing)
Refinancing, retiring aircraft, hiring executives. **These cost no action.**
Taxing housekeeping just makes the budget feel punitive.

---

## Simultaneous resolution

All airlines commit, then the quarter resolves at once. This is
**architectural — build it this way from day one**, even for single-player.
Retrofitting simultaneity onto a sequential engine means rewriting the core.

**Information gradient** (pure blind commitment degrades into coin-flipping):
- Aircraft orders are **public** — manufacturers announce them.
- Route launches are **filed one quarter ahead** — rivals get one turn of warning.
- Slot negotiations are **silent** until they resolve.

**Losers must always get partial payoff.** With 4 players bidding on one city,
3 lose every time. A failed negotiation still improves standing there, making
the next attempt cheaper. You lost the gate, not the investment. Without this
rule players stop contesting and the whole mechanism dies.

---

## Multiplayer

- **4 players**, matching the original. Local split-screen is the primary target.
- **Always exactly 4 airlines in play.** Any seat not filled by a human is
  filled by a computer player. A 1-human game is a human against 3 AI
  airlines, never a 1-airline world — the sim never runs with fewer than 4
  competing networks. This means AI-controlled airlines aren't an
  eventual-polish feature; the sim core has to support them from early on,
  since there's no scenario where the game runs without them.
- Each player's planning view is **their own network** — panels, exception
  queue, action slots. Not a map. Panels survive quarter-screen; a world map
  does not.
- The **world map is the shared resolution view**, full-screen, all four
  watching the quarter settle together.
- Split-screen means opponents can see your screen. This is fine and good:
  **intent is visible, commitment is sealed.** Rivals see you browsing Cairo;
  they don't see the confirmed target or the cash amount until reveal.
  Lingering on a market you won't enter becomes a real bluff.
- Turn timer required. Length is a playtest variable, not a design decision.
- Co-op (two players sharing one airline and one action budget) is a possible
  future mode on the same engine. Not in scope now.

---

## Map and clash design

Four home regions will politely partition the globe unless prevented. Rules:

**A home region cannot fund a win.** Regional demand bootstraps you and gets
you nowhere near the title. The largest demand pools are *intercontinental*.

**Chokepoints generate clashes automatically.** In early eras aircraft can't
cross oceans, so routes need refueling stops — Wake, Honolulu, Anchorage,
Gander, Azores, Dakar. These cities have almost no passenger demand and
enormous strategic value because they gate access. Everyone needs them and
there are three gates.

**Chokepoints obsolete as range improves.** The player who sank everything
into Pacific stepping-stones watches it become worthless when the 747 arrives.
Clash points therefore *move across the campaign* rather than being a fixed
set players memorize.

**Gateway airports** with hard gate caps control entry into each region.

**Scarcity ratio: gates at any contested city < number of players.** Three
gates and four airlines guarantees someone goes home empty. Four and four is
a queue, not a fight.

---

## Architecture

```
Simulation core (pure C#, zero Godot references)
        |
        +-- Godot 4 client       (rendering, input, UI)
        +-- Test suite           (headless, xUnit, fast)
        +-- Game server          (later, for online play)
```

The core is a plain .NET class library. It must be runnable and testable
without launching an engine — `dotnet test` in a terminal, sub-second. This
is the primary development feedback loop.

**Stack:** Godot 4 + C# (.NET 8). Godot is MIT, no revenue share.
VS Code as editor. GitHub + Actions for CI.

**Data lives in JSON/CSV in the repo**, not baked into code. Cities, aircraft
specs, historical events. Airport data can be sourced from OurAirports or
OpenFlights (public domain).

---

## Determinism rules — non-negotiable

Simultaneous resolution and future online play require identical outcomes
across machines. These are cheap to follow now and painful to retrofit.

1. **Money is integers.** Store cents as `long`. Never `float` or `double`
   for currency — accumulation across 240 turns diverges.
2. **One seeded RNG, passed explicitly.** No static `Random`, no
   `DateTime.Now`, no ambient randomness. Same seed + same orders = same
   quarter, always. This gives replays and reproducible bug reports for free.
3. **Never iterate an unordered collection where it affects outcomes.**
   `Dictionary` enumeration order is not guaranteed. Sort by a stable key
   first. This is the bug that surfaces three months in and costs a week.

---

## Current phase

**Headless sim first. Do not open Godot yet.**

Build a console application: load ~30 cities, run ~40 quarters against
scripted orders, print results as text. No rendering, no input, no UI.

The question this answers, and the reason it comes first: **does
hub-and-spoke strategy emerge from the demand model, or does it have to be
faked?** If a passenger flying Raleigh to Bangkok can be routed through a
player's hub, hub strategy emerges from a gravity demand model plus
connecting itineraries. If it doesn't emerge, the design needs rework before
any UI exists.

Everything else is downstream of getting this right.

---

## Working preferences

- Prefer small, testable commits over large ones.
- Write tests alongside sim logic — the headless core is the whole point.
- Flag any change that would couple the sim core to Godot.
- Flag any violation of the determinism rules above.
- **A milestone isn't complete without a coverage check.** Run
  `dotnet test --collect:"XPlat Code Coverage"` and read the report before
  calling a milestone done — not just "tests pass." Chase every gap it
  surfaces to a real answer: fix it with a test, or determine it's dead/
  unreachable code and remove it (or, rarely, document why it's
  deliberately kept unreachable). Don't chase 100% for its own sake once a
  gap is understood and confirmed to not be a real issue.
