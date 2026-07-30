# V2 Source Launches and Projected Fixed Ground

**Status: Proposed**

## Motivation

V2 currently requires exposed two-origin fixed frontages for both source starts
and fixed-provider goals. That fails on jagged boundaries and cannot safely
start from a one-origin source. The earlier synthetic-companion implementation
also modeled the missing source lane as a fake strafe, while current code
rejects the one-origin source entirely.

Generated mining bodies add a deeper constraint: their continuous target
surface may contain V-prime corner designations. Those profiles are traversable
when finished. V2 must navigate them freely and may generate them at a fixed
fringe, but must not admit them as propagating route profiles.

The design therefore separates two concerns:

* Existing terrain work is navigation surface. Project its exact finished
  target and navigate it like ground.
* Propagating accessway work remains generated V space, limited to flat and
  axis-aligned ramp profiles.
* One bounded transition adapter may generate canonical V-prime profiles to
  join projected ground to V or another projected surface. It uses one
  longitudinal slice where possible and one complementary two-slice pair
  where a slanted fringe requires it.

This makes jagged and slanted patching and one-origin starts ordinary resolved
boundaries between generated work and projected ground rather than special
frontage geometry.

The architectural decision is recorded in
[`docs/adr/0001-navigate-fixed-work-as-projected-ground.md`](../../adr/0001-navigate-fixed-work-as-projected-ground.md).

## 1. Project Existing Work as Ground

Build one tile-resolution Mega vehicle-center graph over:

* ordinary physical G;
* the finished target surfaces of every reusable fixed terrain designation;
* exact edges between fixed target surfaces; and
* exact edges between fixed target surfaces and physical G.

Fixed navigation is independent of origin-cluster ownership and current
accessibility. It may reuse source work, another origin cluster, or a registered
generated accessway. If the selected grounded route crosses another
inaccessible cluster, that cluster receives an incidental provider connection
that normal reachability can discover on the next pass.

Fixed-profile eligibility is physical rather than a permanent shape whitelist.
Current flat, ramp, and V-prime corner profiles are examples. A future existing
saddle may also be reusable if its finished geometry is representable and
passes the exact vehicle test. Generation remains a separate closed set:
propagating V may use flat and axis-ramp profiles, and a bounded transition
band may additionally use canonical single-corner V-prime profiles. Saddles
and arbitrary four-corner profiles remain non-generatable.

At every projected center and real edge, validate:

* the full Mega pathability mask;
* cardinal or diagonal swept clearance;
* finished-surface steepness;
* projected operation workability;
* buildings and persistent props;
* cleanup that the existing designation will actually perform;
* conflicting target surfaces; and
* physical map bounds.

Conflicting fixed targets are invalid surface; they are not averaged or merged.
Navigation pays physical cardinal or diagonal travel but no generated-work or
generated-origin cost.

## 2. Unify Ground and Fixed Providers

The projected fixed surface and physical G belong to one exact navigation
graph:

* tower-reachable physical G nodes are the goals;
* fixed surfaces connected to them belong to the same goal-connected
  component;
* disconnected fixed clusters are ordinary disconnected projected-ground
  components; and
* search may leave or re-enter any component through validated G/V
  transitions.

This replaces:

* fixed-provider frontage discovery;
* both source-side and goal-side `IsExposed`;
* `AccessV2FixedFrontage` terminal matching; and
* the optimistic `AccessV2ProviderDistanceField` as a terminal authority.

Reverse distances in the exact projected-ground graph supply real downstream
travel costs. No exposed-pair terminal fee is needed.

### Fixed-navigation V space

Represent the fixed-target portion of the exact graph in directionless **FV
navigation space** rather than expanding every physical vehicle-center tile.
An FV node is a compatible width-two fixed-navigation band; it does not inherit
generated-V profile propagation, entry-heading, predecessor, strafe, turn, or
generatable-profile restrictions.

An exact cardinal FV move advances one origin and costs `4`. An exact diagonal
FV move advances one origin on both axes and costs `4 * sqrt(2)`. Diagonal
travel is strict: both corresponding cardinal swept corridors must be legal.
Every FV edge must prove the complete intermediate Mega vehicle-center path,
including projected height, steepness, obstacle, cleanup, and history-dependent
clearance. Replay expands the edge back into that exact center path.

Source centers, physical-G contacts, transition-adapter contacts, cleanup changes,
and irregular fringe contacts are explicit FV portals. The FV representation
must preserve the connectivity, travel cost, cleanup ownership, and available
crossings of the clearance-exact vehicle-center graph; it is not optimistic
fixed-origin adjacency.

The one-adapter transition limit constrains generated designation origins, not
the number of vehicle-center steps between an FV canonical center and the
contact. The projected Mega footprint extends beyond an origin edge. Exact
portal probing may therefore retain up to eight straight or diagonal center
steps: one four-tile origin band plus the fixed-side footprint offset. It stops
at the first non-projected or cleanup center and never flood-fills the fixed
body.

A free fixed-to-ground edge is legal only for pre-existing fixed work. Any edge
from a generated-owned target surface into physical G must still use the
authoritative generated V/G seam evaluator so operation selection, workability,
cleanup, cost, replay, and materialization metadata remain intact.

## 3. Recognize Planned Provider Chains

A zero-generated route through unfinished fixed work is not a failure.

For example:

```text
tower G -- [A A] -- [B B] -- [C C]
                                  source
```

If the projected targets of A, B, and C form a clearance-exact route, no
parallel accessway is needed. The cluster is:

* immediately accessible when live Mega reachability already passes; or
* `WaitingForProviderCompletion` while the fixed chain works progressively
  from the ground side.

The current zero-work rule requiring every source origin to be live-pathable
must not reject this planned chain.

## 4. Select Source-Center Distance Tiers

Reuse V1's arithmetic cluster center and Manhattan center-distance score, but
do not apply V1's coordinate tie-break to choose one origin.

Group source origins into distance tiers:

1. The first tier contains every origin tied as closest to the arithmetic
   center.
2. Later tiers contain successively less-central origins.
3. All roots in one tier have equal zero initial traversal cost.
4. Search one tier at a time.
5. Advance to the next tier only after a structural failure such as no feasible
   launch or an exhausted `NoPath`.
6. Do not advance after `VisitedLimit`, `CostLimit`, cancellation, or another
   resource failure, because those outcomes do not prove the tier lacks a
   route.
7. Skip a backup tier unless it contributes at least one launch search state
   that no earlier tier explored. Novelty uses the authoritative V2 label
   identity rather than the displayed coordinate, so a distinct axis, band
   profile, projected-navigation portal, or adapter role remains a valid
   fallback.

Centrality is lexicographic: any successful route from a more-central tier wins
even if a later tier could be cheaper. The tier fallback avoids making an
unusable centroid a permanent failure without returning to zero-cost
perimeter-wide start discovery. Exhausting a tier proves that re-seeding an
already-explored launch state would only repeat its reachable search component;
such a seed does not justify replaying the tier.

## 5. Replace Synthetic Starts with a V2 Source Launch

A Mega source cannot start from a two-origin strip alone. It needs a complete
two-slice, 2x2-origin launch footprint:

```text
initial source slice  ->  longitudinal successor slice
[lane 0]                  [lane 0]
[lane 1]                  [lane 1]
```

The initial slice:

* contains at least one source obligation;
* may reuse compatible fixed work in either lane;
* may generate missing V companions;
* pays generated work, fixed overhead, cleanup, disturbance, height-envelope,
  and ray cost for generated origins only; and
* pays no traversal because it has not advanced longitudinally.

The successor slice:

* is a legal longitudinal continuation;
* may also mix compatible fixed targets with generated V profiles;
* pays the normal four-tile traversal cost; and
* completes the first Mega footprint.

Do not allow a turn, strafe, G transition, or terminal success until the launch
is complete.

For each source root, enumerate:

* every spatial 2x2 support containing the root;
* both choices of longitudinal orientation within that support;
* every eligible Mega center;
* every compatible fixed-target reuse;
* every legal generated V companion and successor profile;
* one bounded V-prime transition adapter where the source fringe permits it;
  and
* both travel directions where distinct.

Deduplicate identical resolved launch plans, but retain distinct pathable
centers and outgoing states because their downstream travel can differ. Two
continuations opened by the same companion or corner profile are equal
candidates rather than straight-versus-strafe alternatives. Let ordinary route
cost choose among them.

### One-origin example

For an isolated flat source one level above flat G, the cheapest launch may be:

```text
[flat fixed source]  ->  [down ramp]
[flat companion]         [down ramp]  -> G
```

The companion is generated beside the source and the two down-ramps form the
perpendicular successor and immediate handoff. This is not a flat 2x2 fill and
must emerge from profile enumeration and costing.

The handoff seam begins at the ramp face, but its G entry is the first captured
Mega-pathable center reached outward from that face. Any intervening centers
remain part of the proven handoff spoke and pay ordinary cardinal travel cost;
the search must not require an extra V band merely to move the entry center
clear of the ramp footprint.

An exact-terrain generated companion remains an explicit designation. It may
have zero direct-work cost, but it still pays generated-origin overhead and
creates a persistent, tracked corridor.

### Launch result representation

* A fully fixed launch enters the projected fixed-ground graph directly.
* A partly generated launch whose successor is an enabled V band queues the
  ordinary V frontier with its complete generated history.
* A physically valid mixed launch that cannot form an enabled uniform V band
  may queue a projected-ground start carrying its generated history.
* A source-side transition adapter uses the same resolver, candidate rules, exact
  crossing, ownership, and cost as every other projected-ground boundary.
* A V-prime transition adapter supplies physical launch geometry but never counts
  as a route-profile predecessor for a later strafe or turn.
* Generated origins always remain owned, replayed, and materialized. Projected
  navigation never reclassifies them as fixed work.
* Leaving any generated-owned surface for physical G still requires the
  generated V/G seam proof.

This replaces the fake initial `Strafe`, the first-state replay special case,
and `StartSourceMegaPairMissing` as a structural rejection.

## 6. Resolve Fixed Fringes with One Bounded Transition Adapter

Search may alternate among generated V, projected fixed ground, and physical G
within one route. At each projected-ground boundary, one width-two transition
adapter may resolve the surface mismatch without becoming a propagating V
state. A jagged repair normally occupies one longitudinal slice. A slanted
repair may occupy one complementary pair of consecutive slices whose far face
is an ordinary level or ramp face. No adapter may grow to a third slice.

Each transition lane may:

* reuse a compatible fixed target;
* generate an ordinary flat or axis-ramp V profile; or
* generate a canonical V-prime profile with exactly one corner offset by
  `+1` or `-1`.

Saddles, arbitrary four-corner interpolation, and larger corner offsets remain
non-generatable. Every first-slice V-prime origin must independently belong to
the candidate catalog described below. A first V-prime slice may be followed
only by its complementary second adapter slice when that slice exposes an
ordinary continuation. Neither slice may satisfy a V predecessor requirement,
seed unrelated V-prime work, or begin another transition adapter during the
same crossing. After an accepted route changes the fixed snapshot, its former
transition work is ordinary fixed context and may seed a later route.

The other side of a transition adapter may be:

* an ordinary propagating V band;
* another projected fixed-ground surface, including another node in the same
  component; or
* physical G through the authoritative generated-to-ground seam.

A route may use several transition adapters at distinct crossings. Direct
projected-to-projected bridges are legal and may repair a one-band gap without
forcing an artificial V segment.

### Directionless resolution

Projected navigation supplies no entry heading. Resolve every compatible
outgoing orientation, travel direction, Mega center, and ordinary V band as an
equal candidate. A companion or V-prime origin that opens two continuations
does not make one a straight and the other a strafe or turn.

Deduplicate only candidates with both identical resolved adapter geometry and
an identical resulting search state. Retain different centers and orientations
even when their profiles and initial costs match. Normal cheapest-label
dominance applies after candidate generation, with a canonical
geometry-based tie-break for equal-cost arrivals.

### Cheap candidate catalog and lazy profile resolution

Build a request-snapshot catalog of **V-prime candidate origins**. An origin is
in the catalog when:

* it is not itself fixed;
* it has at least one and no more than three cardinally adjacent
  non-conflicting fixed terrain-designation targets; and
* it lies within the ordinary request/map bounds.

Every existing fixed target may seed the catalog regardless of cluster
ownership, completion, or current Mega connectivity. This includes an isolated
source origin and registered accessway work. Physical G and diagonal-only
contacts do not seed it. Four fixed neighbours describe an enclosed hole rather
than a fringe.

The catalog is only a conservative spatial trigger. Do not precompute a global
catalog of transition-profile combinations. When search reaches a nearby V
frontier or projected boundary center:

1. perform a cheap local catalog lookup;
2. load fixed edge and diagonal shared-corner constraints;
3. enumerate only canonical V/V-prime profiles satisfying those constraints;
4. combine compatible profiles across the two lanes and chosen continuation;
   and
5. run the authoritative history-dependent work and exact crossing evaluation.

Diagonal fixed origins can eliminate every V-prime profile through their shared
corner height even though they do not create or remove catalog entries.
Generated route history supplies the same mandatory shared-corner constraint
and never seeds new candidates. Cache only snapshot-static templates and
contact geometry; ownership, revisits, cleanup, rays, and cost remain
per-label evaluations.

Rebuild the catalog with the projected snapshot after each cluster is
provisioned. Newly accepted accessway work may therefore support later clusters
in the greedy outward pass.

### Exact crossing, work, and ownership

No frontage-shape predicate authorizes a transition. After overlaying the
resolved candidate:

* prove an exact Mega vehicle-center path through the complete transition
  footprint;
* prove every projected, V, or physical-G edge selected by the route;
* enforce swept cardinal or diagonal clearance and real steepness; and
* charge exact center travel at `1` per cardinal tile or `sqrt(2)` per diagonal
  tile.

Transition travel has no categorical straight, strafe, turn, band, or spoke
fee. Generated work is additive and separate. Charge each newly planned origin
once for direct work, fixed overhead, cleanup, and exposed-perimeter rays.
Derive exterior rays from the resolved footprint: compatible fixed context,
generated history, the other transition lane, and the selected continuation
are interior; every remaining generated-origin edge is exterior.

Every generated transition origin is accessway-owned and must survive history,
replay, and materialization. This includes an origin whose target already
matches current terrain: it remains an explicit designation so adjacent work
that later disturbs it is restored. Replace the current exact-terrain omission
fixture. If the game cannot retain such a fulfilled no-op designation, reject
the candidate with a specific diagnostic rather than silently omitting it or
inventing terrain work.

All ordinary generated-work constraints remain authoritative:

* tower area, horizontal bounds, and useful-height envelope;
* prospective workability and operation compatibility;
* ocean, building, durability, prop, cleanup, and disturbance rules;
* fight invariants against every cardinal and diagonal shared corner;
* history ownership and no revisits;
* exterior ray envelopes; and
* transactional replay, placement, rollback, and provider validation.

## 7. Cost and Multi-Cluster Policy

Route cost remains additive:

```text
complete grounded driving distance
+ generated direct work
+ generated-origin fixed overhead
+ new cleanup and exterior-ray costs
```

Fixed target navigation pays travel only because its work is already scheduled.
Driving distance is not a hard priority: enough saved construction work may
justify a longer permanent route. The existing landscaping-cost distance scale
remains the player control for that exchange rate.

Continue greedy outward provisioning:

1. Sort inaccessible clusters by the current minimum squared geometric distance
   from any cluster origin to the tower.
2. Add a deterministic coordinate or cluster-identity tie-break.
3. Connect one cluster.
4. Rebuild reachability and the projected-ground snapshot.
5. Let later clusters reuse all established and incidental connections.

This deliberately avoids global joint access-network optimization. Near-to-far
ordering prevents the obvious remote-first detour, while each cluster minimizes
its complete grounded route against infrastructure established so far.

## 8. Search Dominance

Retain the current cheapest-label history policy. The cheapest arrival at a V
band, transition continuation, or projected-ground center owns the generated
geometry, rays, and cleanup history used for later expansion. Equal-cost
arrivals use a canonical geometry-based tie-break rather than enumeration
order.

This is not fully complete: a more expensive arrival with a less restrictive
history might theoretically succeed where the retained arrival fails. A
history-aware Pareto search would materially expand the state space. The
existing visit/cost limits, diagnostics, and higher-level fallback remain the
chosen safety boundary.

## 9. Projected-Ground-Aware Heuristic

The A* lower bound gains a projected-ground relaxation:

* physical and projected fixed terrain propagate cardinally at cost `1`;
* they propagate diagonally at cost `sqrt(2)`;
* every possible V-prime candidate origin provisionally joins that G-like
  relaxation with the same cardinal and diagonal travel;
* transition generation, cleanup, ray, and fixed-overhead cost are omitted;
* heuristic edges require only adjacent in-map relaxation nodes;
* heuristic steepness, full Mega mask, swept-corner clearance, and diagonal
  side-corridor checks may be omitted; and
* the exact graph and replay still enforce every real edge.

Exact FV diagonals therefore remain strict even though the heuristic relaxation
may include a diagonal whose two cardinal corridors are not both available.
That additional heuristic edge can only shorten the estimate.

Ignoring those constraints adds heuristic shortcuts and can only lower the
estimate. The relaxation may even chain adjacent transition candidates that
exact search forbids; encoding the one-band budget in the heuristic is not
required for admissibility. The estimate therefore remains safe even for
invalid player-placed fixed targets or locally incompatible candidate profiles.

Generated V retains its proven minimum generated-travel rate and relaxed seam
costs. Goal-connected projected-ground nodes contribute exact downstream
distance. Disconnected components use a relaxed escape toward generated V.
Generic seam generation overhead weakens to zero wherever a mixed or fully
reused transition can avoid it, while physical travel distance remains in the
field.

Implement Dijkstra as the correctness oracle. Keep stronger A* use gated until
deterministic, randomized, and adversarial fixtures reproduce Dijkstra's route
cost and outcome.

### Terrain-extrema interaction

The terrain-extrema heuristic must use **projected fixed terrain**: overlay
fixed designation targets onto captured physical terrain and regard those
targets as ground. Raw terrain underneath already-scheduled work contributes
no new landscaping charge.

Fixed projected travel and possible transition adapters consume no charge.
Reaching a candidate transition adapter ends the terrain-extrema charge horizon
unless generated landscaping before that contact is independently proven
unavoidable. If the relaxed envelope cannot prove unavoidable work after
projected-ground and transition-candidate closure, the extrema landscaping
component weakens to zero.

## 10. Implementation Sequence

1. Build a conflict-aware projected target-height surface over all fixed work.
2. Extend the Mega G graph into a clearance-exact projected fixed-ground graph,
   including free fixed/physical-ground edges and exact reverse goal distances.
3. Add projected-ground fixtures for V-prime navigation, turns, diagonals,
   disconnected components, and invalid target geometry.
4. Implement source-center distance tiers and deterministic cluster ordering.
5. Replace start frontage discovery with explicit two-slice source-launch
   enumeration and costing.
6. Add launch route-step data so search, replay, and materialization share one
   ownership model.
7. Build the cheap fixed-neighbour V-prime candidate catalog and request-local
   snapshot-static template cache.
8. Add one lazy, directionless transition resolver for source launches, V,
   projected surfaces, and fixed-seeded physical-G handoffs.
9. Extend route data, work evaluation, replay, and materialization with
   canonical transition V-prime profiles and exposed-perimeter rays.
10. Replace exact-terrain omission with explicit accessway-owned designation
    placement and a clear unsupported-retention rejection.
11. Recognize zero-generated projected provider chains as waiting rather than
   failed.
12. Run the unified search under Dijkstra and reconcile every cost and route
   step.
13. Add transition candidates to the projected-ground heuristic relaxation and
    prove A*/Dijkstra equivalence.
14. Remove fixed-frontage discovery, terminal matching, optimistic provider
    terminal fees, `IsExposed`, synthetic-companion-as-strafe handling, and
    obsolete diagnostics.
15. Live-verify one-origin launches, jagged and slanted transition adapters,
    exact-terrain designation retention, corner-rich mining bodies, incidental
    cross-cluster connections, and waiting provider chains behind the existing
    experimental accessway gate.

## 11. Required Fixtures

At minimum, cover:

* fixed flat/ramp/V-prime target surfaces become projected-ground nodes;
* arbitrary fixed-profile eligibility is determined by exact physical
  pathability rather than a V/V-prime whitelist;
* conflicting fixed targets are excluded;
* real projected-ground edges enforce steepness and complete Mega clearance;
* heuristic diagonal and steepness shortcuts never exceed Dijkstra;
* cardinal and diagonal projected-ground costs are `1` and `sqrt(2)`;
* a large corner-rich mining body can navigate from its center to its fringe
  without generating V-prime work inside the body;
* projected navigation may reuse a different origin cluster;
* a reused cluster incidentally becomes connected after normal re-analysis;
* a zero-generated fixed chain returns waiting until live reachability passes;
* every equally central source root is admitted at equal cost;
* deterministic tier and cluster ordering;
* less-central tiers are tried after structural no-path but not resource
  failure;
* a successful central tier beats every less-central tier;
* a one-origin flat source at `+1` can choose a flat companion, two down-ramps,
  and a valid ground handoff;
* a leveling handoff advances outward to captured G without generating a
  terminal flat band;
* exact-terrain launch companions remain explicit generated origins;
* a corner-profile source launch can resolve one mixed V/V-prime transition
  band before entering ordinary V;
* mixed launches enter either an enabled V frontier or projected navigation
  with generated history;
* generated-to-ground edges always replay the generated seam operation;
* catalog membership requires one to three cardinal fixed-target neighbours,
  excludes physical G and diagonal-only contacts, and does not require current
  Mega connectivity;
* four-neighbour holes and non-catalogued companion lanes cannot generate
  V-prime profiles;
* diagonal fixed corners constrain lazy V-prime resolution and reject every
  hourglass mismatch;
* generated history cannot seed another V-prime candidate during the same
  search;
* catalog refresh after an accepted route admits its transition origins as
  fixed seeds for the next cluster;
* ordinary V nodes far from catalog entries perform no V-prime profile
  enumeration;
* snapshot-static template caching never caches route-history authorization or
  cost;
* one generated V-prime lane repairs a jagged fringe;
* two complementary generated V-prime slices repair a slanted fringe and
  expose an ordinary outbound face;
* both `+1` and `-1` canonical corner variants work, while saddles and arbitrary
  corner profiles remain rejected;
* one adapter can expose two equal-cost continuation orientations without
  classifying either as a strafe or turn;
* distinct centers and outgoing states survive geometric deduplication;
* equal-cost arrivals at one state use the canonical geometry tie-break;
* a V-prime transition adapter cannot seed a strafe, turn, third adapter
  slice, separate transition adapter, or unrelated same-route V-prime
  expansion;
* direct projected-to-projected bridges work across different or identical
  projected components;
* a fixed-seeded V-prime adapter may hand off to physical G only through the
  authoritative generated seam;
* transition travel reconciles as exact unit-cardinal and
  square-root-of-two-diagonal center steps without categorical move fees;
* exposed-perimeter transition rays are independent of the route by which
  projected ground reached the adapter;
* jagged boundaries generate only their missing owned transition or V origins;
* fight-invariant, building, durability, disturbance, and ray failures reject
  the generated patch;
* reused fixed work never appears in generated output;
* exact-terrain transition origins remain explicit designations, and an
  unsupported retention path rejects rather than omits them;
* launch search, replay, materialization, cleanup keys, rays, and cost
  reconciliation agree;
* projected-ground heuristic relaxation treats transition candidates as G-like
  cardinal/diagonal travel, may chain them optimistically, and never exceeds
  Dijkstra;
* terrain-extrema charge accounting stops at a possible transition adapter unless
  earlier generated work is proven unavoidable;
* both `IsExposed` failure shapes now route through projected ground;
* fixed-frontage and unified projected-ground routes agree on retained legacy
  fixtures during migration; and
* A* and Dijkstra return the same success/failure and route cost.

## 12. Deferred Work

* Retrofit projected fixed-ground navigation into V1 only after V2 is proven.
* Weight permanent travel by expected mining/filling demand when a defensible
  workload model justifies the added complexity.
* Add the useful-material rebate independently. It must either be represented
  conservatively in the terrain-extrema bound or force that heuristic component
  to zero wherever the rebate may apply.

## Expected Benefits

* Jagged source and provider boundaries need no exposed flat pair.
* Slanted fringes can normalize through at most one bounded two-slice V-prime
  adapter.
* One-origin sources use the normal launch and transition cost model.
* Existing corner-rich mining work becomes naturally navigable, while new
  V-prime work remains confined to fixed-fringe adapters.
* Other clusters and accessways can be reused as real infrastructure.
* Fixed-provider continuation and downstream travel become clearance-exact.
* Zero-work planned chains wait instead of causing duplicate accessways.
* Search, replay, materialization, and heuristics share explicit ownership
  boundaries between fixed projected terrain and newly generated V.
