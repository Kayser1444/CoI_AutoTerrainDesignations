# V2 Ground-to-V Deterministic Handoff

## Status

**Implemented**

This document supersedes the earlier canonical-face prefilter, two-anchor
enumeration, singleton floor/ceiling profile placement, monotonic seam reuse,
and reverse-handoff corridor-BFS design. The existing direct leveling work and
ground-suffix optimization remain useful, but rough G-to-V admission is to be
rebuilt around every reached G center.

## Objective

Make reverse V2 ground-to-generated transitions cheap enough on rough terrain
that ordinary G exploration remains practical.

The previous implementation asked the general V2 handoff evaluator to discover
a route through the proposed worked cells. That evaluator pairs lane contacts,
classifies post-work centers, and performs an internal BFS through as many as
four generated rows. Repeating that work for many G centers and profile
variants dominates rough-terrain search time.

The replacement uses facts the search already established:

* the current G center and its resolved vehicle mask are pathable;
* a width-two V2 handoff has a deterministic companion side at that center;
* the vehicle-width cardinal bridge from the future V face to G is finite and
  small; and
* rough profile geometry reduces to three inclinations at one operation-
  specific starting level.

Each candidate therefore receives a fixed post-work V-face-to-G proof rather
than an internal path search.

## Scope

This design changes **G-to-V only**.

V-to-G retains its existing handoff evaluator, including independent
down/flat/up testing and post-work corridor validation. V-to-G fixes the face
connected to the current V state and leaves the terrain-facing end free. A
successful lower mining or dumping variant does not generally prove another
variant: a different ending surface can leave natural terrain too steep.

No G-to-V corridor-BFS fallback is retained. A rare candidate that supports
only a winding or lateral path inside its worked cells may be rejected. This
is a deliberate completeness tradeoff. Because every reached G is tested, it
is extremely unlikely that such a seam is the only possible handoff when all
direct face-to-G seams fail.

## Search order

For each popped G search node:

1. Apply the existing `inTowerArea` authorization check before doing any
   G-to-V candidate work. Outside-area G remains available for ordinary ground
   traversal but cannot seed generated terrain work.
2. Run the ordinary ground-goal handling in its existing order.
3. Consider G-to-V from this G regardless of its longitudinal canonical
   residue. Do not require the G center to lie on a four-tile cell face or to
   reach a particular interior residue.
4. Derive the unique width-two band placement for each travel direction.
5. Try the direct leveling candidate when its stricter shared-edge predicate
   applies.
6. For each concrete paired profile state, stop immediately on a positive
   request-local cache hit.
7. Otherwise validate the leveling candidate or enumerate and validate the
   precomputed rough mining/dumping candidates with the deterministic
   V-face-to-G predicate.
8. Enqueue the first successful V state through ordinary transition, history,
   and cost handling, then add its paired profile state to the positive cache.

The tower-area test is intentionally first. Candidate-table lookup, profile
construction, terrain projection, and handoff validation must not run for a G
that has no authority to create the proposed V origins.

## Every-G admission

The rough path does not use a canonical-face eligibility filter. A G center on
`X & 3 == 0` may hand off in X+ even when the next tile in X+ is not currently
pathable, provided the mining or dumping designation creates a valid post-work
path from its future V face back to G. The same principle applies at every
other residue and under rotation/reflection.

This is the same core principle as the V1 general handoff:

* G exploration proves the current vehicle placement;
* prospective work may make currently blocked inward tiles usable; and
* the generated continuation, rather than another pre-work G tile, proves the
  route after the handoff.

V2 improves on V1's broad nearby-origin and height-window scan by deriving one
companion placement and a small pre-enumerated profile set.

## Deterministic companion placement

The current G center's transverse residue identifies which half of the
vehicle clearance is already represented on that side. The companion is
placed on the opposite side.

For travel along X:

| `G.Y & 3` | Companion side |
|---:|---|
| `0` or `1` | Y- |
| `2` or `3` | Y+ |

For travel along Y:

| `G.X & 3` | Companion side |
|---:|---|
| `0` or `1` | X- |
| `2` or `3` | X+ |

This mapping is independent of the sign of longitudinal travel. Direction
selects the longitudinal V placement; the transverse residue selects the
companion. Exactly one paired band is produced for a `(G, direction)` pair.

Use bitwise `& 3` or an equivalent positive modulo so negative world
coordinates use the same `0..3` classification.

Both 4x4 origins must pass the managed-area/origin check before profiles are
constructed.

## Direct leveling path

The existing shared-edge leveling proof remains the first candidate when its
strict geometry applies:

* G lies on the relevant canonical shared edge;
* the captured precise terrain height is an admissible designation level;
* the primary profile has the required level edge; and
* the deterministic companion profile is compatible with the primary lane.

A successful direct result covers the primary and companion together. Both
are guaranteed workable and pathable after leveling. It may therefore skip
general lane/contact enumeration, post-work classification, corridor BFS, and
local escape search.

A fully accepted result is cached by its resulting paired V band/profile state.
Because leveling is attempted first and is cheap, a smooth G commonly claims
the state before another G reaches the rough path. Later rough attempts for the
same state are then skipped entirely.

## Pre-enumerated rough profile candidates

Candidate generation is deliberately simple. Let `h(G)` be the precise height
of the current G center and let the profile start be the starting face relative
to G-to-V travel.

Emit exactly:

* **Mining:** start at `ceil(h(G))`, with falling, flat, and rising inclination.
* **Dumping:** start at `floor(h(G))`, with falling, flat, and rising inclination.

The three inclination templates are pre-enumerated and rotated/reflected into
the requested travel direction. Runtime work consists only of choosing the
operation-specific integer start, translating the templates, pairing them with
the deterministic companion, and applying ordinary origin/useful-height
bounds. Every resulting candidate is independently tested; there is no
inclination-success inheritance.

An alternative mining start either above or below `ceil(h(G))` may pass every
local test. If it does, the successful natural connection implies at least one
pathable G in the cell whose elevation rounds upward to that alternative start.
That G will generate and test the profile. Dumping is the floor-rounded dual:
any viable alternative start is assigned to a pathable G whose elevation
rounds downward to it.

Thus each integer starting level is tested by the pathable G nodes for which
`ceil(h(G))` selects it for mining or `floor(h(G))` selects it for dumping.
Testing every reached G covers viable higher and lower starts without testing
them all at every G.

Area boundaries, blockers, route history, or first-success caching can prevent
the expected owning G from being evaluated. Limiting the current G to these six
profiles is therefore another deliberate completeness tradeoff, not a proof
that other placements are impossible. Do not fall back to a broader height
scan.

## Deterministic future-pathability proof

### Why the existing G mask is insufficient

The G graph proves that the resolved vehicle mask is pathable **before** the
candidate designation is worked. That fact cannot be reused as a post-work
shortcut. Mining, dumping, or leveling may alter an overlapping tile between
G and the future V face and create a blocking ledge even though the same tile
was pathable from G in the snapshot.

The replacement must prove future pathability in the relevant direction: from
the candidate V face back to the already reached G center. It is still a fixed
set of parallel cardinal walks, not a BFS.

### Establish the future V face

Enumerate every vehicle-mask-wide face tile required by the primary and
companion profiles. For each face tile `F`:

1. Determine which candidate designation owns `F`.
2. Bilinearly evaluate its target height `hV(F)`.
3. Project the terrain height after that candidate operation has worked `F`.
4. Require:

   `abs(hPostWork(F) - hV(F)) <= 0.25`

The `0.25` tolerance is intentional. A maximally inclined generated profile
changes its target by 0.25 per tile, so a face tile within this tolerance is
compatible with continued travel on the V profile.

### Walk cardinally from the V face to G

From every required face tile, walk one tile at a time opposite the proposed
travel direction until reaching G's coordinate on the travel axis. In the
canonical X+ case, each walk is X- and retains its transverse coordinate.

For every consecutive pair `(previous, current)` on every walk:

1. Project both terrain heights after the complete primary/companion candidate
   has done its work. A tile outside candidate work retains its captured
   terrain height.
2. Require:

   `abs(hPostWork(previous) - hPostWork(current)) <= 0.5`

All parallel walks must reach the G-aligned end of the bridge. Together they
cover the resolved vehicle width and terminate in the previously proven G
mask. This check intentionally includes any overlapping mask tiles: their
pre-work pathability does not prove their post-work height after the candidate
designation changes them.

There is no "already pathable" shortcut for an intermediate bridge tile. The
proof is the complete sequence of post-work deltas from the compatible V face
to G.

### Props

For tiles whose projected bridge state depends on candidate work:

* dumping rejects a non-tree prop blocker unless the candidate target at the
  prop's exact captured position rises strictly beyond that prop's scaled
  burial threshold (normally `0.5`);
* mining and leveling ignore props for this future-pathability proof.

The burial test is per prop, not per occupied footprint tile. A terrain change
to any tile occupied by the prop causes vanilla to reconsider it, but vanilla
then samples the finished terrain once at the prop's floating-point position.
Every distinct prop blocking the vehicle-width bridge must therefore pass its
own exact-position threshold; the whole footprint does not need `> 0.5` fill.

Other hard blockers and ordinary candidate feasibility checks remain in
force. The prop exception is limited to this operation-aware projected bridge
test and must not weaken unrelated building, area, durability, designation,
or history rules.

### Successful seam

When every V-face sample and every cardinal bridge step passes, synthesize a
one-row G-to-V handoff candidate:

* `GroundEntryCenters` contains the current G;
* the primary and companion operations are uniform;
* escape/continuation centers describe the deterministic V-face-to-G walks;
* cleanup ownership is carried exactly once; and
* the center-spoke/traversal charge uses the existing G/V cost model.

The resulting V state still passes ordinary transition feasibility, immutable
history, disturbance-ray, cleanup, useful-height, cost-limit, and best-label
rules. Subsequent V cells are validated by normal V expansion.

Materialization replay must independently reconstruct the same companion,
profiles, future face, post-work cardinal walks, prop rule, `0.25` face
tolerance, and `0.5` step limit. A search-only proof is insufficient.

## Positive paired-profile cache

Every-G admission creates substantial duplication. Smooth terrain commonly
reaches a direct-level-compatible G before other centers can propose the same
paired profiles. On rough terrain, several G centers can also derive the same
band, profiles, and entry orientation while each would otherwise repeat the
face-to-G proof.

Use a request-local, success-only ownership cache keyed by the concrete
resulting V state:

* primary/companion band anchor;
* both lane profiles; and
* V entry direction.

The G center and handoff operation are deliberately absent. Mining, dumping,
and leveling candidates that produce the same paired V state compete for one
first successful owner. A direct leveling success can therefore suppress all
later rough work for those profiles.

Insert a key only after the candidate has passed its complete transition,
history, work, bridge, cleanup, ray, cost, and route-emission checks. A mere
geometric match or face-to-G proof is not sufficient. Do not cache failures:
another G or route history may make the same profiles viable.

On a positive hit, perform no profile feasibility, future-face, cardinal-walk,
prop, cleanup, or handoff evaluation for that key. The previously emitted V
state remains the sole owner. The cache is discarded with the search request
and must never survive a snapshot refresh.

## Removed reverse-handoff work

For G-to-V candidates, remove or bypass:

* canonical-face-only direction filtering for rough terrain;
* the two possible transverse companion anchors;
* loose live height-window scans;
* singleton floor/ceiling placement assumptions;
* recursive seed extension through up to four V rows;
* lane-contact pair search used only to discover an escape route;
* post-work corridor-center BFS;
* local escape BFS; and
* negative-result caching.

Shared helpers may remain for V-to-G. Diagnostics must distinguish the
unchanged forward evaluator from the new deterministic reverse proof.

## Correctness and completeness

The deterministic proof is intended to be sound: an accepted candidate has a
concrete straight, vehicle-width continuation after its proposed work.

It is intentionally incomplete. The removed BFS could occasionally find an
unusual lateral or winding route inside the handoff cells when every direct
face-to-G walk fails. The operation-specific start rule can also omit a profile
when no reachable G has the `ceil` or `floor` value that would select it. Such
routes are expected to be rare, fragile under continued V work, and unlikely
to be the only solution after every reachable G has been tested. Do not fall
back to broader height scans or BFS after rejection; doing so would restore
the rough-terrain cost this design removes.

The existing limitation of one best label per concrete G or V state remains.
The search does not retain every G history merely because later work could
invalidate the cheapest history's approach.

The positive cache strengthens this into explicit first-success ownership for
G-to-V. A later cheaper G route, or one whose history would survive subsequent
V work better, is not evaluated after the key is cached. This can lose a route,
but retaining all alternative G histories would recreate the state explosion
the optimization is intended to avoid. This tradeoff is accepted.

## Implementation sequence

1. Add pure companion-side derivation and exhaustive residue fixtures.
2. Add the six pure rough candidate templates and start-level fixtures.
3. Add vehicle-width future-face and parallel face-to-G walk geometry.
4. Add the operation-aware post-work face, step-delta, and prop predicates.
5. Add deterministic one-row seam synthesis and replay validation.
6. Add request-local positive ownership keyed by the paired V profile state.
7. Route rough G-to-V through the new proof while retaining direct leveling.
8. Remove reverse calls into corridor/local-escape BFS and obsolete monotonic
   proof reuse.
9. Split diagnostics for rough candidates, cache ownership, bridge
   tiles/rejections, direct
   leveling, and any accidental reverse BFS invocation.
10. Measure T1 and T3 on the standard rough Cluster 2 save.

## Fixtures

### Ordering and authority

* Outside-area G performs no candidate lookup or terrain projection.
* Ground-goal handling retains its existing precedence.
* Managed primary plus unmanaged companion is rejected before profile work.

### Companion residues

For all sixteen canonical `(x, y)` residue pairs, all four directions, and
positive and negative absolute coordinates, assert the exact companion side
tables above and exactly one paired anchor.

### Candidate generation

For integer and fractional positive and negative G heights, and under every
travel direction, assert exactly:

* three mining inclinations starting at `ceil(h(G))`; and
* three dumping inclinations starting at `floor(h(G))`.

Assert rotation/reflection equivalence, valid integer corners, primary/
companion continuity, and no additional vertical translations.

### Every-G behavior

* Exercise rough handoffs from all four longitudinal residues.
* At integral G height, assert that mining and dumping share the same start but
  remain distinct operations with all three inclinations.
* For both mining and dumping, demonstrate that alternative starts above and
  below the current rounded start are omitted at this G and generated by the
  pathable G whose `ceil` or `floor` selects that start.
* Prove a seam where the current G is pathable, the next inward tile is not,
  and the proposed mining designation creates a valid face-to-G bridge.
* Reject the same geometry when the candidate operation does not work that
  tile sufficiently.

### Face-to-G geometry

* Assert the exact future-face tiles and parallel cardinal walks under every
  direction and companion side.
* Include bridge tiles that overlap the current G mask and prove that
  prospective work can make such an overlap fail despite pre-work G
  pathability.
* Check the face `0.25` boundary inclusively and reject the next representable
  value beyond it.
* Check the per-step `0.5` boundary inclusively and reject the next
  representable value beyond it, including a ledge immediately before G.
* Cover profile interpolation on both primary and companion tiles.

### Props and blockers

* Dumping rejects a bridge non-tree prop at or below its exact-position scaled
  burial threshold and accepts the next representable value above it.
* Mining and leveling ignore the same prop in this proof.
* Buildings, unmanaged origins, opposing work, durability, and history remain
  rejected by their ordinary stages.

### No reverse BFS

* Rough and level G-to-V successes perform zero corridor/local-escape BFS pops.
* A fixture with only a winding internal escape is deliberately rejected.
* Equivalent V-to-G fixtures still invoke and validate the existing corridor
  behavior.

### Positive cache

* A direct-level success prevents a later rough candidate from evaluating the
  same paired V state.
* Several rough-capable G centers perform the face-to-G proof only until the
  first success for that key.
* A failed candidate does not suppress a later success from another G.
* Different anchors, either lane profile, or entry directions do not collide.
* A new request or snapshot starts with an empty cache.
* A deliberately cheaper later G route is skipped, documenting first-success
  ownership rather than accidental cost dominance.

### Replay

Every accepted deterministic seam must reproduce its profiles, companion,
future face, bridge walks, cleanup, operations, and cost during materialization
replay. Mutating any required face or bridge sample in the replay snapshot
must reject the plan.

## Diagnostics and live validation

Add counters for:

* G nodes rejected by the tower-area gate;
* rough-candidate sets and emitted profiles;
* deterministic companion selections;
* direct leveling accepts;
* positive-cache insertions and hits, split by the skipped leveling or rough
  candidate kind;
* future-face tiles and cardinal bridge steps checked;
* face-height, step-delta, prop, and ordinary-feasibility bridge rejections;
* deterministic rough accepts; and
* reverse corridor/local-escape BFS calls, expected to remain zero.

For the standard rough-ground Cluster 2 save, record search time, visited G/V
states, candidates per G, transition time, handoff time, bridge-check time,
selected route/cost, materialized profiles, and post-placement Mega
pathability. Compare the selected route with the current implementation, but
do not require route equivalence: the new proof deliberately rejects rare
BFS-only seams.

## Expected outcome

Level terrain continues to use the cheap paired leveling proof. Rough terrain
tests a small precomputed profile set at every authorized G, validates one
deterministic vehicle-width V-face-to-G bridge, and immediately resumes
ordinary V expansion. The first successful paired profile state suppresses all
later G-to-V work for the same state. Reverse handoff cost becomes fixed per
uncached candidate instead of scaling with an internal BFS domain, while V-to-G
correctness and behavior remain unchanged.
