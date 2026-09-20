# Historical Geopolitical Aircraft Restrictions: Design

**Status: proposal, not implemented.** Written after a feasibility
investigation (the user's own direct request: "have we done anything to
limit availability of aircraft based on historical geo-political
effects... such as the Soviet Union not having access to American
aircraft") reported directly in conversation, then a follow-up request to
map real historical events onto the game's own cities and aircraft
catalog. Extends `docs/AIRCRAFT_ACQUISITION_ROADMAP.md`'s own two-phase
acquisition machinery — this is a third gate alongside the era window and
the `Quantity` check, not a separate mechanism.

**Revised after a follow-up research pass** (the user's own direct
request: what could China actually have bought or built to cover the
gap?) that overturned this doc's own original headline claim — see "The
correction" below. The original version modeled a single undifferentiated
`Western` bloc and concluded Beijing would hit a real 48-quarter
zero-aircraft window in 1960–1972. That was wrong: Britain recognized the
PRC diplomatically in 1950 (the US didn't until 1979) and was never part
of the US-specific embargo, so British aircraft stayed purchasable the
whole time — CAAC really did buy 6 Vickers Viscounts in 1963 and, in
August 1971, ordered Hawker Siddeley Tridents directly, a full year
*before* Nixon's visit and the Boeing deal. A single `Western` bloc can't
represent that; the design below now splits `American` from `European`.

## The finding that shapes everything here: `Region` can't carry this

`City` (`src/AeroNext.Core/World/City.cs`) carries no country, no ISO
code, no political identity beyond the coarse 7-value `Region` enum. Every
real embargo checked below cuts *through* an existing region rather than
along its edge:

| Region | Cities needing opposite treatment from their neighbors |
|---|---|
| Europe | Moscow, Kiev, Minsk, Rostov (USSR) alongside London, Paris, Frankfurt (NATO West) |
| EastAsia | Beijing, Shanghai (PRC) alongside Taipei (Taiwan/ROC — a US ally, recognized by Washington as "China" until 1979) and Tokyo/Osaka/Fukuoka (Japan); Khabarovsk (Soviet Far East) also lives here |
| SouthWestAsia | Tashkent (Soviet Uzbekistan) alongside Tehran (only embargoed from 1979) and non-aligned Delhi/Mumbai |
| NorthAmerica | Havana (embargoed from 1960) alongside Atlanta, Toronto, Mexico City |

**Consequence for the design below: every restriction is keyed to a
specific `CityId`, never a `Region`.** A region-scoped rule would
misfire on at least one of these four home regions immediately.

## Historical events being modeled

| Event | Cut off from | Start | End |
|---|---|---|---|
| Cold War COCOM embargo on the Soviet Bloc | Soviet-aligned cities ↔ American *and* European aircraft | 1948 | 1991 |
| Sino-Soviet split | China ↔ Soviet aircraft/cooperation | 1960 (Soviet advisors withdrawn, Jul 1960) | 1989 |
| US trade embargo on the PRC | China ↔ **American** aircraft specifically | 1949 | 1972 (Boeing 707 contract, Sep 1972) |
| US embargo on Cuba | Havana ↔ American aircraft | 1960 | never lifted (open-ended through the campaign's 2015 end) |
| US sanctions on Iran | Tehran ↔ American aircraft | 1979 | never lifted through 2015 |

**The correction, and why it matters more than it looks:** the PRC
embargo was a *US* policy, not a Western one — the UK recognized Beijing
diplomatically in 1950 and was never bound by it. Real history: CAAC
bought 6 Vickers Viscounts in 1963 (mid-"gap"), then 4 used Trident 1s
from Pakistan International Airlines in 1970, then ordered 6 new Trident
2Es directly from Hawker Siddeley in **August 1971** — a full year before
Boeing's own 1972 deal. China's own indigenous attempt at self-
sufficiency (the Xian Y-7, a licensed An-24 copy started in 1966 using
pre-split Soviet data plus reverse-engineering) didn't even fly until
December 1970 and wasn't in real production until 1977 — it contributes
nothing to this window.

Cross-referencing against `data/aircraft.json`'s real availability
windows with the corrected, bloc-split model: a Beijing-based airline
(EastAsia's own AI-default home city, per `GameSetup.CreateSeats`'s
highest-`DemandWeight`-non-gateway rule) is **never actually at zero**.
The Comet (purchasable through 1964), the Viscount (through 1963), and
the Caravelle (1959 straight through 1972) all stay open the entire
1960–1972 window, since none of them is American. The real effect is a
**narrowed catalog, not a dead one** — American types (707, DC-8, 727,
DC-9...) drop out from 1949–1972, Soviet types drop out from 1960–1989,
and European types are the only thing left standing for that whole
stretch. That's exactly what CAAC actually did. No release valve needs
inventing (see "Open questions" §1 below, now resolved) — the accurate
model already has one, because reality did.

## Data model

One new nullable-permissive-default field on `AircraftType`, same
convention every optional field in this codebase already follows, plus
one new data-driven restriction list. Deliberately kept separate from the
*existing* `AircraftType.ManufacturerRegion` field (which stays exactly
what it is today: flavor + one-time starting-fleet bias, per
`RegionalFleet.AppropriateAircraft`) rather than overloading it, because
the two questions are genuinely different: "whose industry gets to claim
this as a home-region starting pick" is not "whose political bloc built
it." The clearest proof they're different axes: Ilyushin/Tupolev/Sukhoi
already carry no `ManufacturerRegion` at all (deliberately, per
`docs/GAME_SETUP_ROADMAP.md` — "the USSR doesn't map cleanly onto this
game's region model") while very much needing a bloc tag for this
mechanic to say anything about them.

```csharp
public enum PoliticalBloc { American, European, Soviet }
```

Three values, not two — the whole point of the correction above.
`American` and `European` both being "the West" only ever matters for the
Soviet-facing restriction (COCOM applied broadly); for the China-facing
restriction they diverge, and that divergence is the entire reason a
Beijing-based airline isn't stuck at zero.

**`AircraftType`** gains one new field:

```csharp
PoliticalBloc? Bloc = null   // null = not modeled / no restriction ever applies to this type
```

Tagged `American` for every NorthAmerica-manufactured type (Boeing,
Douglas, Lockheed, Convair, McDonnell Douglas, Bombardier), `European`
for every Europe-manufactured type (de Havilland, Vickers, Sud Aviation,
BAC, Aerospatiale/BAC, Airbus, ATR), and `Soviet` for Ilyushin/Tupolev/
Sukhoi. Embraer (South America) and COMAC (modern China, 2008+) stay
`null` — neither is embargoed in this scope, so there's nothing to tag
them against. Note this now runs exactly parallel to the existing
`ManufacturerRegion` grouping for every Western aircraft (NorthAmerica →
`American`, Europe → `European`) — the two fields only *diverge* for the
Soviet types, which is precisely the case `ManufacturerRegion` already
leaves blank on purpose.

**New record**, mirroring `PendingLeaseRequest`/`LeaseDelivery`'s own
one-record-per-file convention:

```csharp
public sealed record TradeRestriction(
    string CityId,
    PoliticalBloc RestrictedBloc,
    int StartYear,
    int? EndYear);   // null = never lifted within the campaign
```

Loaded from a new `data/trade-restrictions.json`, same
loader-plus-validation shape `AircraftCatalog` already has for
`aircraft.json` (unknown `CityId` and `EndYear < StartYear` both reject at
load time, not silently).

**Gating**, a new method alongside `Timeline.IsAvailableForPurchase`/
`IsAvailableForLease` rather than folded into them — this axis is
airline-identity-dependent (needs the ordering airline's home city) where
the other two are global, so it can't share their signature:

```csharp
public static bool IsRestricted(
    AircraftType aircraft, City? homeCity, int year,
    IReadOnlyList<TradeRestriction> restrictions) =>
    homeCity is not null
    && aircraft.Bloc is not null
    && restrictions.Any(r =>
        r.CityId == homeCity.Id
        && r.RestrictedBloc == aircraft.Bloc
        && r.StartYear <= year
        && (r.EndYear is null || year <= r.EndYear));
```

`homeCity: null` (the permissive default, matching every other optional
identity field here) means "no restriction ever applies" — the safe
reading, and byte-identical to today for every existing caller that
doesn't pass one.

## Where this plugs into `QuarterEngine`

Good news, checked directly rather than assumed: `QuarterEngine.RunQuarter`
**already takes a `City? homeCity` parameter** (`QuarterEngine.cs:191`,
currently used only to compute `validOriginCityIds` for new-route
origination). No new plumbing project — `IsAircraftAvailable`
(`QuarterEngine.cs:840`) just needs `homeCity` and `tradeRestrictions`
threaded into its two acquisition-order arms:

```csharp
AircraftOrder ao => Timeline.IsAvailableForPurchase(ao.Type, year)
    && !TradeRestriction.IsRestricted(ao.Type, homeCity, year, tradeRestrictions),
LeaseAircraftOrder lo => Timeline.IsAvailableForLease(lo.Type, year)
    && !TradeRestriction.IsRestricted(lo.Type, homeCity, year, tradeRestrictions),
```

`RouteOrder`/`AdjustRouteOrder` stay untouched — same "flying an
already-acquired aircraft needs no upper bound, only *acquiring* a new one
does" split the whole acquisition-limits plan already established. A
Beijing-based airline caught in the 1960–1972 window can keep flying
whatever it already owns regardless of bloc, and can still grow or
replace using European types the whole time — it's specifically American
and Soviet growth that goes dry, exactly like the real CAAC, which kept
its pre-1960 fleet flying *and* kept buying British aircraft throughout.

`RunQuarter` gains one new optional parameter,
`IReadOnlyList<TradeRestriction>? tradeRestrictions = null` — permissive
default, so this whole mechanic is opt-in and every existing test/caller
is unaffected until a real restriction list is actually passed.

## The AI ripple — smaller than the acquisition-limits plan's nine call sites

Checked directly: `StrategyContext.EraAvailableTypes` is computed in
**exactly one place** per real quarter loop
(`Timeline.AvailableAircraft(fleetCatalog, year)`,
`FullGameRunner.cs:158`), *before* the per-airline loop, then reused for
every airline's own `StrategyContext`. That's the one seam to update, not
nine — but it's a real one: today it's computed once per quarter, region-
and airline-agnostic; making it bloc-aware means computing it *inside*
the per-airline loop instead (it now depends on which airline's home city
it's being built for).

This AI-side change is a pure efficiency concern, not a correctness one,
and can ship as its own follow-up phase: `QuarterEngine`'s own gate above
is the authoritative enforcement point regardless. Without the AI update,
a Beijing-based AI airline picking an American or Soviet type as its own
best candidate during an embargoed window would keep proposing it every
quarter, having it rejected each time — a real efficiency loss, but a
narrower one than this doc originally implied, since European candidates
(now correctly unrestricted) still get through fine. Worth noting: this
exact "old, pre-existing gap" already exists today in a smaller form —
`EraAvailableTypes` is filtered by `Timeline.IsAvailable` (the floor-only
*flying* check), not `IsAvailableForPurchase`, so the AI can already
propose an order for a type that's flyable-but-past-its-own-
`EndOfManufactureYear`; `QuarterEngine` is already the real backstop for
that case too. Fixing both at once (thread `year`/`homeCity` all the way
through `EraAvailableTypes`'s own computation) is probably the right
scope for that follow-up phase, not just the new bloc check in isolation.

## Open questions

Down to two — the third (the release-valve question) is resolved by the
correction above.

1. ~~Is a genuine multi-year "nothing to buy or lease" wall good design,
   or does it need a release valve?~~ **Resolved.** The historically
   accurate, bloc-split model doesn't produce a wall in the first place —
   Beijing's own catalog narrows to European types for 1960–1972 rather
   than emptying out. No synthetic release valve (a black-market lease,
   a special-case exception) needs inventing; the corrected data already
   behaves the way good design would want it to, because that's what
   actually happened.
2. **Scope of the initial `PoliticalBloc` tagging and restriction list.**
   The doc proposes tagging every catalog aircraft's bloc and authoring
   18 initial `TradeRestriction` rows (below) in one pass, mirroring how
   `EndOfManufactureYear` was researched for the whole catalog at once
   rather than piecemeal. Confirm that's the right size for a first PR
   versus starting with just the China rows, which are the only ones
   that visibly change any AI/player-facing catalog within the current
   campaign's home-city defaults.
3. **Human-chosen home cities.** A human player can start anywhere in a
   region's non-gateway city list — e.g. Taipei instead of Beijing in
   EastAsia, which correctly avoids the embargo entirely (accurate:
   Taiwan was never embargoed). Confirm the briefing/UI surfaces *why* a
   Beijing player's own catalog just narrowed to European-only for over a
   decade, rather than it reading as a silent bug — this is exactly the
   kind of thing CLAUDE.md's "legibility over simulation depth" pillar
   exists to protect against, and it's a real narrowing worth explaining
   even though it's no longer a total blackout.

## A real catalog gap this research surfaced, independent of this mechanism

The **Hawker Siddeley Trident** — the aircraft China actually used to
plug most of this window (117 built 1962–1978; China became its largest
operator outside Britain, 35 total) — isn't in `data/aircraft.json` at
all today. Worth adding as its own small follow-up regardless of whether
this bloc-restriction mechanic ships: `availableFromYear: 1962`,
`endOfManufactureYear: 1978`, `manufacturerRegion: Europe`,
`bloc: European`. It directly narrates the China case once restrictions
exist, and it's a real, well-documented catalog omission on its own
merits either way.

## Proposed initial data (not yet authored into `data/`)

**`AircraftType.Bloc` tagging:** `American` — dc3, l049, dc6, cv340, dc7,
707, dc8, 727, dc9, dc10, l1011, md80, crj100, 767, 757, 737, 747, 777,
787 (Boeing/Douglas/Lockheed/Convair/McDonnell Douglas/Bombardier).
`European` — comet, viscount, caravelle, bac111, a300, concorde, a340,
atr72, a320, a330, a380, a350 (de Havilland/Vickers/Sud Aviation/BAC/
Aerospatiale-BAC/Airbus/ATR). `Soviet` — il14, tu104, il62, tu154, il86,
ssj100. `null` (unmodeled) — emb120, e190 (Embraer/South America), arj21
(COMAC/modern China).

**`data/trade-restrictions.json`** (18 rows — Soviet-bloc cities need two
rows each, since COCOM restricted both Western sub-blocs equally; China
needs two rows of a different shape, since only the American one actually
bites):

| CityId | RestrictedBloc | StartYear | EndYear |
|---|---|---|---|
| mow (Moscow) | American | 1948 | 1991 |
| mow (Moscow) | European | 1948 | 1991 |
| kie (Kiev) | American | 1948 | 1991 |
| kie (Kiev) | European | 1948 | 1991 |
| msq (Minsk) | American | 1948 | 1991 |
| msq (Minsk) | European | 1948 | 1991 |
| rov (Rostov) | American | 1948 | 1991 |
| rov (Rostov) | European | 1948 | 1991 |
| khv (Khabarovsk) | American | 1948 | 1991 |
| khv (Khabarovsk) | European | 1948 | 1991 |
| tas (Tashkent) | American | 1948 | 1991 |
| tas (Tashkent) | European | 1948 | 1991 |
| pek (Beijing) | American | 1949 | 1972 |
| pek (Beijing) | Soviet | 1960 | 1989 |
| pvg (Shanghai) | American | 1949 | 1972 |
| pvg (Shanghai) | Soviet | 1960 | 1989 |
| hav (Havana) | American | 1960 | null |
| thr (Tehran) | American | 1979 | null |

## Testing plan

1. `TradeRestriction.IsRestricted` — under/over each window's boundary
   years for every row above; `homeCity: null` always returns false; an
   aircraft with `Bloc: null` always returns false regardless of city.
2. `QuarterEngine` — an `AircraftOrder`/`LeaseAircraftOrder` for an
   American type rejected for a Beijing `homeCity` in 1965, accepted
   again in 1973; the same order for a European type (e.g. Caravelle)
   *accepted* for Beijing throughout 1960–1972, never rejected; the same
   order for a Soviet type accepted for Beijing in 1958, rejected in
   1965; confirms `RouteOrder`/`AdjustRouteOrder` stay unaffected (an
   already-owned embargoed-bloc aircraft keeps flying).
3. **The headline regression guard**: a real sweep confirming a Beijing-
   home-city airline's set of purchasable-or-leasable types genuinely
   *narrows* through 1960–1972 (American and Soviet types absent,
   European types like Comet/Viscount/Caravelle still present) and
   widens again after 1972/1989 — the corrected finding this whole plan
   exists to make real, locked in as a test rather than left as a
   one-off manual check. Explicitly assert the count never actually
   reaches zero at any point in the window, since that was this doc's
   own original (wrong) claim.
4. Full coverage check on every changed/new file, per CLAUDE.md.
