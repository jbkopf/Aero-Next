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

## Connection value when opening routes (2026-10-24)

`AiToolkit.ConnectingPassengersOpenedBy`: a new-route candidate also
counts the one-stop passengers it would newly connect through either
endpoint (pairs the airline can't serve today), split with rivals on each
pair and cut to its proportional share of the existing feeder leg, at
`ConnectingEstimateFactor`. `EstimatedRealProfit` seats them alongside the
pair's own passengers, up to the route's seats.

Estimate vs actual, new routes two quarters after opening (10 Hard games,
full weight): connecting 20.8k estimated / 18.4k actual, local 15.7k /
13.3k. But the AI opened 50% more routes and 29.7% were losing money two
quarters in (13.7% without connection value); the more a route leaned on
connections the worse (75%+ connecting: 41% losing, actual load 0.81x the
estimate) - connections through one hub share feeder legs that each
estimate assumes are its own.

2x300 Hard (seeds 1 / 301):

| | No opening value | Full value (1.0) | Half (0.5) |
|---|---|---|---|
| Games with a bankruptcy | 6 / 5 | 9 / 8 | 13 / 12 |
| South America bankrupt seats | 0 / 4 | 6 / 9 | 10 / 11 |
| Network Optimizer share | 50 / 50% | 69 / 70% | 61 / 62% |
| Avg win quarter | 116 / 113 | 115 / 120 | 115 / 114 |
| Wins NA / EU | 76 / 127, 78 / 123 | 85 / 121, 74 / 120 | 81 / 123, 68 / 124 |
| Wins EA / SWA / AA / SA | 25 / 38 / 32 / 2, 27 / 40 / 28 / 4 | 23 / 35 / 35 / 1, 29 / 42 / 30 / 5 | 28 / 37 / 25 / 6, 28 / 42 / 32 / 6 |

20-game diagnostic, share of passengers who connect in 2005: 30.1% / 35.9%
/ 33.7%; idle aircraft 33.5 / 24.2 / 29.2.

Undecided: every variant costs bankruptcies against no opening value, the
cost landing almost entirely on South America.

## South American bankruptcies, seat return and competitive estimates (2026-10-25)

Tracing the added bankruptcies (all South America, 10 of them Rio): Rio's
airline expanded through Atlanta into long-haul routes justified half by
connections. In its own plan each new route was full; in the market they
flew half empty, for two reasons:

1. Seats an airline's own plan held for a connection that then lost its
   pair to a rival stayed empty, and the local passengers they displaced
   never got them back (Atlanta-Rio: 361 of 5,780 local passengers carried,
   half its seats empty). **Fix: `CompetitiveMarket.ReturnUnusedSeats`** -
   after the contest, each airline's spare seats go to passengers nobody
   carried on pairs its network reaches, nonstop first, split by room x
   Quality, never past the pair's (fare/marketing-adjusted) demand.
2. The opening estimate split each pair evenly with nonstop rivals,
   ignoring how well the new offer competes. **Fix: `CompetitiveMarket.ExpectedShare`**
   (the contest's own seats x Quality x journey-cost weighting) prices
   both the new route's own passengers and every connection it would
   open against last quarter's rival offers on that pair
   (`CompetitivePairResult.Offers` -> `StrategyContext.LastQuarterRivalOffersByPair`).
   A long detour now expects little against a direct rival.

2x300 Hard (seeds 1 / 301), both fixes in, by ConnectingEstimateFactor:

| | 0 | 0.5 | 1.0 | Before the fixes (0.5) |
|---|---|---|---|---|
| Games with a bankruptcy | 3 / 1 | 3 / 1 | 3 / 1 | 13 / 12 |
| South America bankrupt seats | 0 / 2 | 0 / 2 | 0 / 2 | 10 / 11 |
| Network Optimizer share | 10.0 / 9.6% | 12.0 / 10.4% | 15.4 / 15.5% | 60.9 / 62.0% |
| Avg win quarter | 107.7 / 106.0 | 121.4 / 111.3 | 107.6 / 98.2 | 114.7 / 114.0 |
| Contested route-quarters | 40.8 / 40.4% | 44.8 / 43.4% | 45.8 / 43.7% | 38.0 / 36.0% |
| Avg final cash, NA | $21.0B / $23.6B | $42.6B / $46.0B | $37.9B / $37.1B | $25.0B / $29.0B |
| Wins NA / EU | 104 / 83, 101 / 81 | 97 / 94, 90 / 85 | 101 / 84, 102 / 88 | 81 / 123, 68 / 124 |
| Wins EA / SWA / AA / SA | 32 / 36 / 34 / 11, 30 / 41 / 40 / 7 | 29 / 33 / 35 / 12, 32 / 44 / 41 / 8 | 25 / 46 / 37 / 7, 26 / 38 / 39 / 7 | 28 / 37 / 25 / 6, 28 / 42 / 32 / 6 |

The remaining bankruptcies are the long-known weak starts (Baghdad/Tehran
at Q27, Papeete, Buenos Aires/Santiago at Q24).

Speed (one game, four AI airlines, Release build): a quarter takes a median
62 ms in 1955-64, 102-127 ms in 1965-84 and ~350 ms in 1985-2015 (slowest
~400 ms), ~80% of it AI decisions - well inside a 90-second turn. Against
the connections build, ~60 -> ~76 ms per quarter on the same 5 games.
