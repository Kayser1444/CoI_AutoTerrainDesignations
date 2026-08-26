# Accessway V2 independent-lane terminal extension

Status: implementation-ready specification

## Purpose and authority

Replace the current single-extension-lane, recursive staggered-handoff fallback
with one bounded, snapshot-pure terminal-evaluation module. The replacement must
find forward, lateral, and inner-notch mining or dumping exits without inserting
intermediate terminal shapes into the global V/G search. It must preserve all
vertical alternatives through the first successful terminal rank while avoiding
the allocation-heavy corridor BFS responsible for the measured handoff hitch.

The semantic authority is the V2 handoff section of
[Accessway pathfinding](../done/accessway-pathfinding.md), with terminology from
[`CONTEXT.md`](../../../CONTEXT.md). This document specifies the implementation
shape and acceptance evidence. If wording conflicts, the authoritative design
and domain definitions win.

The execution boundary in
[ADR 0005](../../adr/0005-execute-pure-access-search-on-one-worker.md) also
applies: terminal evaluation consumes captured facts and request-local workspace
state only. It may not read Unity/Mafi objects, mutable settings, live callbacks,
or logging state.

## Problem being replaced

The production path currently spans four seams:

1. `AccessV2SearchSession.EnqueueSameTypeTerminalExtensions` asks
   `GetSameTypeExtensionRequest` for one unfinished lane.
2. It re-evaluates the already-generated straight from its parent under a
   mining or dumping operation, then recursively enumerates later straight
   states depth first.
3. `AccessV2Handoffs.EvaluateStaggeredExtension` pairs handoffs for the selected
   near and far lanes.
4. `TryBuildStaggeredPostWorkEscape` allocates hash sets, dictionaries, a queue,
   and a reconstructed center path for every attempt.

This is both semantically obsolete and expensive. It models one extension lane,
is depth-first and first-success-sensitive, does not expose the complete fixed
frontage catalog, and treats an incidental BFS center path as route data. The
captured Cluster 2 trace measured 951 staggered evaluations, 22.869 seconds
aggregate evaluator time, and a 331.71 ms maximum evaluation, with a 1.388
second player-visible hitch.

The replacement is not a general BFS with different pruning. It is a bounded
rank-synchronous shape evaluator plus allocation-free bitmask proofs inside each
shape.

## Scope

Included:

- V-to-G mining and dumping terminal forms initiated by a two-origin straight;
- rank-one and extended forward, lateral, and inner-notch frontages;
- rising, level, and falling vertical successors at every extension step;
- exact terminal costs, rays, cleanup, and explicit G-label integration;
- replay of a retained terminal form and handoff proof;
- diagnostics, semantic fixtures, allocation checks, and live profiling.

Excluded:

- integer-flat quick leveling, which remains a forward-only direct test;
- G-to-V deterministic handoffs;
- fixed-provider handoffs;
- mixed-operation terminal forms;
- terminal forms initiated by turns or strafes;
- intra-form dominance, beam search, best-height selection, or a deterministic
  terrain-derived shape planner;
- changes to ordinary V expansion, G traversal, G dominance, or goal settlement.

## Module and interface

Introduce one deep module named `AccessV2TerminalEvaluator`. It owns operation
derivation, rank construction, crest state, the frontage catalog, post-work
center classification, bitmask reachability, minimum-cleanup proof selection,
and result dominance. The global search owns only when to call it and how to
enqueue returned G labels.

The production interface should be one operation, conceptually:

```csharp
AccessV2TerminalResult Evaluate(
    in AccessV2TerminalRequest request,
    AccessSearchWorkspace workspace,
    AccessSearchDiagnostics diagnostics,
    AccessSearchCancellation cancellation);
```

`AccessSearchCancellation` is one concrete data-only token shared by the
cooperative and worker adapters; it exposes a cheap cancellation poll and no
callback delegate. The cooperative adapter may reflect the current
`AccessSearchSliceBudget`, while the worker adapter reflects the job's logical
cancellation authority. Do not expose separate operation,
transition, staggered-handoff, center, or ground-entry delegates. Synthetic
fixtures may construct captured facts or a fixture evaluator behind the
workspace seam; they must not define a second terminal algorithm.

`AccessV2TerminalRequest` contains only value/data references already owned by
the search:

- the predecessor state, history, and exact accumulated cost components;
- the candidate two-origin straight transition before ordinary-transition cost
  is charged;
- search bounds and remaining cost limit;
- captured G graph and immutable policy through the workspace/snapshot;
- an optional required G entry used only by replay, not normal search.

`AccessV2TerminalResult.Status` has exactly four outcomes:

- `NotApplicable`: no unique shared mining/dumping operation or the straight
  does not meet the rank-one crest trigger;
- `NoHandoff`: the terminal form was applicable but produced no successful
  candidate within four ranks;
- `Success`: one or more nondominated candidates from the first successful
  rank; and
- `Cancelled`: evaluation observed cancellation and discarded all partial
  candidates.

`Cancelled` is not `NoHandoff` and must never establish a negative search
result.

Each returned `AccessV2TerminalCandidate` contains sufficient value-owned data
to create an ordinary G label and later replay/materialize the terminal form:

- shared operation;
- terminal rank count and persistent rank deltas flattened into emission data;
- frontage descriptor and outward direction;
- captured G entry;
- scalar cardinal handoff distance and its traversal cost;
- exact generated work, fixed, exterior-ray, direct, and cleanup costs;
- exact ray constraints and deduplicated cleanup obligations;
- the final projected profiles/origins needed by materialization.

It does not contain an `EscapeCenters` sequence, a local search node, or a
reference to mutable workspace state.

## Search integration seam

Move terminal initiation to straight-successor expansion. For every
mechanically eligible two-origin straight from predecessor `P`:

1. Evaluate and enqueue the ordinary leveling V successor exactly as today.
2. Independently run the terminal module against the same unpriced straight and
   predecessor `P` when its cheap operation/crest trigger applies.
3. Convert every returned terminal candidate into an explicit G label whose
   cost is `cost(P) + exact terminal-form cost`.

The terminal call must not depend on the ordinary successor being enqueued,
settled, undominated, or later failing a handoff. Conversely, terminal failure
must not suppress the ordinary successor. This replaces the current
`candidates.Count == 0` fallback and prevents an ordinary leveling charge from
being inherited, corrected, or refunded.

The cheap trigger should derive at most one shared operation and classify the
two rank-one leading edges before constructing branch state. `NotApplicable`
must therefore be substantially cheaper than a full handoff evaluation.
Ambiguous, contradictory, mixed-only, or no-crest evidence returns
`NotApplicable`; it does not fork mining and dumping.

Integer-flat quick leveling remains in the ordinary handoff path and is not
called by this module. Mining/dumping V-to-G tests for an eligible straight,
including rank one, move into this module so the same frontage catalog,
post-work proof, costing, and first-successful-rank rule govern direct and
extended terminal shapes.

## Bounded state representation

Use fixed-capacity, request-local storage sized from the semantic ceiling:

```text
rank 1:  1 shape
rank 2: <= 3 shapes
rank 3: <= 9 shapes
rank 4: <= 27 shapes
total:  <= 40 shapes per initiating straight
```

Represent each branch with a parent index plus one immutable rank delta. A rank
delta stores:

- rank number and shared vertical mode (`rising`, `level`, or `falling`);
- newly added origin/profile for each advancing lane;
- current lane cursor for each lane;
- each lane's leading-edge crest state and whether it has ever been partial;
- frozen-lane bits;
- newly exposed frontage bits;
- incremental cost components, ray constraints, cleanup changes, and history
  delta;
- incremental post-work pathability and cleanup-host invalidation masks.

Snapshot-static facts and terminal-form caches are shared by all 40 possible
shapes. Copy cursor indices and fixed-width masks; do not clone origin lists,
histories, cleanup sets, center collections, or complete projected forms for
children. Flatten the parent chain only when emitting a successful candidate or
replay plan.

The frontier is two contiguous index ranges or two fixed buffers: current rank
and next rank. It is not a queue, global label set, or visited graph. No branch
dominance or deduplication is permitted in the first implementation.

## Required evaluation algorithm

### 1. Initiate rank one

From the straight's lagging-to-leading terrain relation, derive one coherent
mining or dumping operation shared by both lanes. Apply the transition from the
predecessor under that operation and score both initiating origins exactly once.
Reject bounds, profile, work/history, and cost-limit failures using the same
snapshot-pure feasibility rules as ordinary expansion.

Classify every sample of each lane's leading edge. Corner-only crest checks are
insufficient. At least one lane must be partially or fully edge-crested for the
shared operation. A fully crested lane freezes; every other lane remains active.

### 2. Evaluate one complete rank

For every live branch in the current rank, in deterministic order:

1. Update form-wide projected work, post-work terrain, pathability, prop, and
   cleanup-host facts only for newly added or invalidated cells.
2. Enumerate only newly exposed catalogued frontages.
3. Classify each frontage's two outward-facing edges independently of the
   leading-edge states.
4. If neither constituent edge is fully crested, skip the frontage.
5. Build a cheap oriented view of the branch's legal center mask in files 3-6.
6. Prove cardinal connectivity to every compatible captured G entry and retain
   its minimum-distance/minimum-cleanup result.

Do not decide success until every eligible frontage of every live branch at the
rank has been evaluated. Direct goal-G contact is merely another G entry and
does not short-circuit the rank.

If the rank has successes, discard dominated results and return all survivors.
Do not generate a deeper rank. Dominance is only among successful candidates:
for the same G entry, discard a candidate only when another has no greater exact
cost and no additional cleanup obligation; preserve differing nondominated
cleanup sets. Enumeration order must not select the result.

### 3. Generate the next rank

If the current rank has no success and is below rank four, generate every
compatible rising, level, and falling successor for every live branch.

- Freeze every fully leading-edge-crested lane.
- Advance every non-frozen lane.
- Apply one vertical mode to all advancing lanes in a child. Two active lanes
  therefore create at most three children, never a `3 x 3` per-lane product.
- Do not suppress falling, choose a preferred elevation, or prune a higher-cost
  branch except at the request's absolute cost limit.
- Reject only bounds/profile infeasibility, work/history conflicts, cost limit,
  or crest regression.

A lane that has reached partial leading-edge crest and later becomes uncrested
kills that child branch. It does not kill siblings or the ordinary V successor.
A lateral frontage's crest change never triggers regression. A fully crested
lane is frozen and cannot regress.

Stop with `NoHandoff` when rank four has no success or no children survive.

## Fixed terminal-frontage catalog

Do not infer arbitrary pairs from perimeter edges. For each branch expose:

- exactly one forward frontage formed by the two lane cursors, whether even or
  staggered; and
- every pair of consecutive collinear origin edges on each exposed lateral run,
  including the inner notch produced when one lane is frozen and the other
  advances.

Identify a frontage with a compact deterministic descriptor: orientation,
outward direction, two owning origin indices, and their outward edge indices.
Use that descriptor for unchanged-frontage suppression and replay. The catalog
must allow an extended `uncrested + partial` leading state to exit sideways when
the lateral frontage itself has a fully crested constituent edge.

## Allocation-free handoff proof

Replace `TryBuildStaggeredPostWorkEscape` and its hash graph with bounded bitmask
operations. Legal vehicle centers are only the four middle files of the form.
Map them to dense bit indices by longitudinal rank and file.

For one branch/rank:

- compute the operation-specific post-work pathable mask once;
- derive each frontage's start mask and captured-G-entry mask as oriented views;
- propagate a cardinal wavefront with shifts and masks until stable;
- record minimum cardinal distance by wave number;
- prefer a proof with no optional cleanup;
- otherwise compute the lowest-cleanup-cost valid proof to each entry and retain
  only that proof's deduplicated obligations.

The cleanup search may use fixed arrays/bitsets because the domain is bounded.
It must not allocate `HashSet`, `Dictionary`, `Queue`, LINQ result collections,
or a center-by-center path during candidate evaluation. The internal path is
proof-only and is neither emitted work nor route identity.

Cleanup-host availability is non-monotone. When a new origin occupies a host
that previously made dumping cleanup legal, invalidate every center depending
on that host and recompute those bits. Unchanged terrain, vanilla pathability,
and prop facts must remain cached across frontages and sibling branches where
their projected-work dependency is unchanged.

## Cost and G-label contract

All terminal costs are nonnegative and owned from the initiating predecessor.
For candidate `T` from predecessor `P`:

```text
cost(T at G entry) = cost(P)
                   + initiating-rank terminal cost
                   + later-rank terminal costs
                   + scalar handoff traversal cost
                   + selected cleanup cost
```

Preserve the existing separate traversal/generated/direct/fixed/ray/cleanup
accounting. Charge cleanup only for the retained proof. Never charge all
pathable-mask cleanup, inherit the ordinary leveling successor's cost, or apply
a negative correction.

The caller creates one normal G label per returned candidate. Existing G-state
dominance, G traversal, G-to-V re-entry, and settled-goal handling remain the
authority. Even a retained entry already in the goal set must win normal queue
settlement; the terminal module never returns a completed route.

## Replay and materialization

Replay must use the retained terminal plan rather than rediscovering terminal
branches. Reapply each rank delta from the predecessor under the shared
operation, validating the same profiles, history, rays, costs, and cleanup-host
facts. Then re-enumerate the retained frontage descriptor and reprove
connectivity to the retained G entry with the same bounded-mask helper.

Replay equality requires the operation, flattened terminal form, frontage,
entry, scalar distance/cost, and cleanup obligations. It explicitly does not
require the same incidental internal center path. This removes
`EscapeCenters` from terminal candidate identity and route data.

Normal goal-time replay and current-live-state validation remain defense in
depth. A replay mismatch rejects that candidate and allows the G search to
continue as it does for other rejected goals.

## Cancellation and resumability

Implement the evaluator atomically first, because the accepted worker design
removes it from player-visible frames. Poll cancellation at least between branch
evaluations and inside any cleanup-proof loop whose work can exceed one bounded
mask propagation. Record the maximum work between polls.

Do not expose slicing in the module interface. If cooperative profiling proves
the bounded call still needs resumability before worker migration completes,
add a data-only continuation behind the same `Evaluate` contract/adapter. A
resumed evaluation must preserve rank order and return exactly the same
candidates as uninterrupted execution. Intermediate branches remain private and
are discarded on cancellation.

## Diagnostics

Aggregate diagnostics are mandatory and must not allocate per-branch strings
unless trace detail is enabled. Record, per initiating straight and in totals:

- terminal attempts, `NotApplicable`, `NoHandoff`, success, and cancellation;
- derived mining/dumping operation and trigger rejection reasons;
- live and generated shapes per rank and total shapes (assert `<= 40`);
- rising/level/falling generated, accepted, and rejection counts;
- lane freezes and branch crest regressions;
- forward, lateral, and notch frontages exposed, eligible, and evaluated;
- unchanged frontages skipped;
- mask proofs, G-entry checks, successful proofs, wave iterations, and cleanup
  alternatives examined;
- snapshot fact/cache hits, recomputations, and cleanup-host invalidations;
- returned and dominance-pruned candidates;
- elapsed ticks for trigger, transition evaluation, rank-fact update, frontage
  evaluation, mask proof, cleanup proof, and complete terminal attempt;
- maximum single-attempt ticks, allocations, shapes, frontages, and work between
  cancellation polls.

Retain a fixed-capacity detail sample for the slowest attempt and first examples
of each rejection. Diagnostic collection must not change candidates or ordering.

## Required fixtures

Add pure fixtures at the terminal-module seam. Every fixture must assert result
data and, where relevant, shape/frontage diagnostics.

1. Rank one succeeds and charges from the predecessor, with the ordinary
   leveling sibling still enqueued independently.
2. No unique shared operation returns `NotApplicable`; mixed mining/dumping is
   never generated.
3. Integer-flat leveling is not handled by the terminal module.
4. Rising, level, and falling successors are all explored at every extension
   step, including a case where falling is the only success.
5. Two active lanes create at most three children and share one vertical mode;
   no nine-way per-lane product appears.
6. A fully crested lane freezes while a partial lane advances and exposes an
   even or staggered forward frontage.
7. An inner-notch frontage and each lateral side are catalogued; an
   `uncrested + partial` leading state can succeed laterally.
8. Leading partial-to-uncrested regression kills only that vertical branch;
   rising/level/falling siblings and the ordinary V successor remain viable.
9. Lateral frontage regression alone does not abort a branch.
10. Every eligible frontage and branch at one rank is evaluated before success
    is returned; branch permutation produces identical nondominated results.
11. First successful rank stops all deeper generation, but direct goal contact
    does not skip other successes at that rank.
12. The unconstrained fixture reaches exactly `1 + 3 + 9 + 27 = 40` shapes and
    never exceeds fixed capacity.
13. A cardinal path containing a turn succeeds where straight per-lane spokes
    would fail.
14. A broken rank in the four-file mask fails without graph allocation.
15. A no-cleanup proof beats a cleanup proof; otherwise the minimum-cleanup-cost
    proof is selected per G entry and only its obligations are charged.
16. Occupying a dumping cleanup host invalidates every dependent cached center.
17. Candidates with different nondominated cleanup sets survive; dominated
    candidates for the same G entry do not.
18. Replay succeeds when bitmask propagation chooses a different incidental
    center path but the retained frontage, entry, distance/cost, and cleanup
    proof remain valid.
19. A successful candidate enters the ordinary G queue, can traverse G and
    re-enter V, and reaches goal only on normal settlement.
20. Cancellation during branch or cleanup evaluation returns cancellation,
    publishes no partial candidates, and does not affect a later uninterrupted
    retry.

Keep existing quick-leveling, ordinary V/G, G-to-V, replay, cost-accounting, and
fixed-provider fixtures as regression coverage.

## Implementation sequence

1. **Contracts and fixtures.** Add terminal request/result/candidate/frontage
   descriptors and fixture builders. Add failing semantic fixtures without
   changing production routing.
2. **Bounded proof helper.** Implement dense masks, cardinal wavefront,
   per-entry minimum distance, and cleanup selection. Prove zero steady-state
   managed allocation with a repeated fixture.
3. **Terminal evaluator.** Implement operation derivation, fixed frontage
   catalog, persistent rank deltas, rank-synchronous frontier, costing, crest
   regression, and diagnostics behind the new module interface.
4. **Search integration.** Invoke the module beside eligible straight expansion
   and enqueue its G candidates. Keep the ordinary leveling sibling independent.
   Run semantic comparison diagnostics before deleting the old path; do not run
   both algorithms into the production queue.
5. **Replay/materialization.** Persist flattened rank deltas and frontage proof
   identity, reprove masks, and remove terminal reliance on `EscapeCenters`.
6. **Remove obsolete seams.** Delete the single-lane request, three terminal
   delegates, recursive extension, staggered evaluator, and hash BFS listed
   below. Update old fixtures to the new module seam.
7. **Profile and tune.** Re-run the captured problem scenario and large synthetic
   stress cases. Consider resumability or a deterministic shape planner only if
   measurements fail the acceptance gates.

Each step must compile and keep the production backend usable. Do not retain the
old fallback as a permanent compatibility path.

## Proposed source layout

Keep the implementation navigable without splitting the module's interface
across the repository:

- `src/Access/V2/AccessV2TerminalEvaluator.cs`: request/result contracts,
  evaluator interface, rank frontier, branch deltas, operation/crest logic, and
  successful-candidate emission;
- `src/Access/V2/AccessV2TerminalProof.cs`: internal fixed frontage catalog,
  dense center masks, wavefront, cleanup proof, and proof replay helper;
- `src/Access/V2/AccessV2TerminalFixtures.cs`: focused pure semantic,
  permutation, cancellation, allocation, and ceiling fixtures, invoked from
  `AccessV2Fixtures.ValidateAll`;
- `src/Access/V2/AccessV2Search.cs`: the narrow straight-expansion call and G
  label adapter only;
- `src/Access/V2/AccessV2Replay.cs`: retained-rank replay and proof revalidation;
- `src/Access/AccessSearchWorkspace.cs`: request-local shared terminal caches
  and the production evaluator instance;
- `src/Access/AccessSearchModels.cs`: aggregate terminal diagnostics; and
- `src/Access/AccessPathSearch.cs`: construction/wiring only, with the obsolete
  terminal delegate adapters removed.

If a file grows large, split internal implementation by rank construction and
proof mechanics, but keep `AccessV2TerminalEvaluator.Evaluate` as the sole
search-facing interface. Do not reintroduce the present callback fan-out under
new filenames.

## Obsolete symbol inventory

Delete or replace these current symbols and call sites:

- `AccessV2TerminalExtensionRequest`;
- `AccessV2Handoffs.GetSameTypeExtensionRequest`;
- `AccessV2TerminalExtensionOperationEvaluator`;
- `AccessV2TerminalTransitionEvaluator`;
- `AccessV2StaggeredHandoffEvaluator`;
- the corresponding three `AccessV2SearchSession` fields and constructor
  parameters;
- `AccessV2SearchSession.EnqueueSameTypeTerminalExtensions` and its recursive
  local `Extend` function;
- `AccessV2Handoffs.EvaluateStaggeredExtension`;
- `AccessV2Handoffs.TryBuildStaggeredPostWorkEscape`;
- `AccessPathSearch.EvaluateV2StaggeredHandoffs` overloads;
- terminal replay dispatch through `IsStaggeredExtension` and `NonCrestLane`;
- `EscapeCenters` as terminal route identity.

`AccessV2HandoffCandidate` may remain for non-terminal seams, but terminal
results should not be forced back into its obsolete single-lane/staggered shape.
Prefer a shared narrow G-handoff value only if it does not leak terminal internals
into ordinary quick-leveling or G-to-V code.

## Acceptance gates

The implementation is complete only when all of the following hold:

- `dotnet build` succeeds and `git diff --check` is clean;
- all existing V2 fixtures and the required terminal fixtures pass;
- source search finds none of the obsolete single-lane/delegate/recursive
  symbols above, terminal code does not read or compare `EscapeCenters`, and no
  hash/queue BFS remains in terminal evaluation;
- every attempted form asserts at most four ranks and 40 shapes;
- repeated warm terminal evaluation performs zero steady-state managed
  allocations in the bounded frontier and mask-proof loops;
- branch/frontage enumeration permutation does not alter returned candidates;
- cooperative and worker adapters return semantically identical results;
- cancellation is acknowledged at the documented bounded checkpoints and never
  publishes a partial negative result;
- the captured Cluster 2 case no longer reports a staggered-evaluator hitch,
  and its replacement diagnostics identify bounded shapes, frontages, mask
  work, allocation, and maximum attempt time;
- live playtests cover mining, dumping, lateral, staggered, notch, cleanup,
  regression, G traversal, replay, cancellation, and ordinary-V recovery after
  a discarded terminal branch.

Do not set a speculative millisecond threshold before collecting the new phase
diagnostics. The hard performance contracts are bounded shapes, bounded masks,
no general graph, zero steady-state inner-loop allocation, shared rank facts,
and responsive cancellation. Use the first implementation's measurements to set
a regression budget afterward.

## Deferred optimization decision

The initial implementation deliberately explores all feasible vertical modes at
each rank. If profiling still shows material cost after the bounded allocator-free
implementation, investigate a deterministic terminal-shape planner. It may
replace branching only with fixtures or a proof that discarded trajectories
cannot provide a winning forward, lateral, or notch handoff. Crest regression,
falling-mode suppression, beam width, or best-current-height heuristics are not
acceptable substitutes.
