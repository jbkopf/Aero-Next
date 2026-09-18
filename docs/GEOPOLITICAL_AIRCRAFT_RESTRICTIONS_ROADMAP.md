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
| Cold War Western embargo on the Soviet Bloc (COCOM) | Soviet-aligned cities ↔ Western aircraft | 1948 | 1991 |
| Sino-Soviet split | China ↔ Soviet aircraft/cooperation | 1960 (Soviet advisors withdrawn, Jul 1960) | 1989 |
| US trade embargo on the PRC | China ↔ Western aircraft | 1949 | 1972 (Boeing 707 contract, Sep 1972) |
| US embargo on Cuba | Havana ↔ Western aircraft | 1960 | never lifted (open-ended through the campaign's 2015 end) |
| US sanctions on Iran | Tehran ↔ Western aircraft | 1979 | never lifted through 2015 |

The China rows are the headline case: 1960–1972 is a genuine
double-embargo window — cut off from Soviet supply by the Sino-Soviet
split *and* from Western supply by the US embargo, simultaneously. Cross-
referencing against `data/aircraft.json`'s real availability windows, a
Beijing-based airline (EastAsia's own AI-default home city, per
`GameSetup.CreateSeats`'s highest-`DemandWeight`-non-gateway rule) would
have **no aircraft type in the catalog it could purchase or lease for
roughly 48 quarters straight (1960–1972)** — every Western type is
blocked by the US embargo, and the only Soviet types in that window
(Il-14, Tu-104) had *already stopped production* by 1960 before the split
even hit, with the next Soviet type (Il-62) not arriving until 1967 and
blocked anyway. Moscow, Havana, and Tehran, by contrast, never hit a full
zero — the USSR had a continuous domestic industry, and Cuba/Iran each
kept a real fallback supplier for the whole window.

## Data model

Two new, independent, nullable-permissive-default fields — same
convention every optional field in this codebase already follows — plus
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
public enum PoliticalBloc { Western, Soviet }
```

**`AircraftType`** gains one new field:

```csharp
PoliticalBloc? Bloc = null   // null = not modeled / no restriction ever applies to this type
```

Tagged `Western` for every NorthAmerica- or Europe-manufactured type
(Boeing, Douglas, Lockheed, Convair, McDonnell Douglas, Bombardier, de
Havilland, Vickers, Sud Aviation, BAC, Aerospatiale/BAC, Airbus, ATR) and
`Soviet` for Ilyushin/Tupolev/Sukhoi. Embraer (South America) and COMAC
(modern China, 2008+) stay `null` — neither bloc is embargoed in this
scope, so there's nothing to tag them against.

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
whatever it already owns; it just can't grow or replace during the
embargo, exactly like the real CAAC.

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
a Beijing-based AI airline would keep proposing (and having rejected)
`AircraftOrder`/`LeaseAircraftOrder` for embargoed types every quarter for
12 years straight — correct outcome, wasted actions. Worth noting: this
exact "old, pre-existing gap" already exists today in a smaller form —
`EraAvailableTypes` is filtered by `Timeline.IsAvailable` (the floor-only
*flying* check), not `IsAvailableForPurchase`, so the AI can already
propose an order for a type that's flyable-but-past-its-own-
`EndOfManufactureYear`; `QuarterEngine` is already the real backstop for
that case too. Fixing both at once (thread `year`/`homeCity` all the way
through `EraAvailableTypes`'s own computation) is probably the right
scope for that follow-up phase, not just the new bloc check in isolation.

## Open questions

None blocking a first pass, but three worth a direct answer before
building:

1. **Is a genuine 12-year "nothing to buy or lease" wall good design, or
   does it need a release valve?** CLAUDE.md's own "losers must always
   get partial payoff" pillar is about contested actions, not quite this
   shape, but the spirit — never leave a mechanic as a pure dead zone
   with no player agency — is the live question here. Mitigating factor:
   an embargoed airline keeps every *other* action (negotiate landing
   rights, marketing, fares, hiring, retiring, flying its existing
   fleet) — it's specifically the acquisition actions that go dry, not
   the whole turn. Real CAAC history says the hard wall is faithful; it's
   an open question whether faithful is fun here.
2. **Scope of the initial `PoliticalBloc` tagging and restriction list.**
   The doc proposes tagging every catalog aircraft's bloc and authoring
   12 initial `TradeRestriction` rows (below) in one pass, mirroring how
   `EndOfManufactureYear` was researched for the whole catalog at once
   rather than piecemeal. Confirm that's the right size for a first PR
   versus starting with just the China case that actually produces a
   zero-aircraft quarter.
3. **Human-chosen home cities.** A human player can start anywhere in a
   region's non-gateway city list — e.g. Taipei instead of Beijing in
   EastAsia, which correctly avoids the embargo entirely (accurate:
   Taiwan was never embargoed). Confirm the briefing/UI surfaces *why* a
   player's own aircraft catalog just went from 15 types to zero, rather
   than it reading as a silent bug — this is exactly the kind of thing
   CLAUDE.md's "legibility over simulation depth" pillar exists to
   protect against.

## Proposed initial data (not yet authored into `data/`)

**`AircraftType.Bloc` tagging:** `Western` — dc3, l049, dc6, cv340, dc7,
707, dc8, 727, dc9, dc10, l1011, md80, crj100, 767, 757, 737, 747, 777,
787 (Boeing/Douglas/Lockheed/Convair/McDonnell Douglas/Bombardier);
comet, viscount, caravelle, bac111, a300, concorde, a340, atr72, a320,
a330, a380, a350 (de Havilland/Vickers/Sud Aviation/BAC/Aerospatiale-BAC/
Airbus/ATR). `Soviet` — il14, tu104, il62, tu154, il86, ssj100. `null`
(unmodeled) — emb120, e190 (Embraer/South America), arj21 (COMAC/modern
China).

**`data/trade-restrictions.json`** (12 rows):

| CityId | RestrictedBloc | StartYear | EndYear |
|---|---|---|---|
| mow (Moscow) | Western | 1948 | 1991 |
| kie (Kiev) | Western | 1948 | 1991 |
| msq (Minsk) | Western | 1948 | 1991 |
| rov (Rostov) | Western | 1948 | 1991 |
| khv (Khabarovsk) | Western | 1948 | 1991 |
| tas (Tashkent) | Western | 1948 | 1991 |
| pek (Beijing) | Western | 1949 | 1972 |
| pek (Beijing) | Soviet | 1960 | 1989 |
| pvg (Shanghai) | Western | 1949 | 1972 |
| pvg (Shanghai) | Soviet | 1960 | 1989 |
| hav (Havana) | Western | 1960 | null |
| thr (Tehran) | Western | 1979 | null |

## Testing plan

1. `TradeRestriction.IsRestricted` — under/over each window's boundary
   years for every row above; `homeCity: null` always returns false;
   an aircraft with `Bloc: null` always returns false regardless of city.
2. `QuarterEngine` — an `AircraftOrder`/`LeaseAircraftOrder` for a
   Western type rejected for a Beijing `homeCity` in 1965, accepted again
   in 1973; the same order for a Soviet type accepted for Beijing in
   1958, rejected in 1965; confirms `RouteOrder`/`AdjustRouteOrder` stay
   unaffected (an already-owned embargoed-bloc aircraft keeps flying).
3. **The headline regression guard**: a real sweep confirming a Beijing-
   home-city airline has *zero* purchasable-or-leasable types for the
   full 1960–1972 window and a non-zero count immediately before and
   after — the exact finding this whole plan exists to make real, locked
   in as a test rather than left as a one-off manual check.
4. Full coverage check on every changed/new file, per CLAUDE.md.
