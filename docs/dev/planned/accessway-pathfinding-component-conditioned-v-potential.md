# Component-Conditioned V Commitment Potential

Status: proposed refinement for later grilling

Drafted: 2026-07-29

Related designs:

* [V2 Implementation Plan](../done/accessway-v2-implementation-plan.md)
* [Terrain-Extrema Landscaping Heuristic](accessway-pathfinding-terrain-extrema-landscaping-heuristic.md)
* [V2 Jagged Fringes](accessway-v2-jagged-fringes.md)

## Purpose and status

Replace the current canonical-center V2 potential with a mixed scalar
travel-plus-overhead potential over physical ground, fixed navigation, and
actual 4x4 V origins. Extend that global potential with sparse,
component-conditioned fields for V routes launched from non-goal G components.

This is a route-potential refinement, not part of the terrain-extrema
landscaping heuristic. The route potential owns travel, generated-origin fixed
overhead, handoff costs, fixed-terminal fees, and the grounded suffix. The
terrain-extrema heuristic independently owns unpaid direct and exterior-ray
landscaping work. If both are enabled, their proposed composition remains:

```text
h = Hroute + Hland
```

where `Hroute` is either the global field `P` or a component-conditioned field
`S_C`.

The design is intentionally not yet approved for implementation. Its
dominance, state-identity, coverage, and cost-ownership claims require a
dedicated grilling session and fixtures against reference Dijkstra.

## Motivation

The current V potential spreads the minimum V rate `1 + F/4` over physical
cardinal distance. That is a useful lower bound, but it does not directly model
the mixed topology through which a relaxed route may alternate between:

* strict-diagonal mask-aware physical G;
* relaxed projected fixed navigation;
* generated 4x4 V origins; and
* disconnected G components that provide zero-overhead travel shortcuts
  between later V launches.

A mixed field can represent those choices directly. However, a global mixed
field evaluated at a V state may immediately return to the same G component
from which that V route was launched. Such a return can make an unnecessarily
early V launch appear to have no unpaid V commitment. The proposed
component-conditioned extension retains the launch component and prices the
V continuation required to merge into the globally useful V part of the mixed
field.

## Terminology

### Global mixed potential `P`

The request-scoped reverse shortest-path field over relaxed G, fixed, and V
navigation. It lower-bounds the complete remaining route cost represented by
that graph.

### Potential owner

The identity carried by a V search label that selects which route-potential
field applies. The owner is either global `P` or a particular source G
component `C`.

### P-owned V region

The V origins at which a Bellman-minimal continuation in `P` uses generated V
travel, together with compatible fixed targets and useful exits that safely
merge a component-conditioned route into `P`. Every equal-cost Bellman V
alternative belongs to this region; it must not depend on one arbitrary
shortest-path-tree parent.

### Component-conditioned V field `S_C`

The replacement route potential for V labels launched from G component `C`
before they reach the P-owned V region. It inherits complete remaining costs
from `P` at a component-specific merge fringe and extends those costs backward
over generated V origins.

### V shadow of `C`

The sparse set of generated origins for which `S_C` is relevant. It is a
potential-ownership domain, not necessarily the literal physical footprint of
the G component.

## Global mixed potential

Construct `P` by reverse shortest path from all compatible targets. The
proposed relaxed layers and costs are:

| Layer or edge | Proposed relaxed cost |
|---|---:|
| Goal G or matching fixed goal | `0` |
| Physical G cardinal travel | `1` |
| Physical G strict diagonal travel | `sqrt(2)` |
| Projected fixed cardinal travel | `1` |
| Projected fixed relaxed diagonal travel | `sqrt(2)` |
| Generated V cardinal origin step | `4 + F` |
| V/G or fixed terminal | authoritative relaxed handoff or terminal cost |

Here `F` is the fixed overhead for one generated origin. A V field node
represents an actual 4x4 origin rather than an arbitrary physical tile.
Cardinal generated propagation deliberately relaxes V2 band, orientation,
profile, and history constraints. In particular, a real straight may introduce
two origins while the scalar relaxation pretends that one `4 + F` step is
sufficient. Such extra relaxed choices can weaken the result but must not make
it exceed a real continuation.

The field must retain the current search's paid/unpaid convention. A generated
origin already present in the current V state was charged in `g`; a reverse
step charges `4 + F` for the next unpaid origin. A direct terminal from the
current origin charges only its still-unpaid handoff or terminal edge.

The exact handling of G-to-V entry, V-to-G exit, center spokes, V2 transition
bands, and fixed-provider terminal fees remains to be reconciled against the
authoritative edge-cost breakdown. No cost may be omitted from both `g` and the
model, and no already-paid cost may be charged again in `h`.

## Component-conditioned replacement fields

For each relevant non-goal G component `C`, derive a merge fringe `B_C` at
which a V route launched from `C` may safely hand ownership to `P`.

The intended scalar definition is:

```text
S_C(o)
    = min over q in B_C of
        VCost(o, q) + PBoundaryCost_C(q)

VCost(o, q)
    = minimum relaxed cardinal V-origin cost from o to q
    = originSteps(o, q) * (4 + F)
```

`PBoundaryCost_C(q)` is normally `P(q)`. For an immediate useful handoff whose
global `P(q)` would instead return to `C`, the boundary seed must use the
handoff edge plus the downstream `P` value explicitly rather than inheriting
the cheaper return-to-`C` value.

Because boundary seeds contain arbitrary octile, handoff, and terminal values,
constructing `S_C` is a multi-source weighted flood/Dijkstra operation even
though every V fill step has the same `4 + F` increment.

`S_C` replaces `P`; it is not added to it:

```text
Hroute(state) =
    P(state)       when the state has global ownership
    S_C(state)     when the state is V-committed from component C
```

This replacement avoids double-counting travel or overhead already contained
in the inherited `P` boundary value.

## Proposed merge-fringe contents

The conservative merge fringe for component `C` is expected to include:

* every V origin where a Bellman-minimal continuation in `P` uses another V
  origin;
* every equal-cost tie in which generated V is one minimal continuation;
* compatible fixed targets;
* a V origin with a useful handoff into a G component other than `C`; and
* a return to `C` only after `P` has represented the useful V portion of that
  shortcut.

The last item is the central unresolved dominance claim. The proposal assumes
that a useful same-component V shortcut is already represented by `P`: if
paying `4 + F` to cross a barrier or bypass a circuitous G route is favorable,
the mixed Bellman field uses V for that portion. A V launch before that
P-owned portion is therefore committed to reaching its nearest admissible
merge fringe rather than immediately returning to `C`.

This claim must be tested against cleanup costs, projected-history ground
validation, multiple handoff geometries, fixed transition adapters, profile
requirements, and ties. If an omitted or history-qualified condition can make
a same-component V shortcut useful when static `P` prefers G, the affected
origin must become an additional merge seed or `S_C` must weaken to `P`.

## Search-state ownership

A V label needs enough identity to select its route field:

```text
PotentialOwner =
    GlobalP
    SourceGroundComponent(C)
```

The proposed ownership transitions are:

```text
G in C -> generated V
    owner = C

V owned by C -> another shadow V origin
    owner remains C

V owned by C -> B_C
    owner becomes GlobalP

V owned by C -> useful handoff into different G component D
    leave V; a later G-to-V launch receives owner D

fixed/source V that has no source G commitment
    owner = GlobalP
```

If ownership changes which V-to-G returns are admitted or dominated, it is part
of the search-state semantics and likely must participate in the V label key.
The same geometry reached from different components can have different
component-conditioned costs and legal commitment exits.

An origin may overlap or hand off to more than one G component. Consequently,
field ownership is not a single global partition of physical origins. The same
origin may have `P(o)`, `S_C1(o)`, and `S_C2(o)` values; the label owner selects
the applicable value.

## Dominance and admissibility obligation

The simplest conventional proof treats a component-owned V label as a state in
a transformed graph. A premature V-to-`C` return is unavailable until the
label reaches `B_C`; all other relaxed continuations remain available. Every
surviving continuation must therefore:

1. traverse a relaxed V prefix to some merge origin `q`;
2. pay at least the `4 + F` fill cost accumulated by `S_C`; and
3. pay at least the inherited `PBoundaryCost_C(q)` afterward.

Under those graph semantics, `S_C` is a lower bound by construction.

It is not enough merely to give a normal V label a larger heuristic while
leaving an immediate return to `C` available. Such a continuation could cost
less than `S_C`, making the field non-admissible as an ordinary pointwise
heuristic. The later grilling must settle whether premature returns are:

* removed by a complete dominance proof;
* represented as distinct committed/uncommitted states;
* retained with a weaker field; or
* used only as a non-optimal experimental ordering policy.

The reference Dijkstra configuration must use the same authoritative graph
pruning and ownership semantics when validating A*.

## Relationship to the terrain-extrema heuristic

This proposal does not move generated overhead into or out of the
terrain-extrema calculation. Generated fixed overhead already belongs to the
route potential, while the extrema heuristic deliberately excludes it.

The proposed disjoint composition is:

```text
h = Hroute + Hland
```

`Hroute` owns:

* G, fixed, and V traversal;
* generated-origin fixed overhead;
* handoff and center-spoke costs;
* fixed-terminal fees; and
* the exact or relaxed grounded suffix.

`Hland` owns only unpaid:

* direct generated-origin terrain work; and
* ordinary exterior-side-ray terrain work within its proven charge horizon.

Potential ownership may later refine which terrain contacts are legal
terminals for the extrema charge horizon. That is an integration point, not a
reason to make `S_C` part of the extrema heuristic.

## Construction and caching sketch

1. Build the immutable projected-ground components for the request.
2. Build `P` once over the mixed relaxed graph.
3. Identify all Bellman V ties and conservative useful exits.
4. Build `S_C` lazily only for components from which search actually launches
   generated V.
5. Restrict each `S_C` to its sparse V shadow and discard it with the request
   snapshot.
6. On missing coverage, ambiguous ownership, or failed invariants, weaken the
   affected lookup to `P` rather than inventing a penalty.

The complete artifact key must include every request setting that changes G
masks, projected fixed navigation, V-origin eligibility, `F`, handoff costs,
terminal costs, or bounds.

## Diagnostics proposed for the experiment

Record:

* `P` nodes by G, fixed, and generated-V layer;
* `P` build time, queue pressure, and Bellman V-tie count;
* `S_C` fields requested, built, reused, and weakened to `P`;
* merge-fringe and V-shadow sizes per component;
* states evaluated under `P` versus `S_C`;
* ownership transitions from `C` to `P`;
* premature same-component returns rejected or weakened;
* origins carrying values for multiple components;
* average and maximum `S_C - P` difference;
* A* visited states, pending high-water, and total time; and
* A*/Dijkstra success, selected cost, route, and cost-breakdown agreement.

## Required fixtures and open grilling questions

At minimum, cover:

* open flat goal-connected G where an early V launch is pointless;
* a disconnected non-goal G island requiring two V segments;
* a U- or horseshoe-shaped single G component where V is a real shortcut;
* a cheap V bridge over an unpathable strip;
* a gradual ramp that may need to begin over otherwise navigable G;
* cleanup-bearing G whose true route cost differs from the relaxed G mask;
* projected-history validation that changes later G availability;
* origins touching two G components;
* tied G and V Bellman continuations;
* fixed goals and projected fixed-origin octagonal travel;
* transition adapters and V-prime fixed navigation;
* immediate terminal handoffs and paid-current-origin indexing;
* V2 straights introducing two origins versus the one-origin relaxation;
* requests with no component-conditioned merge fringe; and
* exact A*/Dijkstra comparison with `S_C` enabled and disabled.

Questions reserved for the grilling session:

1. What exact graph fact proves that every useful same-`C` shortcut appears in
   the P-owned V region?
2. Which cleanup and history effects can invalidate that proof?
3. What is the minimal safe definition of `B_C`?
4. Must `PotentialOwner` participate in label dominance, or can ownership be
   derived without losing a viable route?
5. How should a two-lane V2 band query one-origin `P` and `S_C` values?
6. Which handoff and spoke costs belong on directed G-to-V versus V-to-G edges?
7. Can component fields be bounded tightly enough to improve total runtime?
8. Does the stronger field still help after adding the terrain-extrema
   landscaping heuristic?

No implementation should begin until these questions have been grilled,
the transformed-graph admissibility argument is accepted, and the expected
field-build cost is compared with the queue work it is intended to remove.
