# Hub Capacity Growth: Design

**Status: proposed, not yet implemented.** Written 2026-10-23 at the
user's direct request: "Review each major hub city for historical growth
and create a growth profile that would mimic real world behavior."

## The problem

Measured on 60 Hard games (temporary instrumentation, reverted): the
largest cause of unused chokepoint slots (55% of unused slot-time) is an
endpoint that can't be opened. At non-gateway hub cities that's the
city's shared capacity running out. The worst offenders are the biggest
hubs - Atlanta, Dubai, Shanghai, Istanbul, Seoul, Frankfurt, Paris - often
blocking two or three airlines at once.

Every one of them is at its capacity ceiling when it blocks:

- **Current rule** (`CityCapacityBoard`, M46 round 2): every 20 quarters a
  city earns one extra unit per 1/N growth in its total demand over its
  1955 level (N = starting capacity), **up to N extra - capacity at most
  doubles.**
- Game demand reaches ~2.1x its 1955 level everywhere by 1975, so nearly
  every city hits that ceiling at the quarter-80 or quarter-100 check.
- Demand then keeps growing to 5-7x by 2015 (Shanghai 84x, Dubai 191x),
  while capacity stays frozen at 6-8 aircraft at the biggest hubs, shared
  by all airlines, for the last ~35 years of the campaign.

The cap was deliberate (stop small cities piling up capacity from a big
*percentage* swing), but it applies the same ceiling to the world's
largest airports, and stops all growth after the first doubling.

## The proposal: capacity follows each hub's real traffic history

Major hubs get a **growth profile**: the real annual passengers of the
whole metro area (every airport serving the city - a game city is a
metro) at 1955, 1965 ... 2015, in `data/hub_traffic_history.json`, with
the history that drove it.

A profiled city's capacity in year Y:

```
capacity(Y) = max( starting capacity,
                   today's demand-driven rule,
                   floor( A x peakTraffic(Y) ^ k ) )
```

- `peakTraffic(Y)`: the highest traffic reached by year Y, interpolated
  log-linearly between the decade points - a ratchet, so a dip (Moscow
  after 1991) never removes capacity, as with today's rule.
- `A` = 1.6, `k` = 0.5 (proposed, tunable): square-root compression, so a
  100M-passenger hub gets ~16, a 40M hub ~10, a 10M hub ~5. Real traffic
  grew far more than the game's demand does; the square root keeps hubs
  scarce while letting their capacity track their real *relative* size.
- Mapped to **absolute** traffic, not growth since 1955: a first draft
  measured growth relative to 1955 and gave Seoul (72M in 2015) more
  capacity than Atlanta (101M) or Chicago (99M), which were already big in
  1955 - the wrong shape.
- Evaluated each year rather than every 5 years, so a hub grows when its
  real airport did.
- Cities without a profile keep today's rule unchanged.

**Data provenance:** the traffic figures are approximations from general
knowledge of each airport's history, roughly +/-30%. What matters is the
shape and timing - when a city boomed or stalled - not the exact figure.
Each entry carries the history behind it.

## Resulting capacity

Game capacity units (aircraft for cities, gates for gateways); "start" is
today's `cities.json` value. Today's rule caps every city at 2x start.

| City | Start | 1955 | 1965 | 1975 | 1985 | 1995 | 2005 | 2015 | 2015 traffic |
|---|---|---|---|---|---|---|---|---|---|
| Dubai | 3 | 3 | 3 | 3 | 3 | 5 | 8 | 14 | 78M |
| Beijing | 4 | 4 | 4 | 4 | 4 | 5 | 10 | 15 | 90M |
| Shanghai | 4 | 4 | 4 | 4 | 4 | 4 | 10 | 15 | 99M |
| Seoul | 3 | 3 | 3 | 3 | 4 | 8 | 10 | 13 | 72M |
| Singapore | 3 | 3 | 3 | 3 | 4 | 7 | 9 | 11 | 55M |
| Istanbul | 3 | 3 | 3 | 3 | 3 | 5 | 7 | 15 | 89M |
| Delhi | 3 | 3 | 3 | 3 | 3 | 5 | 6 | 11 | 48M |
| Mumbai | 3 | 3 | 3 | 3 | 4 | 5 | 6 | 10 | 41M |
| Bangkok | 3 | 3 | 3 | 3 | 5 | 8 | 9 | 14 | 83M |
| Jakarta | 3 | 3 | 3 | 3 | 3 | 5 | 9 | 11 | 54M |
| Moscow | 3 | 3 | 4 | 7 | 8 | 8 | 9 | 14 | 80M |
| Atlanta | 4 | 4 | 4 | 7 | 10 | 12 | 14 | 16 | 101M |
| Chicago | 3 | 4 | 8 | 10 | 11 | 14 | 15 | 15 | 99M |
| Dallas | 3 | 3 | 3 | 6 | 9 | 12 | 12 | 14 | 79M |
| Paris | 3 | 3 | 3 | 6 | 8 | 11 | 13 | 15 | 95M |
| Frankfurt | 3 | 3 | 3 | 5 | 7 | 9 | 11 | 12 | 61M |
| Munich | 3 | 3 | 3 | 3 | 4 | 6 | 8 | 10 | 41M |
| San Francisco | 3 | 3 | 4 | 6 | 8 | 9 | 9 | 11 | 50M |
| Mexico City | 3 | 3 | 3 | 4 | 5 | 6 | 7 | 9 | 38M |
| Osaka | 3 | 3 | 3 | 5 | 6 | 9 | 9 | 9 | 39M |
| Toronto | 2 | 2 | 2 | 5 | 6 | 7 | 8 | 10 | 41M |
| Washington | 2 | 3 | 5 | 6 | 8 | 10 | 12 | 13 | 69M |
| Rome | 2 | 2 | 3 | 4 | 5 | 7 | 9 | 10 | 46M |
| Madrid | 2 | 2 | 2 | 4 | 5 | 6 | 10 | 10 | 47M |
| Milan | 3 | 3 | 3 | 3 | 4 | 5 | 8 | 10 | 40M |
| Rio de Janeiro | 2 | 2 | 2 | 3 | 4 | 4 | 5 | 8 | 26M |
| Lagos | 2 | 2 | 2 | 2 | 3 | 3 | 3 | 4 | 8M |
| *Gateways (optional - see below)* | | | | | | | | | |
| New York | 3 | 6 | 9 | 10 | 13 | 14 | 16 | 17 | 125M |
| Tokyo | 3 | 3 | 3 | 8 | 11 | 13 | 15 | 16 | 110M |
| London | 3 | 3 | 5 | 8 | 11 | 14 | 18 | 19 | 150M |
| Amsterdam | 3 | 3 | 3 | 4 | 5 | 8 | 10 | 12 | 58M |
| Los Angeles | 3 | 3 | 5 | 7 | 10 | 11 | 12 | 13 | 75M |
| Hong Kong | 3 | 3 | 3 | 3 | 4 | 8 | 10 | 13 | 68M |
| Sao Paulo | 3 | 3 | 3 | 3 | 5 | 5 | 9 | 12 | 63M |
| Cairo | 3 | 3 | 3 | 3 | 3 | 4 | 5 | 6 | 15M |
| Sydney | 2 | 2 | 2 | 3 | 4 | 6 | 8 | 9 | 39M |

What this reproduces:

- **Dubai** stays a minor airport until the late 1980s, then grows fast
  after Emirates (1985) and the Gulf-stopover strategy - 5, 8, 14 - ending
  close to the world's largest hubs, as the Europe-Asia stopover of the
  long-haul jet age.
- **Beijing and Shanghai** stay flat through the closed-economy years,
  then jump after the 1978 reforms, 1992 opening and 2001 WTO accession.
- **Istanbul and Bangkok** surge late (Turkish Airlines' transfer hub;
  Suvarnabhumi plus low-cost carriers).
- **Chicago, New York, Washington, Atlanta** - already big in the 1950s -
  grow early and level off as mature, runway-constrained airports.
- **Moscow** holds through the post-1991 slump (the ratchet) and resumes.

## Open questions

1. **Gateways.** The design pillar wants contested gateways scarce
   ("gates fewer than players"). Today's rule already lets them double to
   6 gates, past the 4-player bar. The endpoint trace found gateway
   blocking was mostly the airline not holding a slot, with gates still
   free in 85% of cases - so gateway capacity isn't the bottleneck. Proposal:
   **profiles for non-gateway hubs only**; gateways stay on today's rule.
2. **Starting balance.** New York, Chicago and Washington were already big
   in 1955, so their profile exceeds their starting capacity immediately
   (New York 3 -> 6). Options: accept it (closer to history), or apply
   profiles from the first expansion check onward.
3. **Dubai's game demand** grows 191x by 2015 (historical events), far
   beyond its real traffic growth relative to other hubs. Even at 14 it
   stays the most capacity-starved hub in the game. A demand-side review
   may be worth it separately.
4. **Tuning.** `A` and `k` are one-line constants; the same 2x300 Hard
   sweep method as other tunables applies.

## How it would be measured

2x300 Hard sweeps against the current build, plus the endpoint trace:

- **Target:** endpoint blocking at the big hubs, and unused chokepoint
  slot-time.
- **Guardrails:** bankruptcies, win timing, win share by region, contested
  route-quarters (more capacity could mean more head-to-head routes or
  less, since rivals no longer crowd each other out), multi-stop route
  orders.

## Hub demand coverage (2026-10-24, experiment)

Even with profiles, demand at a hub ran 4-35x the seats flown there, so
most of it could never be competed for. `CityCapacityBoard.HubDemandCoverage
= 0.8`: a profiled non-gateway hub's capacity never falls below the
aircraft needed to fly 80% of its current demand, at the average seats per
aircraft of the routes ending there (`SeatsPerAircraftByCity`; network
average for a hub nobody serves). Every quarter, a ratchet.

2x300 Hard sweeps (seeds 1 / 301) vs the 800km-floor build:

| | 800km floor | + 80% coverage |
|---|---|---|
| Games with a bankruptcy | 5 / 3 | 14 / 13 |
| Avg win quarter | 138.5 / 137.7 | 133.7 / 131.8 |
| Contested route-quarters | 12.4 / 11.9% | 38.6 / 37.7% |
| Route-quarters flown | 4.64M / 4.50M | 6.77M / 6.63M |
| Network Optimizer share | 14.0 / 12.5% | 53.1 / 51.6% |
| Wins NA / EU | 82 / 66, 82 / 60 | 76 / 126, 66 / 110 |
| Wins EA / SWA / AA / SA | 40 / 45 / 21 / 46, 35 / 50 / 24 / 49 | 31 / 31 / 18 / 18, 42 / 42 / 18 / 22 |
| Bankrupt seats SA / SWA | 0 / 4, 3 / 0 | 9 / 8, 7 / 4 |

40-game monitor, 2005: hub capacity rises from 6-15 to 30-270 slots; slots
used grow 2-6x (Frankfurt 11 -> 46, Paris 13 -> 53, Dubai 8 -> 49, Atlanta
14 -> 54); the share of hub demand carried rises from 5-20% to 25-55% at
the big European hubs and Atlanta. Most of the new capacity stays unused -
aircraft, cash and the other endpoint now bind. Connecting passengers stay
at 0-1%. Because capacity now follows in-game demand, the over-weighted
European hubs (and Dubai's scripted demand) get the most of it, which
lines up with Europe's win surge.

