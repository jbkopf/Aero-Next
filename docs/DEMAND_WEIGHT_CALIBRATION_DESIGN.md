# Demand weight calibration

Bringing each major city's share of in-game demand in line with its real
counterpart. Follows docs/HUB_CAPACITY_GROWTH_DESIGN.md: once hubs had
capacity to match their history, the demand monitor showed in-game demand
itself didn't follow history.

## Problem

2015 share of demand among the 27 profiled non-gateway hubs, game vs real
(40-game monitor, hub capacity profiles on):

| Over-weighted | Game | Real | Under-weighted | Game | Real |
|---|---|---|---|---|---|
| Dubai | 20.4% | 4.7% | Atlanta | 1.9% | 6.1% |
| Frankfurt | 9.1% | 3.7% | Chicago | 1.5% | 5.9% |
| Munich | 6.5% | 2.5% | Dallas | 1.4% | 4.7% |
| Milan | 5.2% | 2.4% | Bangkok | 1.7% | 5.0% |
| Paris | 7.9% | 5.7% | Jakarta | 1.0% | 3.2% |

City weights are fairly flat (60-125), so the spread comes from three
places:

1. **Short-haul geography.** Pair demand falls with distance squared, with
   a 100km floor. Europe's dense cluster of 300-700km pairs gave its hubs
   81-88% of their demand from inside Europe, and Europe 42% of world
   demand. Real trips that short mostly go by rail or road.
2. **Scripted city growth.** Only a few cities have history events, and
   Dubai's (0.3x to 10x) multiplies every Dubai pair. Late boomers without
   one (Istanbul, Delhi, Seoul, Jakarta) grow exactly like mature hubs.
3. **Late booms.** Same cause as 2, from the other side.

## Scope rule

The game deliberately models hubs and important cities only. Traffic from
smaller domestic partners (Atlanta's US feeder cities) is represented by
the demand on inter-hub pairs. Adding feeder cities is out of scope.

## Step 1: ground-competition distance (this change)

`GravityDemandModel.ShippedGroundCompetitionDistanceKm = 800`: the gravity
formula treats any pair closer than 800km as 800km apart. A pair's
reported `DistanceKm` stays its real distance, so routes, fares and range
are unaffected. Opt-in at construction like the AustralAsia multiplier;
every real per-quarter call site passes it.

Static scan of floors (formula only, no history events, profiled hubs):

| Floor | World demand | Cross-region share | Mean abs log(game/real share) | Europe share |
|---|---|---|---|---|
| 100km | 1.00x | 33.9% | 0.67 | 42.3% |
| 400km | 0.81x | 42.0% | 0.60 | 38.4% |
| 600km | 0.68x | 49.3% | 0.51 | 32.3% |
| 800km | 0.60x | 55.1% | 0.45 | 27.4% |
| 1000km | 0.54x | 59.9% | 0.41 | 23.7% |

800km is where the spread between the most over- and under-weighted hub
stops improving (7.1x at 600 and 800, 7.4x at 1000). Munich and Frankfurt
are still ~2.5x over at 800km; step 2 calibrates the remainder.

## Step 2: per-city calibration (next)

Solve offline, per decade, for one multiplier per profiled city so its
share of demand matches its real target share; write them as city Trend
entries in demand_growth.json, replacing Dubai's hand-written curve, and
normalize so total demand is unchanged.

**Target = local + feeder traffic, not airport throughput.** A hub's real
traffic is:

- local passengers - in the target;
- connections from small feeder cities (Savannah-Atlanta-London) - in the
  target, since in the game that is Atlanta-London demand;
- connections between modelled cities (London-Dubai-Bangkok) - **not** in
  the target. The game should produce these when players route passengers
  through a hub; baking them into the hub's own demand would fake the
  hub-and-spoke emergence the sim exists to test.

US hubs' connecting traffic is mostly feeder, so their target stays near
full real traffic. Dubai, Istanbul, Frankfurt, Doha-style hubs carry
mostly connections between modelled cities, so their target falls well
below their airport totals.

**Room for connecting flights.** Lowering a connecting hub's own demand
must not lower its capacity: players need slots there to build the
connecting flights. Capacity profiles (HubCapacityProfile) stay on total
real traffic, connections included. The monitor tracks connecting
passengers through each hub (seats used on routes ending there, minus
local passengers carried, halved) alongside slot use, so we can check
connection hubs fill with connections rather than starving.

## Implementation (step 2)

- `HubCapacityProfile.ModelledConnectingShare`: optional per-hub series in
  data/hub_traffic_history.json (rough estimates: Dubai to 55% by 2015,
  Frankfurt/Singapore ~30-35%, Istanbul to 30%, US hubs 5%).
- `CityDemandCalibrator.Solve`: damped fixed-point solve per 5-year step
  (1955-2015), 27 hubs, events excluded, group total preserved.
- `AeroNext.QaHarness --calibrate-demand data/city_demand_calibration.json`
  writes the 351 breakpoints; `DemandGrowthCatalog.LoadCampaignSchedule`
  loads them and rejects a hand-scripted trend for a calibrated city.
  Dubai's, Moscow's, Beijing's and Shanghai's scripted trends are retired;
  their Olympics events stay. A test keeps the shipped file in sync with a
  fresh solve.

## Results

2x300 Hard sweeps (seeds 1 / 301), against the hub-capacity-profile build:

| | Hub profiles | + step 1 (800km) | + step 2 (calibration) |
|---|---|---|---|
| Games with a bankruptcy | 4 / 2 | 5 / 3 | 3 / 1 |
| Avg win quarter | 144.3 / 140.4 | 138.5 / 137.7 | 122.6 / 113.7 |
| Contested route-quarters | 14.2 / 13.8% | 12.4 / 11.9% | 11.8 / 11.7% |
| Multi-stop route orders | 2,111 / 2,077 | 2,196 / 2,208 | 2,863 / 2,628 |
| Network Optimizer share | 13.6 / 12.5% | 14.0 / 12.5% | 7.2 / 7.2% |
| Wins NA | 58 / 52 | 82 / 82 | 88 / 64 |
| Wins EU | 73 / 72 | 66 / 60 | 81 / 91 |
| Wins EA | 39 / 49 | 40 / 35 | 11 / 19 |
| Wins SWA | 58 / 49 | 45 / 50 | 54 / 57 |
| Wins AA | 20 / 19 | 21 / 24 | 48 / 50 |
| Wins SA | 52 / 59 | 46 / 49 | 18 / 19 |

40-game monitor, mean |log(game share / target share)| over the 27 hubs:

| Year | 100km | 800km | Calibrated |
|---|---|---|---|
| 1975 | 0.84 | 0.93 | 0.00 |
| 1995 | 0.74 | 0.66 | 0.00 |
| 2005 | 0.73 | 0.62 | 0.00 |

**Connections still don't happen.** Passengers connecting at a hub stay at
0-1% at every hub in all three builds (real Dubai/Atlanta/Frankfurt
~55-65%). Hub demand still exceeds the seats flown there 4-20x, and
NetworkPlanner.Allocate seats nonstop passengers first, so a connecting
passenger never finds a free seat. Calibration doesn't change that; it
needs a seat-allocation decision (open).

**Step 2 reverted (2026-10-24).** Matching real history made early-era
North America and Europe dominant (East Asia's and South America's wins
fell by about two thirds), which the user judged too unbalanced. The
calibrator, its data file and the connecting-share series were removed;
step 1 stays.

## Balance damping (2026-10-24, in place of step 2)

Europe-to-Europe pairs at 0.5x (`GravityDemandModel.ShippedIntraRegionMultipliers`,
now holding AustralAsia 1.5 and Europe 0.5) and Dubai's scripted curve
divided by 4 (0.075 / 0.375 / 1.0 / 2.5). Measured with hub demand
coverage (80%) on, 2x300 Hard (seeds 1 / 301):

| | 80% coverage | + damping |
|---|---|---|
| Games with a bankruptcy | 14 / 13 | 14 / 11 |
| Avg win quarter | 133.7 / 131.8 | 131.5 / 129.5 |
| Contested route-quarters | 38.6 / 37.7% | 35.4 / 34.7% |
| Network Optimizer share | 53.1 / 51.6% | 49.5 / 50.0% |
| Wins NA / EU / EA | 76 / 126 / 31, 66 / 110 / 42 | 88 / 102 / 31, 76 / 103 / 42 |
| Wins SWA / AA / SA | 31 / 18 / 18, 42 / 18 / 22 | 29 / 28 / 22, 39 / 22 / 18 |
| Bankrupt seats SWA / SA | 8 / 9, 4 / 7 | 8 / 11, 3 / 7 |

Monitor: European hub demand falls to ~0.65x (Istanbul 0.8x - its
cross-region pairs are untouched), Dubai to 0.25x; their coverage capacity
falls with it (Frankfurt 173 -> 108 slots, Dubai 158 -> 85 in 1995).
Connections still 0-1%.
