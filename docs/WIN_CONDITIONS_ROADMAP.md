# Win Conditions Roadmap

Continues the shared milestone numbering from `SIM_CORE_ROADMAP.md` (M0-M14)
and `GAME_SETUP_ROADMAP.md` (M15-M19). `HISTORICAL_DYNAMICS_ROADMAP.md`
claims M20-M22 but hasn't built them yet — this is a genuinely separate
concern (no win condition existed anywhere in the codebase, confirmed by
grepping the whole repo before starting), so it picks up at M23 rather than
colliding with that reserved range.

**Origin:** the campaign runs 244 quarters (1955-2015 Q4) and just stops —
nothing ever declares a winner. Discussed directly with the user, who named
the real precedent: Aerobiz Supersonic's own primary win condition was
"greatest passengers carried in multiple regions." Adapted here to this
game's actual data (`CompetitiveRouteUsage`, home regions, per-quarter
resolution) rather than guessed at from memory of the original.

---

## M23 — Bankruptcy: close the infinite-refinance loophole ✅

**The gap:** `FinancialResolver`'s bankruptcy trigger was (and remains)
`cashAfter < Money.Zero` — a pure liquidity check. `DebtBalance` is tracked
completely separately and never subtracted from it. `RefinanceOrder` has no
ceiling, so an airline could stay technically solvent forever by borrowing
just enough cash each quarter, no matter how deep its debt hole actually
is — found while designing the win condition below, since a bankrupt
airline needed a real, closed definition to be excluded from contention.

**Decision, discussed with the user rather than assumed:** keep the
existing cash-balance trigger exactly as-is — simple, already extensively
tested, and matches CLAUDE.md's legibility pillar (a player can see exactly
why: ran out of cash this quarter). Add a second, independent trigger
instead of replacing it: `DebtBalance > DebtCeilingDollars` also sets
`IsBankrupt`. Both feed the same sticky flag; no new state shape needed.

**Done when:** an airline that avoids the cash trigger purely by
refinancing past a reasonable debt level still goes bankrupt. Tests:
debt strictly above the ceiling triggers bankruptcy even with healthy
positive cash; debt at or below the ceiling does not, on its own.

Done. `FinancialResolver.DebtCeilingDollars = 80_000_000` — a flat
constant (not scaled per airline's own starting cash, which varies by
`RegionalEconomics.Factor`), keeping the resolver self-contained rather
than coupling it to `AeroNext.Core.Setup` for one number. Confirmed via
the QA harness's 150-game sweep at 244 quarters: 150/150 clean, no
existing bankruptcy timing regressed (the ceiling is generous enough that
only genuinely runaway debt trips it — no game in the sweep hit this
trigger before the cash trigger already would have).

---

## M24 — Regional passenger dominance win condition ✅

**Three things settled with the user before building anything:**
- **Metric**: passengers carried on routes *the airline itself operates*
  that touch a region (`CompetitiveRouteUsage.SeatsUsed`, not
  `CompetitivePairResult`, which can include connecting itineraries through
  a hub the airline doesn't actually fly to) — "your network's footprint,"
  not total regional traffic or intra-region-only.
- **Threshold shape**: difficulty tiers (which would vary the region count
  needed) don't exist anywhere in the game yet — ship one fixed threshold,
  defer tiers to later. Must hold the lead for N *consecutive* quarters,
  not just the instant checked.
- **Outcome shape**: a single player can win outright mid-campaign; if the
  timeline ends first, fall back to a ranking of all 4.

**`RegionalDominanceTracker`** (`AeroNext.Core.Competition`) advances a
`RegionalDominanceState` (streak per `(airline name, home region)`) every
quarter: for each of the 6 home regions (Africa excluded — a bridge region
no airline is based in, the same boundary `RegionalEconomics.Factor`
already draws), whichever *eligible* airline carries the most passengers on
routes touching that region is the leader; a tie, or all-zero traffic,
means no leader that quarter and breaks anyone's ongoing streak there.

**A real, non-obvious correctness trap, caught before it could ship:**
state is keyed by airline *name* (a plain string), not the `Airline`
record. `Airline` has structural equality over `(Name, Routes, Quality)`,
and a fresh `Airline` object with a new `Routes` list gets built every
single quarter at both real call sites (`AeroNext.Sim`'s M13 loop,
`FullGameRunner`) — keying cross-quarter state by the record itself would
mean it never matches its own prior-quarter entry, silently breaking every
streak at 1 forever. Verified directly: a dedicated test rebuilds the same
named airline with a distinct `Routes` list object between two `Advance`
calls and confirms the streak still increments.

**Eligibility excludes bankrupt airlines by design** — a frozen, bankrupt
airline's stale route list shouldn't be able to "win" a region by default
just because nobody displaced it.

**`WinConditionChecker`** (same namespace): an airline wins the instant it
simultaneously holds a streak at or above `RequiredStreakQuarters` in at
least `RequiredRegionCount` *different* regions. "Simultaneously," not a
historical checklist of regions ever led separately — "greatest passengers
in multiple regions" reads as a single achievement (you dominate N markets
at once), a judgment call flagged explicitly rather than assumed.

**Proposed, tunable constants — shipped, not proven optimal:**
`RequiredRegionCount = 3` (half of 6 home regions), `RequiredStreakQuarters
= 4` (one year). Real data from the 150-game QA sweep: **2/150 games
(1.3%) actually won outright, averaging quarter 149 (~1992)** — the rest
ran to 2015 and fell back to the ranking. Low but nonzero under
`BaselineAi` (a deliberately weak, non-strategic opponent) — a real signal
these constants are in a plausible range, not proof they're right; worth
revisiting once a stronger AI or human play generates more data.

**M42 re-tuning check (`BASELINE_AI_ROADMAP.md`'s M39-M41), reaffirmed
with new data, not silently left stale:** under the strategy-framework AI
(region-aware strategies + M41's negotiation fix), the same 150-game
sweep now wins outright **118/150 (78.7%), averaging quarter 25.3** — a
complete reversal from 1.3%. Tried re-tuning both constants directly
against this new data before reaffirming them as-is: raising
`RequiredRegionCount` from 3 to 4 collapses the win rate to **0/150** —
a hard cliff, not a smooth dial, meaning no current strategy ever builds
a network reaching 4+ of the 6 regions at once, so 4 isn't "harder," it's
unreachable. Raising `RequiredStreakQuarters` alone barely moves win
rate at all (8 quarters: 116/150, avg 29.8; 20 quarters: 114/150, avg
42.6) while pushing the average win quarter up only slightly — once one
airline reaches 3-region dominance, it holds it almost indefinitely,
because a bankrupt rival is permanently frozen (M23) and can never
contest a region back. **Reaffirmed at `RequiredRegionCount = 3`,
`RequiredStreakQuarters = 4`** — neither constant is actually the lever
that controls campaign pacing under this AI; the real lever is that
bankruptcy is a permanent, unrecoverable elimination, which structurally
guarantees an early leader's streak becomes uncontestable once rivals
start dropping out. Worth investigating on its own (does a bankrupt
airline need some kind of comeback path, for pacing's sake if nothing
else?) — flagged here, not fixed, since it's a bankruptcy-mechanic
question, not a win-condition-constant one.

**`CampaignRanking`** (same namespace): if the timeline ends with no
winner, ranks all 4 by (a) regions currently led (streak ≥ 1) descending,
(b) total passengers carried that quarter descending, (c) final net worth
(cash − debt) descending — kept philosophically consistent with the win
metric itself (closest to actually winning), not a different scoring axis
introduced only at the very end.

**Integration, both real quarter loops:** `AeroNext.Sim`'s M13 loop and
`AeroNext.QaHarness/FullGameRunner.cs` both call `RegionalDominanceTracker.
Advance` and `WinConditionChecker.CheckWinner` every quarter, after
`CompetitiveMarket.Resolve` and `FinancialResolver.Resolve`. **The campaign
now genuinely stops early the instant a winner is determined** — not
flagged and left running to 244. `FullGameResult` gained `Winner`,
`WinQuarter`, and `FinalRanking`; `QuartersRun` now reflects the actual
quarters simulated, not the requested count, once a game ends early.

**M13's own demo campaign never triggers a winner, and that's the correct
behavior, not a gap:** it's deliberately flat and symmetric (identical
starting cash, identical `BaselineAi` decisions for all 4 airlines every
quarter), so every region ties every single quarter — ties never extend a
streak, confirming the tie rule holds even under the one scenario in the
codebase most likely to expose a flaw in it. The QA harness's real,
per-region-differentiated games (via `GameSetup`/`StartingConditionsResolver`)
are where a win can actually happen, and did — 2 of them.

**Done when:** `RegionalDominanceTracker`/`WinConditionChecker`/
`CampaignRanking` are directly unit tested (streak increments on sole
leadership, resets on a tie or a new leader, bankrupt airlines excluded,
the name-not-record keying trap, zero-passenger routes create no leader,
Africa creates no entries, ranking tie-breaks in order) before either
integration point; both real quarter loops wire it in and genuinely stop
early on a win; the QA harness reports win rate and average win quarter.

Done. 22 new tests (across `RegionalDominanceTrackerTests`,
`WinConditionCheckerTests`, `CampaignRankingTests`, plus 2 in
`FinancialResolverTests` for M23's debt ceiling), 318 total, 0 failures.
100% line/branch coverage on every new class — one real gap found
(`RegionalDominanceTracker`'s zero-passenger branch, never exercised by
the first test pass) and closed with a test, not waived. Overall project
coverage **99.80%/99.66%**, slightly above the established 99.78%/99.64%
baseline. 150-game QA harness sweep at 244 quarters: 150/150 clean, 0
invariant violations from the new mechanism.

---

## M23 follow-up — declare a real winner instead of a stalemate whenever possible ✅

**Origin:** the user's own direct request, surfaced from a real
observation: a 300-game sweep during unrelated `BaselineAi` work found
94.7% of games ran the full 244 quarters and ended as a pure stalemate
(`Winner` staying `null`) even with 2+ solvent airlines still genuinely
contesting the game — `CampaignRanking` already ranked them "closest to
winning," but nothing ever converted that ranking into an actual
declared outcome. The user's own direction: whenever the campaign
reaches its end (or a point where only one airline remains) without a
real dominance winner, declare one — keeping `CampaignRanking`'s own
existing tie-break order (regions led, then passengers carried, then net
worth) exactly as-is, not passengers carried alone. A true stalemate
should now only happen if every remaining airline goes bankrupt in the
same quarter.

**Shipped, two real additions, no change to the dominance condition or
`CampaignRanking.Rank`'s own tested contract:**
1. `WinConditionChecker.CheckWinner` gained a second win path, checked
   every quarter alongside the existing dominance check — the instant
   only one airline remains solvent ("at any point," not just at
   campaign's end), that airline wins outright, whether or not it
   independently ever satisfied the 3-region/4-quarter condition (not
   guaranteed even for a sole survivor — e.g. a home-region-only airline
   that never expanded). Both real quarter loops (`FullGameRunner.cs`,
   `AeroNext.Sim/Program.cs`) now also break early on true mutual
   elimination (every remaining airline bankrupt the same quarter),
   mirroring the existing "stop the instant the outcome is decided"
   pattern rather than wastefully simulating a frozen game to 244.
2. A new `CampaignRanking.DeclareWinner(ranking, eligibleAirlineNames)`
   converts the existing informational ranking into an actual declared
   winner — the highest-ranked airline that's still actually solvent,
   filtered explicitly rather than trusting the invariant that 0-or-1
   survivors would already have ended the campaign earlier. Both real
   quarter loops call it in their own post-loop tail, only when `Winner`
   is still `null` there.

**Measured: the stalemate rate genuinely collapsed, with zero effect on
the underlying simulation.** Real 300-game sweep: stalemate rate
94.7%→**0%** (300/300 now declare a real winner) — bankruptcy rates are
statistically unchanged (NorthAmerica 75.2%→74.5%, noise-level, exactly
as expected since nothing about bankruptcy or the sim's own economics
changed). Average win quarter rose sharply (56.4→210.7), since most
outcomes are now decided by the end-of-campaign ranking fallback rather
than an early, rare, true 3-region dominance win. The win-share
distribution is far richer as a direct result: EastAsia 50.7%, Europe
24.7%, NorthAmerica 12.7%, SouthWestAsia/AustralAsia/SouthAmerica
3.7-4.3% each — instead of the old picture, where EastAsia held
80-90%+ of the tiny handful of real dominance wins and every other
region's "win share" was near-meaningless noise over a handful of games.

**What this means for every win-rate figure discussed anywhere earlier
in this project's own history:** they used the *old* definition (a
declared `Winner` meant a genuine 3-region dominance win only). Under
this change, "win rate" now means "the campaign resolved to a real,
declared outcome" — expect it near 100% going forward, and read
`WinShare%`/`RegionsLed`/average-win-quarter as the meaningful
comparison axes instead, not raw win-rate-as-a-percentage.

---

## Kept in mind, not scheduled

- **Difficulty tiers** (varying `RequiredRegionCount`, and possibly
  `RequiredStreakQuarters`, by difficulty) — explicitly deferred by the
  user's own answer; no `Difficulty` concept exists anywhere in the game
  yet, and this roadmap didn't invent one to avoid scope creep on top of
  the core mechanism.
- **Briefing integration** — surfacing streak progress in
  `BriefingGenerator`'s exception queue (e.g. "3rd quarter leading
  AustralAsia, 1 to go") is a real, natural legibility follow-up. Not done
  here: `AeroNext.Sim`'s console output already shows the underlying
  numbers each checkpoint quarter, and a dedicated briefing item is
  additional UI-layer work, not required for the mechanism itself to be
  correct.
- **Reconciling `RegionalOutcomeReport`'s existing "wins" column** (highest
  final cash that game — an informal QA comparison, explicitly never
  claimed to be a real victory condition) **with the new, real win data.**
  Both now coexist in the QA harness; folding them into one view once
  there's enough real win data to compare against is a reasonable
  follow-up, not done as part of landing the mechanism itself.
- ~~**Re-tuning `RequiredRegionCount`/`RequiredStreakQuarters`** once a
  stronger AI or actual human play exists~~ — done in M42
  (`BASELINE_AI_ROADMAP.md`), see the re-tuning check above: both
  constants were tried against real M39-M41 data and reaffirmed, not
  revised. What's still open: whether permanent, unrecoverable
  bankruptcy (not either win-condition constant) is the real thing
  worth revisiting for campaign pacing.
