# V2 Ground-to-V Canonical-Grid Prefilter

## Status

**Implemented: canonical direction filtering, exact two-anchor derivation,
exact bridge-height profiles, direct leveling, uniform lane operations,
monotonic inclination proofs, and first-success V caching**

## Summary

V2 currently considers a reverse G-to-V transition from every explored ground
center. For each center it tries all four cardinal V travel directions, nearby
width-two band anchors, three profile modes, and a range of profile heights
before the handoff evaluator proves whether the requested ground center can be
the entry of the reverse seam.

The finalized reverse search does not enumerate a height window. For each
eligible direction it derives the two possible width-two bands (handoff cell
with its companion on either transverse side), derives one vertical placement
per inclination and terminal proto, and tests the exact G bridge first.

If the bridge terrain is already on a designation level within epsilon, the
only candidates are leveling candidates in `flat, up, down` order. The G center
is already mask-pathable and lies on the V face, so leveling owns the complete
post-work terminal surface; lane/contact enumeration, mining/dumping selection,
post-work center classification, and corridor BFS are skipped.

If the bridge is between levels, the fallback candidates are:

* mining at the nearest compatible digging-side level, ordered
  `up, flat, down`; and
* dumping at the nearest compatible filling-side level, ordered
  `down, flat, up`.

Each mode/proto pair has exactly one vertical placement whose entry edge uses
that selected level.

The completed V/G handoff geometry already imposes a cheaper necessary
condition: a ground entry center must lie immediately outside a canonical
4x4 origin boundary. Its coordinate residue therefore determines whether it
can enter V and, if so, the only possible G-to-V travel direction or
directions. Apply that rule before enumerating anchors or profiles.

For mining and dumping this is candidate narrowing, not a replacement for
handoff validation. The direct leveling case is intentionally stronger: after
the ordinary V transition/history evaluation succeeds, the level mask-pathable
G bridge proves the seam and all subsequent handoff pathability/cleanup checks
are approved.

## Motivation

The Cluster 2 live case exposed the cost of discovering reverse handoffs too
late. One successful T1 run reported:

* `26,811` expanded G states;
* `107,244` G-to-V origin checks;
* `818,083` G-to-V profile attempts; and
* `503,803` G-to-V attempts that produced no handoff.

The search completed after roughly 42-46 seconds and visited `93,218` states.
The prior T3 attempt was cancelled after 6.3 seconds with only `151` states
popped, consistent with high work per explored state.

The handoff evaluator now accepts a required ground entry and rejects seams
that leave through a different center. That is necessary for correctness, but
the search still reaches this filter only after generating and evaluating
many impossible direction, anchor, and profile combinations.

## Canonical-grid invariant

Every V lane origin is aligned to the four-tile origin grid. A forward handoff
leaves the final rank through one of its four cardinal faces. The first G
center is exactly one tile beyond that face.

For a positive-X V-to-G exit, the final in-corridor X coordinate is
`origin.X + 3`; the outside G center is at `origin.X + 4`, whose residue is
zero. For a negative-X exit, the outside center is at `origin.X - 1`, whose
residue is three. Y follows the same rule.

Reversing those exits gives the possible G-to-V travel directions:

| Ground-center residue | Possible G-to-V travel |
|---|---|
| `(ground.X & 3) == 0` | `-X` |
| `(ground.X & 3) == 3` | `+X` |
| `(ground.Y & 3) == 0` | `-Y` |
| `(ground.Y & 3) == 3` | `+Y` |

Within each repeating 4x4 cell, the allowed directions are:

```text
-X/-Y   -Y      -Y     +X/-Y
-X      none    none   +X
-X      none    none   +X
-X/+Y   +Y      +Y     +X/+Y
```

Consequently:

* the inner 2x2 centers can never enter V;
* eight residue positions allow exactly one direction; and
* the four corners allow exactly two directions.

Across a uniform residue distribution, the search has one possible direction
per G center on average instead of testing four. One quarter of G centers can
skip reverse-handoff discovery entirely.

## Applicability to all current handoffs

The invariant applies to each completed V2 handoff form:

* **Quick forward handoff.** `GetForwardCenter` places the selected G center
  at `anchor + 4` on a positive face or `anchor - 1` on a negative face.
* **Multi-origin forward handoff.** Every additional segment advances by four
  tiles. The final rank therefore retains the same outside-boundary residue.
* **Lateral handoff.** The exit axis changes, but the selected lane origins
  remain aligned and the outside center uses the same positive-zero or
  negative-three rule on that axis.

Vehicle width changes the legal transverse center offsets inside the complete
eight-file corridor. It does not change the boundary residue on the exit axis.
The transverse coordinate may have any residue, which is why the rule selects
a face rather than a single point on that face.

## Proposed search change

Replace the unconditional four-direction array in G-to-V expansion with an
enumerator derived from the ground center:

```csharp
internal static IEnumerable<Tile2i> EnumerateGroundToVTravelDirections(
    Tile2i ground)
{
    int x = ground.X & 3;
    int y = ground.Y & 3;

    if (x == 0) yield return new Tile2i(-4, 0);
    if (x == 3) yield return new Tile2i(4, 0);
    if (y == 0) yield return new Tile2i(0, -4);
    if (y == 3) yield return new Tile2i(0, 4);
}
```

`ExpandGroundToV` should return immediately when this enumerator is empty.
For each emitted direction it should enumerate only band anchors capable of
placing the requested G center on that face, then retain the existing profile
and handoff validation.

Before generating height/profile variants, the implemented search also asks
the snapshot whether both 4x4 lane origins of the width-two seed band are
valid managed-area origins. This rejects outside-area G-to-V candidates at the
first anchor-specific step instead of relying on later profile feasibility.

Use bitwise residues rather than `% 4` so negative world coordinates retain
the same `0..3` canonical-grid classification.

## Anchor narrowing

The surrounding 3x3-origin scan is replaced with exact derivation. Once the G
residue selects a direction, the longitudinal handoff-cell origin is fixed.
Canonical half-open ownership selects its transverse 4x4 origin. The width-two
band then has only two possible placements:

1. the handoff cell is lane zero and its companion is on the positive
   transverse side; or
2. the handoff cell is lane one and its companion is on the negative
   transverse side.

In stored-anchor terms these are `handoffOrigin` and
`handoffOrigin - laneDirection`. Area validation immediately rejects a band
whose two origins are not both managed.

Four G centers can enter a complete eight-file frontier. Their two-anchor sets
overlap: the shared band appears four times (one miss followed by up to three
hits), while each neighboring companion placement appears twice. In an
unobstructed interior this gives five cache hits among eight anchor attempts,
or 62.5%; the shared band alone has a 75% hit rate.

## Vertical candidate derivation

For a proposed inclination, solve the profile center height from the selected
entry-edge level instead of scanning `baseHeight +/- 3`. `Up` and `down` are
relative to G-to-V travel, not absolute X/Y signs.

Let `h` be the precise terrain height at the required G bridge and let epsilon
be the existing exact-level tolerance.

* If `h` is within epsilon of an admissible designation level, emit only:
  `Leveling(flat@h)`, `Leveling(up@h)`, `Leveling(down@h)`.
* Otherwise emit one mining placement per mode at the nearest admissible
  digging-side level in `up, flat, down` order, followed by one dumping
  placement per mode at the nearest admissible filling-side level in
  `down, flat, up` order.

The entry edge of every emitted profile is exactly the selected level. No
other vertical offsets are useful for reverse admission from this G center.

### Uniform lane operation invariant

A width-two non-leveling handoff uses one prototype across both lanes. Mixed
mining/dumping lane pairs are rejected before contact pairing or corridor
validation. A leveling bridge remains the sole special case: one proven level
G bridge promotes both lanes to leveling.

This deliberately gives up cross-slope seams that cut one lane while filling
the other. G routing has broad freedom to choose another boundary, while the
uniform invariant removes ambiguous materialization, reduces pair evaluation,
and makes the inclination proof below valid across the complete eight-file
band.

### Monotonic inclination proof

With a common entry-edge level, mining targets are pointwise ordered
`up >= flat >= down`. If a uniform mining seam is pathable for `up`, the
`flat` and `down` variants only remove more terrain and cannot invalidate that
post-work route. If `up` fails but `flat` succeeds, only `down` inherits the
proof.

Dumping is the dual: targets are pointwise ordered `down <= flat <= up`. A
uniform dumping seam approved for `down` proves `flat` and `up`; a proof first
obtained for `flat` proves `up`.

The inherited proof includes the accepted span, continuation modes, lane
contacts, escape centers, G entry, and handoff cleanup recipe. Before reuse,
the search verifies the new span is pointwise at least as aggressive at every
profile sample. Each inherited variant still performs its own bounds, height
envelope, transition feasibility, history, work, cleanup, and exterior-ray
evaluation. It skips only the general handoff evaluator: corner/lane
selection, corridor-center classification, BFS, and local-escape validation.
Failures are never inherited in either direction, and mixed-prototype seams
never establish a monotonic proof.

### Cluster 2 measurement: uniform lanes and monotonic ordering

The first completed Cluster 2 run with these rules finished successfully on
2026-07-17 at 00:36. It selected the same route and exact cost (`4361.51`) as
the preceding completed run, and post-placement V2 validation again passed.

| Counter | Before | Uniform/monotonic run | Change |
|---|---:|---:|---:|
| Search time | 100.68 s | 90.78 s | -9.90 s |
| Visited states | 47,096 | 47,087 | -9 |
| G-to-V extensions | 226,697 | 225,095 | -1,602 |
| General handoff evaluations | 223,196 | 221,636 | -1,560 |
| Accepted lane-pair checks | 3,609,646 | 3,573,050 | -36,596 |
| Mixed lane pairs rejected early | unavailable | 23,125 | new |
| Monotonic inherited accepts | unavailable | 0 | new |
| G-to-V time | 58.63 s | 53.30 s | -5.33 s |
| General handoff time | 38.05 s | 35.02 s | -3.03 s |

The mixed-lane invariant is therefore active and removes real pair work. The
monotonic shortcut did not activate in this run: no successful defensive
uniform-proto seam had a later more-aggressive variant that reached proof
reuse. The observed runtime improvement is larger than the counter reduction,
so some of it may be ordinary run-to-run timing variance. The measurement does
not justify claiming a monotonic speedup yet.

Subsequent diagnostics distinguish proof creation from reuse. `v2Monotonic`
reports established proofs, later eligible profile candidates, actual reuse
attempts, cache and prefilter skips, and transition-, geometry-, or
route-emission-stage rejection counts. The original `monotonic` counter remains
the number of inherited seams successfully accepted; establishing the first
`up`, `flat`, or `down` proof does not itself count as an inherited accept.

## Potential-field ground suffix completion

In a potential-guided V2 search, once a popped G center has a finite
ground-goal potential, the immutable G graph already contains an exact
Dijkstra distance to a tower goal. Continuing to enqueue every tile on that
suffix is unnecessary and currently causes a G-to-V evaluation at every
intermediate center. Plain Dijkstra retains ordinary expansion and its exact
global route comparison semantics.

The search now attempts to reconstruct the suffix immediately by repeatedly
choosing a neighbor satisfying the Bellman equality:

`distance(current) = stepCost(current, next) + distance(next)`

Each selected step still performs the ordinary projected-center, swept-center,
local cleanup, history, cost-limit, and graph-transition validation. Cleanup
keys and costs are accumulated into ordinary G route nodes so materialization
retains the exact cleanup recipe. If any descending step cannot be validated,
the fast completion is abandoned without mutating the search and normal G/V
expansion remains available.

The first validated suffix is accepted immediately. This deliberately gives
up searching for a later, marginally cheaper V shortcut after the route has
entered a proven tower-connected G component. That is consistent with G being
used for pathability validation and approximate cost rather than as the final
vehicle route optimizer.

## First-success V cache

Successful reverse handoffs are cached by canonical V boundary state:

* paired origins;
* travel direction;
* inclination; and
* derived vertical placement.

The G bridge tile and G route are deliberately excluded. The cached value
retains the operation(s), terminal origins, escape/bridge geometry, cleanup
keys and cost, and handoff cost. The first accepted G path owns that V state;
later G centers skip transition and handoff evaluation. Failures are not
cached without their G/history context.

## Relationship to the A* escape field

The direction rule is exact and can safely identify centers with no possible
G-to-V exit. The disconnected-ground escape potential may use the same rule as
a first improvement: seed only centers on at least one canonical exit face,
then propagate through their G component.

This remains a lower bound because the residue test is a necessary condition
for every concrete reverse handoff. The heuristic must still omit expensive
profile, history, cleanup, disturbance, and landscaping feasibility unless a
later filter is independently proven to be a necessary condition. False
positive escape seeds weaken guidance but preserve admissibility; false
negative seeds can invalidate A* optimality.

Implement and measure search-side filtering before changing the heuristic.
This separates reduced expansion cost from improved queue guidance and makes
regressions easier to attribute.

## Correctness constraints

The implementation must not:

* decide that a handoff is valid;
* bypass `requiredGroundEntry` validation for mining or dumping;
* replace post-work corridor pathability for mining or dumping;
* assume that V can begin only at a visible cliff or ground-component edge;
* exclude an early grade that begins on otherwise open ground;
* depend on generated-history state; or
* change V-to-G handoff selection or materialization.

Except for the explicitly proven direct-leveling bridge, the prefilter answers
only: "Can this canonical G center geometrically be immediately outside a V
face in this direction?"

## Implemented sequence

1. Filter directions from canonical G-center residues.
2. Derive the two companion-band placements and reject unmanaged origins.
3. Read precise bridge terrain and derive three level or six uneven profiles.
4. Evaluate the normal transition/history rules.
5. Synthesize level bridges directly; send uneven candidates through the
   general handoff evaluator.
6. Cache the first accepted seam by V boundary state, excluding the G seed.
7. Reject mixed lane operations, then reuse a uniform-proto seam for later
   pointwise-more-aggressive inclinations.
8. Report anchors, profiles, cache hits, direct-level accepts, mixed-pair
   rejections, and inherited monotonic accepts in live diagnostics.

## Fixtures

### Exhaustive residue table

For every `(xResidue, yResidue)` pair in `0..3`, assert the exact direction set
shown above. Repeat with positive and negative absolute coordinates.

### Forward/reverse symmetry

For quick, multi-span, and lateral handoff fixtures:

1. construct an accepted V-to-G candidate;
2. take each reported `GroundEntryCenter`;
3. assert that the reverse direction enumerator includes the direction back
   into the V band; and
4. assert that reverse evaluation with that required entry accepts the same
   seam geometry.

### Inner-center rejection

Assert that residue positions `(1,1)`, `(1,2)`, `(2,1)`, and `(2,2)` perform no
G-to-V anchor or profile evaluation.

### Search equivalence

Run otherwise identical filtered and unfiltered fixture searches. They must
produce the same success/failure result, accepted cost, route state sequence,
handoff metadata, and cleanup ownership. If tie-equivalent routes differ, run
both through replay and compare final plan cost and validity.

## Live validation

Use the standard Cluster 2 save after loading a DLL whose timestamp matches the
new build. Record separately for T1 and T3:

* total visited states and pending high-water;
* expanded G and V states;
* G-to-V direction, origin, and profile attempts;
* G-to-V no-handoff count;
* handoff evaluation time and total search time;
* selected cost and cost breakdown;
* selected G/V route and final materialized plan; and
* post-placement Mega pathability.

The direction filter should reduce attempted directions by approximately 75%
on broadly distributed G centers. Every surviving direction now produces two
anchors and either three or six profiles, replacing the loose 3x3 anchor and
height-window scans. On mostly level terrain, general handoff evaluations
should be rare and `directLevel` should dominate successful reverse admission.

## Expected outcome

The implemented path moves exact geometry and height decisions ahead of the
expensive evaluator. Level bridges stop after transition validation; uneven
bridges retain the completed handoff implementation as the authority for seam
feasibility and materialization.
