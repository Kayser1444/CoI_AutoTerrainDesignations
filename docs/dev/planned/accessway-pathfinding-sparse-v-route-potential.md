# Sparse V-Type Route Potential

Status: approved first implementation. The stronger component-conditioned
commitment idea is explicitly deferred.

Drafted: 2026-07-29

Architecture note (2026-07-31): V2 has no fixed-terminal fees. Exact
goal-connected projected-G/FV suffix distance is the grounded route-cost term.

Related designs:

* [V2 Implementation Plan](../done/accessway-v2-implementation-plan.md)
* [Terrain-Extrema Landscaping Heuristic](accessway-pathfinding-terrain-extrema-landscaping-heuristic.md)
* [V2 Jagged Fringes](accessway-v2-jagged-fringes.md)
* [Deferred Component-Conditioned V Commitment](accessway-pathfinding-component-conditioned-v-commitment.md)

## Purpose

Replace the current dense canonical-center V2 potential with a sparse scalar
travel-plus-overhead potential `P` over reusable FV and actual 4x4 V origins.
Goal-connected physical G remains an exact suffix graph; each non-goal G
component gets a lazy component-local escape field that inherits `P` at its
canonical G-to-V contacts.

This is a route-potential refinement, not part of the terrain-extrema
landscaping heuristic. The route potential owns travel, generated-origin fixed
overhead, mandatory center-spoke travel, and grounded suffixes. Terrain extrema
independently owns unpaid direct and exterior-ray landscaping work:

```text
h = Hroute + Hland
```

`Hroute` is initially `P` or a component-local G escape lookup. The deferred
commitment field would change label ownership and requires a separate
transformed-graph dominance proof.

## Sparse potential `P`

Build `P` once per immutable request snapshot by reverse shortest path from
goal-connected FV/G contacts. It is a request-scoped field over reusable FV and
the in-bounds ordinary 4x4 V-origin lattice, rather than a dense physical-G
field.

| Layer or edge | Relaxed cost |
|---|---:|
| Goal-connected FV node | exact projected-G suffix distance |
| FV cardinal / diagonal travel | `4` / `4 sqrt(2)` |
| Generated V step into generated V | `4 + F` cardinal |
| Generated V step into FV | `4` cardinal |
| V/FV contact into goal-connected G | shared minimum center-spoke + exact G suffix |

`F` is the fixed overhead of one generated V origin. A field node represents a
V origin or reusable FV origin, not an arbitrary physical tile. Generated-V
propagation deliberately relaxes V2 band, orientation, profile, history, and
adapter constraints. It has no V-to-V diagonal edge: a useful generated
diagonal must pay a full cardinal-plus-cardinal route. FV uses its natural
diagonal relaxation; exact FV navigation still enforces strict diagonal
clearance.

FV-to-V contacts use cheap fixed-fringe and V-prime catalog membership only.
Profile resolution, corners, clearance, cleanup, handoff geometry, and the
one-adapter budget remain exact-search concerns.

The field uses V2's paid/unpaid convention: an origin already present in a V
state was paid in `g`; a forward edge charges `F` only when entering the next
unpaid generated origin. A V2 band initially queries the minimum value among
its currently paid origin nodes. Stronger paired-band queries need a separate
lane-projection proof.

## Component-local G escape field

Non-goal G components are not global `P` nodes. When the search first reaches
one, construct a component-local reverse escape field using exact static G
movement to canonical G-to-V launch positions. A seed adds the shared minimum
center spoke and adds `F` only if its contact enters generated V rather than
reusable FV.

The field guides G labels only; it does not introduce ownership, alter exact
feasibility, or restrict V-to-G returns. Cache it for the immutable request.
Missing or ambiguous coverage returns zero and records one diagnostic per
search.

## Construction and validation

1. Build immutable projected-ground components for the request.
2. Build `P` once over V origins, reusable FV, and catalogued FV/V contacts.
3. Attach goal-connected G contacts with the common minimum center-spoke and
   their exact suffix values.
4. Build component-local G escape fields lazily when non-goal components are
   encountered.
5. On missing coverage or ambiguous static contact, return zero and record the
   first weakening event for that search.

The artifact key includes all settings affecting masks, projected fixed
navigation, V-origin eligibility, `F`, the shared center-spoke, or bounds.

Record node counts by class, build time, queue pressure, field cache behavior,
zero weakenings, A* queue work, and A*/Dijkstra success, cost, route, and
cost-breakdown agreement. Retain the old dense potential only as a temporary
A/B benchmark, then remove it.

Required fixtures include canonical G-to-V escapes, disconnected islands,
same-component V shortcuts, bridges, ramps over G, cleanup-bearing G,
projected-history changes, tied continuations, FV octagonal navigation,
V-prime adapters, paid-current-origin indexing, V2 two-origin straights,
missing catalog coverage, and exact A*/Dijkstra comparisons with `P` on/off.

## Deferred refinement

[Component-Conditioned V Commitment](accessway-pathfinding-component-conditioned-v-commitment.md)
is deliberately separate. Do not add `PotentialOwner`, restrict V-to-G
returns, or change the exact V2 state graph as part of the first `P`
implementation.
