---
status: accepted
---

# Shrink oversized access snapshots with geometry-only masked corridors

When a normal full-area access snapshot exceeds its retained-memory ceiling,
ATD will make one best-effort access attempt against a request-local reduced
access snapshot. Large modded tower areas are often drawn for management
convenience while the active work and a useful connection are close together;
rejecting the whole request leaves these ordinary short ramps and fills
unserved. The reduced attempt seeks some valid route rather than the same or
best route that a full-area search would select.

The reduced domain is chosen from geometry only. Its inputs are the access
obligation's source footprint, explicit goal anchors, a tower-access proxy when
ordinary reachable-ground goals are not yet available, the finite managed-area
polygon, a conservative tile budget, and the immutable access-search geometry
policy. Terrain heights, terrain designations, pathability, buildings, props,
and other captured world facts do not influence the mask's shape. Bounded
world reads may establish valid endpoint coordinates before reduction; that
endpoint discovery remains separate from the geometry-only reducer.

The reducer works on the search's discrete horizontal lattice. It first finds
a low-turn source-to-goal spine that fits a minimum viable corridor. Straight
segments receive the required width and turns receive larger maneuver bulbs,
so additional turns consume real area rather than only an arbitrary score.
It may include only a subset of source frontages and goals: further branches
are added greedily by their marginal masked cost, and each included branch
must receive its full minimum viable corridor before another is admitted.
Known reachable fixed-network goals and the tower-access proxy compete in the
same construction. Remaining capacity widens the resulting network with
balanced outward growth. This produces one maximum useful mask rather than a
sequence of progressively wider snapshots, because capture overhead is small
relative to the cost of a failed access search.

The source footprint describes one access origin cluster, not necessarily one
tile. It supplies candidate boundary frontages, but the reduced snapshot need
capture only enough local source geometry for each selected complete launch;
it does not have to contain the entire cluster. Reduction is performed per
active access request or cluster so several distant farming obligations do not
recreate a tower-wide oversized union. If no complete minimum-width connection
from any source frontage to any goal fits the conservative budget, ATD skips
the search rather than capture a mask that cannot possibly connect an endpoint
pair.

A reduced snapshot has two non-rectangular masks. The **search mask** is the
policy-authorized domain in which route states and generated terrain-work
origins may exist. The **capture mask** is the search mask dilated by the
ground, clearance, landslide, side-ray, and end-buffer context required to
evaluate interior work. Capture context outside the search mask never grants
generation authority. Missing facts beyond the capture mask fail closed as a
reduced-snapshot boundary rejection. The normal search mask is clipped to the
managed-area polygon; the existing outside-area fallback may use the polygon
dilated by its authorized generation margin. One maximum reduced capture
provides facts for both phases, while search retains the existing inside-first,
outside-fallback ordering.

Retained memory must scale with masked tiles or endpoint count, not with the
area of the masks' enclosing rectangle. Captured terrain already uses keyed
collections, but bounds-sized derived structures such as the A* any-goal
distance field must become mask-aware or safely weaken themselves for reduced
snapshots. Goal-distance representation remains an implementation and
benchmark choice: small goal sets may be evaluated on demand, while larger
sets may use a sparse or mask-indexed workspace structure. Derived search
indexes remain in the request-local access search workspace rather than being
folded into captured terrain facts. The conservative geometry-only budget
includes mask storage, required capture halo, and fixed headroom; it may leave
some of the configured memory ceiling unused rather than use world density to
shape the mask.

A candidate found in a reduced snapshot follows the unchanged pure
materialization, authoritative live validation, and transactional commit path.
A failed reduced search is reported distinctly as `ReducedAreaNoPath`; it is
inconclusive because an omitted part of the full managed area may contain a
route. It must never establish canonical `NoPath` or otherwise become an
authoritative negative result. Farming may retry it under the existing bounded
event-assisted policy when the obligation or environment changes. This is a
deliberate resource-degradation domain, not a claim of full-area
modeled-rule preservation.

Mask construction is deterministic for its inputs and versioned policy. Access
Search Laboratory cases and diagnostics record the reducer version, selected
source frontages and goals, spine network, search mask, capture mask, budget,
and policy. Replays consume the recorded masks rather than recomputing them
under newer reduction logic. This preserves ADR 0005's sealed worker-safe
snapshot and provisional-result rules and ADR 0006's exact recorded-input
replay boundary.

Rectangular cropping was rejected because a narrow diagonal or winding route
still retains its large enclosing rectangle. Terrain- or designation-guided
cropping was rejected because it couples resource admission to the facts that
the oversized snapshot could not safely capture and makes selection harder to
reproduce. Requiring all goals or the full source cluster was rejected because
distant endpoints can defeat reduction. Progressive widening was rejected
because an early failed search is expected to cost more than capturing the
largest permitted geometry once.

The staged implementation and qualification gates are maintained in the
[reduced access snapshot implementation plan](../dev/planned/reduced-access-snapshot-implementation-plan.md).
