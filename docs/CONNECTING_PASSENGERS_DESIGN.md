# Connecting passengers

Making hub connections carry real traffic. The question CLAUDE.md puts
first - does hub-and-spoke strategy emerge from the demand model? -
can't be answered while connections carry nothing.

## Problem (measured 2026-10-24)

A 20-game Hard trace, every airline every 5 years:

| Year | Routes / airline | One-stop trips offered | Their demand | Share carried | Nonstop demand / seats | Load factor |
|---|---|---|---|---|---|---|
| 1975 | 34 | 107 | 3.0M | 0.09% | 1.8x | 98% |
| 1995 | 71 | 278 | 10.3M | 0.004% | 2.1x | 100% |
| 2005 | 89 | 375 | 12.6M | 0.02% | 2.2x | 100% |

Networks already form hubs - by 2005 an airline offers 375 one-stop trips
whose demand exceeds its whole nonstop demand - but connections carried
almost nothing, because:

1. `NetworkPlanner.Allocate` seated nonstop passengers first, and nonstop
   demand exceeds seats on nearly every route, so no seat was ever left.
2. `CompetitiveMarket` drops an offer that got zero passengers in its own
   airline's plan before passengers compare offers, so a connection never
   competed on price or time either.
3. The AI's extra-aircraft estimate counted local demand only, so a hub
   leg never earned frequency from the connections it could carry.

A connecting passenger pays both legs' fares for two seats, the same per
seat as two locals - no revenue change is needed.

## Change

**Seat sharing (`NetworkPlanner.Allocate`).** When more people want a
route than it has seats, every itinerary using it - nonstop or one-stop -
gets a share in proportion to its demand; a one-stop itinerary gets the
smaller of its two legs' shares, since it needs a seat on both. Seats that
leaves empty (a connection held back by its other leg) are then filled in
the old order, nonstop first, so routes stay as full as before.
Deterministic: shares are plain arithmetic over the itinerary list's fixed
order, and the fill pass sorts by kind, demand and city ids.

Fixing 1 also fixes 2: a connection now gets a non-zero share in its own
airline's plan, so it enters the contest on its journey cost.

**Turned-away passengers (`RouteCapacityUsage.TurnedAway`).** After
allocation, each still-unseated passenger is credited to the leg that held
them back (the full one; the more oversubscribed if both are). The
competitive market carries it onto `CompetitiveRouteUsage`, and the runner
hands it to next quarter's `StrategyContext.LastQuarterTurnedAwayByRoute`.

**Growth estimate (`AiToolkit.MarginalProfitOfAnotherAircraft`).** One
more aircraft on a full route carries what that route turned away - local
and connecting - split evenly with rivals on the pair, up to the added
seats. Without the record it falls back to the old local-demand estimate.

Not in scope: the AI valuing connections when it *opens* a route (only
Domestic Profit Optimizer does, through its own hub-bonus estimate), and
discounted through-fares.

## Results (2026-10-24)

20-game diagnostic, per airline, vs the growth-fix build:

| Decade | Share of one-stop demand carried | Share of passengers who connect | Aircraft per route | Routes | Idle aircraft |
|---|---|---|---|---|---|
| 1975 | 0.12% -> 11.2% | 0.3% -> 25.8% | 1.19 -> 1.57 | 44 -> 38 | 8.4 -> 6.9 |
| 1995 | 0.03% -> 9.9% | 0.1% -> 29.0% | 1.10 -> 1.67 | 83 -> 63 | 15.8 -> 28.2 |
| 2005 | 0.17% -> 11.1% | 0.5% -> 30.1% | 1.10 -> 1.60 | 94 -> 72 | 17.2 -> 33.5 |

Load factors unchanged (98-99%). Networks concentrate: fewer routes, more
aircraft on each. 40-game hub monitor, 1995, share of hub passengers who
connect there: Frankfurt 32%, ICN 29%, Atlanta 30%, Paris 26%, Istanbul
24%, Rio 21%, Bangkok 17%, Singapore 16%, Dubai 11%, Chicago 8%, Dallas 3%
(all were 0-0.3%). Slots used rise with it (Frankfurt 41 -> 64, Atlanta
54 -> 79, Istanbul 30 -> 41).

2x300 Hard (seeds 1 / 301):

| | Growth fix | + connections |
|---|---|---|
| Games with a bankruptcy | 18 / 21 | 6 / 5 |
| Avg win quarter | 115.2 / 120.5 | 115.6 / 112.7 |
| Contested route-quarters | 35.8 / 35.1% | 38.9 / 37.3% |
| Network Optimizer share | 53.0 / 53.0% | 49.7 / 50.0% |
| Wins NA / EU / EA | 73 / 108 / 33, 76 / 107 / 35 | 76 / 127 / 25, 78 / 123 / 27 |
| Wins SWA / AA / SA | 42 / 27 / 17, 42 / 23 / 17 | 38 / 32 / 2, 40 / 28 / 4 |
| Bankrupt seats SA | 17 / 17 | 0 / 4 |

Open: idle aircraft doubled (~33 per airline by 2005); South America
almost never wins (2 / 4 of 300) though it rarely goes bankrupt now;
Europe's share of wins rose to ~42%.

