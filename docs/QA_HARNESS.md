# QA Harness

`AeroNext.QaHarness` runs many full games end-to-end and checks a checklist
of correctness invariants across all of them. It's deliberately separate
from `dotnet test`: CLAUDE.md sets a sub-second target for that suite, and a
full campaign (up to 244 quarters, the whole 1955-2015 span) times out that
budget fast. This is the slower, broader QA tier — "does the whole system
hold together across the real space of games," not fast per-commit
feedback. Not wired into any per-milestone gate; run it on demand.

## Running it

```
dotnet run --project src/AeroNext.QaHarness -- [--games N] [--quarters N] [--seed N]
```

Defaults: 100 games, 244 quarters (the full campaign), seed 1. ~100 games
at full length takes roughly 30 seconds on a typical dev machine (200 games
was benchmarked at ~69s). Exit code is 0 if every game passed, 1 otherwise
— usable in a script or CI job later without parsing output.

## What one "game" is

For each game, a `SimRng` seeded from `--seed + game index` generates a
random-but-valid human configuration (0-4 humans, each with a distinct home
region and an eligible starting city — see `GameConfigGenerator`), then runs
it through the real production chain: `GameSetup.CreateSeats` →
`StartingConditionsResolver.Resolve` per seat → `QuarterEngine` per seat per
quarter → `CompetitiveMarket.Resolve` → `FinancialResolver.Resolve`, plus
`LandingRightsResolver` for any negotiations `BaselineAi` submits along the
way. A second, independent `SimRng` (seed `+500,000`) drives the negotiation
delay draws, kept separate from the config-generation RNG.

**Quarter 1 is seeded with each seat's M18-guaranteed route explicitly, not
BaselineAi's own pick.** This is a real finding worth knowing, not an
arbitrary choice: `BaselineAi.DecideOrders` has no concept of "home region"
or "starting city" — on an empty route list it searches every city pair in
the world and picks the single globally-best one, so all 4 seats would
otherwise make the *identical* first move regardless of M15-M19's
differentiated setup, exactly like before that roadmap existed. Injecting
the guaranteed route for quarter 1 is what actually lets the differentiation
reach into the game; from quarter 2 on, `BaselineAi` drives every seat
normally, and divergence from there is a genuine knock-on effect of each
seat's different quarter-1 history — not because the AI is itself
region-aware. Extending `BaselineAi` to be region/network-aware is exactly
the kind of AI-improvement work the game setup roadmap explicitly deferred.

**Also more complete than `AeroNext.Sim`'s own M13 campaign loop:**
`BaselineAi` does submit `NegotiationOrder`s (per M9), but M13's loop never
wires them into `LandingRightsResolver` — those actions are silently wasted
there. This harness resolves them for real every quarter, since a QA sweep
should exercise the same resolver surface a real game would. This is a
pre-existing gap in M13's demo, noted here rather than fixed there (M13's
own recorded numbers stay untouched).

## What's checked, every quarter, every seat

- The M18-guaranteed quarter-1 route is never rejected.
- Route count never decreases for a non-bankrupt seat (routes are only ever
  added or updated, never removed).
- Action cap never decreases.
- Once bankrupt, an airline stays bankrupt (sticky) and its routes/action
  cap are frozen — the exact regression class of the real bug M13 found and
  fixed, re-verified here on every quarter of every generated game, not
  just M13's own hand-scripted scenario. Verified to actually catch this
  class of bug: temporarily reintroducing it during development produced
  4 violations per quarter across every affected game, immediately.
- Cash/debt magnitude stays within a sane bound (catches a sign-flip or
  overflow, not a plausible airline balance).
- `GameSetup` always produces exactly 4 seats with 4 distinct home regions
  and 4 distinct starting cities.

Bankruptcy itself is never treated as a failure — it's a legitimate
simulated outcome, not a correctness violation, and every single game
during development produced at least one bankruptcy before quarter 244
(consistent with, and now confirmed at scale beyond, the M13 finding that
`BaselineAi`'s unconditional frequency-growth heuristic reliably bankrupts
airlines given enough quarters). The harness reports how many games saw a
bankruptcy as a statistic, not a pass/fail signal.

## Reproducing a failure

Every reported violation and crash prints its `configSeed` and
`campaignSeed`. Re-running with `--seed` set so that `baseSeed + gameIndex`
reproduces the same `configSeed` (or calling `FullGameRunner.Run` directly
with the printed seeds) replays the exact same game, since the whole chain
is deterministic per CLAUDE.md's rules.
