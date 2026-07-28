# V2 Accessway Pathfinding: Interior Seeds and Jagged Fringes

**Status: Proposed**

## Motivation

V2 currently converts a source cluster into exposed, two-origin-wide start
frontages. This couples two separate questions:

1. Where should the search begin?
2. Where can a width-two route leave the source cluster?

The coupling causes two awkward edge cases:

* A staggered or jagged perimeter may have no flat exposed pair even though a
  legal width-two route could cross the perimeter by adding one missing
  companion designation.
* A one-origin source has no fixed companion lane. Earlier V2 code represented
  the missing lane as a special synthetic start and evaluated it as though it
  were a strafe; current code rejects the source entirely because the source
  cannot itself prove a fixed Mega-width pair.

Both cases are instances of the same problem: a geometric V2 band may contain a
mixture of reusable fixed lanes and lanes that the route must generate.

The proposed change is therefore not just to remove `IsExposed`. V2 should seed
the search inside the source cluster and introduce one shared band-resolution
operation that partitions every geometric band into reusable fixed context and
new generated work.

## Core Model

For a candidate V2 state or transition, distinguish:

* **Introduced origins**: every origin newly covered by the geometric move.
* **Reused fixed origins**: introduced origins whose accepted fixed profile
  exactly matches the candidate profile.
* **Generated delta**: introduced origins that have no reusable fixed profile
  and must be validated, costed, owned by history, replayed, and materialized.

A fixed profile with a different shape is a conflict, not a generated delta.
Fixed profiles outside the explicitly reusable source/provider sets must not be
silently traversed.

This partition is the central abstraction. Straight, strafe, turn, initial
source-band, and eventual fixed-provider entry should all use it rather than
having separate rules for jagged edges or synthetic companions.

## 1. Select an Interior Source Root

Reuse V1's deterministic `SelectStart` rule: choose the source origin nearest
the arithmetic center of the cluster, with the existing coordinate tie-break.
Move that rule to shared endpoint-discovery code rather than duplicating it.

The selected origin is the **source root**. Candidate initial V2 bands:

* contain the source root;
* use every axis enabled by the root profile;
* consider the companion lane on either transverse side; and
* consider both travel directions along the axis.

Do not require an exposed edge. A compatible companion in the source cluster is
reused. An absent companion is a generated lane resolved by the same rules as
any other generated delta.

Initially use one deterministic center root, not every origin in the cluster.
If this proves too restrictive, a later refinement may admit equally central
roots without returning to perimeter-wide multi-source discovery.

## 2. Make the Initial Band a Real Operation

Replace the `AccessV2StartFrontage` synthetic-companion special case with an
explicit initial-band operation. The initial operation has:

* no predecessor state;
* the fixed source root as local immutable context;
* zero traversal cost, because no longitudinal move has occurred;
* generated work, fixed overhead, cleanup, disturbance, useful-height, and
  exterior-ray costs for only its generated delta; and
* the same fight-invariant and feasibility validation as generated origins in
  later transitions.

This should remove the current fake `Strafe` construction in `AddStart` and the
matching first-state special cases in replay. Route data must record the
resolved initial operation explicitly so search, replay, cost reconciliation,
and materialization use the same ownership model.

## 3. Generalize the One-Origin Source

A one-origin cluster naturally produces initial bands in which:

* the source root is reused fixed context; and
* the companion lane is the one-origin generated delta.

The generated companion does not claim that the original cluster was already
Mega-wide. It creates the missing half of a Mega-width working band. The initial
operation must validate the complete two-lane mouth, including the reused
source lane's exterior clearance, before the state is queued.

This preserves the safety concern behind `StartSourceMegaPairMissing` while
removing that rejection as a structural requirement. A one-origin start is
accepted only when the resolved two-lane band is actually feasible.

The same rule also handles a wider cluster whose selected center root has no
compatible transverse source companion. No separate `cluster.Count == 1` path
is needed.

## 4. Traverse Reusable Source Profiles

Ordinary V2 evaluation currently treats an introduced fixed designation as
`ExistingDesignation` and rejects it. Center-outward search therefore requires
transition resolution before generated-work validation:

1. Enumerate the geometric successor and its full introduced strip.
2. Reuse matching profiles belonging to the source cluster.
3. Put only absent origins into the generated delta.
4. Reject mismatching or non-reusable fixed origins.
5. Validate and apply only the generated delta to `AccessV2History`.

Reused source profiles:

* pay no generated-work or generated-fixed cost;
* are not owned, replayed, or rematerialized by the route;
* remain available as immutable geometry and fight-invariant context; and
* do not create a zero-cost teleport: the canonical band-center displacement is
  still charged as traversal cost.

The existing potential field can then guide the search from the center toward
the best exit while physical distance through the cluster remains visible to
the objective.

## 5. Cross and Patch a Jagged Perimeter

No `IsExposed` test is needed for source starts. When a transition crosses the
source perimeter:

* a matching source origin in the introduced strip is reused;
* an empty companion origin becomes generated work; and
* a conflicting fixed origin or infeasible generated profile rejects the move.

The result is emergent patching: the route generates exactly the missing lane
needed to preserve a legal width-two band across the fringe.

All ordinary constraints still apply to the generated lane, including:

* tower-area and horizontal bounds;
* useful-height envelope;
* prospective workability and operation compatibility;
* building, ocean, durability, prop-cleanup, and disturbance rules;
* corner agreement with fixed and generated neighbours;
* history ownership and no-revisit rules; and
* exterior ray envelopes.

V2 still does not gain corner or saddle profiles through this change. If the
missing lane cannot be expressed by an enabled V2 profile, the transition is
rejected.

## 6. Fixed-Provider Goals

Goal-side generalization uses the same band resolver, but it should be
implemented after source-side traversal is proven.

The intended end state is to replace “an exposed fixed pair” with “a resolvable
entry band into an accepted, tower-connected provider.” A goal transition may
reuse matching accepted-provider origins and generate an absent companion lane.
Its downstream terminal fee still comes from the provider-distance field.

Do not remove the fixed-goal `IsExposed` rule merely because source seeding no
longer needs it. Remove it only when:

* provider-side introduced origins can be resolved as reusable context;
* the provider-distance field can assign the resolved band a finite downstream
  cost;
* search and replay agree on the mixed fixed/generated entry; and
* the resulting provider connection still proves Mega-width continuation.

This staging avoids conflating a safe source working band with proof that an
arbitrary fixed network is a safe reusable provider.

## 7. Cost and Heuristic Rules

For every resolved move:

```text
step cost
  = canonical-center traversal distance
  + generated work for generated delta only
  + generated-origin fixed overhead for generated delta only
  + new ray and cleanup costs
```

The initial-band operation omits the traversal term. Reused fixed origins add
no generation cost.

The current relaxed potential remains admissible because interior traversal
still pays physical distance and all omitted generation, cleanup, and ray costs
are nonnegative. Dijkstra fixtures must continue to reproduce A* results after
mixed fixed/generated resolution is introduced.

## Implementation Sequence

1. Extract the deterministic V1 center-origin selector for shared use.
2. Add a resolved-band/transition representation with separate introduced,
   reused-fixed, and generated-delta origins.
3. Add an explicit initial-band operation and route-step representation.
4. Seed V2 from the selected interior root and remove start-side `IsExposed`.
5. Admit matching source-cluster profiles during ordinary V2 transitions.
6. Delete the synthetic-companion-as-strafe path and
   `StartSourceMegaPairMissing` structural rejection.
7. Add source-side fixtures and live diagnostics.
8. Generalize fixed-provider entry and only then remove goal-side `IsExposed`.

## Required Fixtures

At minimum, cover:

* a rectangular cluster starts at its deterministic center and walks through
  fixed source profiles at traversal-only cost;
* a jagged edge with one fixed and one absent introduced lane generates only
  the absent companion;
* a jagged edge whose missing companion violates the fight invariant is
  rejected;
* a one-origin flat source creates a feasible initial band without a fake
  strafe;
* a one-origin ramp source considers only its enabled travel axis;
* a blocked one-origin companion is rejected by the ordinary feasibility or
  clearance reason, not by a missing-pair special rule;
* initial-band search and replay produce identical costs, history, cleanup
  keys, rays, and materialized origins;
* reused fixed origins never appear in generated output;
* interior movement pays canonical-center traversal distance;
* A* and Dijkstra select equal-cost results; and
* until goal-side work lands, buried/non-exposed fixed providers remain
  ineligible terminals.

## Deferred Refinements

Diagonal movement inside a fixed cluster may later improve distance fidelity,
provided both adjacent cardinal bands are resolvable and the swept Mega
clearance is proved. It is not required for jagged-fringe or one-origin-source
support.

## Expected Benefits

* Jagged source edges no longer need a pre-existing flat two-origin frontage.
* One-origin V2 starts become the smallest instance of the normal band model.
* Fixed reuse, generated ownership, costing, replay, and materialization gain
  one explicit boundary.
* Interior travel remains physically costed and heuristic-guided.
* Goal-side jagged entry can adopt the same abstraction without weakening
  provider-connectivity proof.
