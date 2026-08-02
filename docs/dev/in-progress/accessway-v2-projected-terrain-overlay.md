# Accessway V2 projected-terrain overlay

Status: stage two implemented; in-game A*/Dijkstra validation pending.

## Objective

Replace V2's chronological ray-history queries with a persistent spatial model
without changing the currently active search rules in the first rollout stage.
Then activate the refined projected-terrain rules separately so performance
and route changes remain attributable.

The immediate target is the two-lane T3/Mega model. The concepts may later be
generalized to the single-lane T1/T2 accessways, but the implementation must
not introduce abstractions needed only by that future work.

## Model

One request has two projected-terrain layers:

1. The immutable snapshot layer produced by existing terrain designations.
2. A persistent path-local layer produced by accepted generated V work.

The captured physical terrain remains immutable. A query combines it with the
two projection layers; no search label mutates the request snapshot.

Each projected tile can independently contain:

- a cut work surface, collapsed to the lowest projected height;
- a fill work surface, collapsed to the highest projected height;
- a cut safety-only exclusion;
- a fill safety-only exclusion; and
- disruption, derived from any work or safety effect and therefore unavailable
  to G.

Opposing existing cut and fill projections on one tile are ambiguous and are a
hard blocker for generated work. Candidate history never creates a new
opposing overlap because the second operation is rejected.

## Ray queries

For an incoming cut ray at height `M` and an earlier cut work surface `N`:

- `M >= N` resolves the ray;
- `M < N` charges only `N - M`, retains the deeper projected cut, and continues.

Fill is the mirror: a ray at or below the earlier fill surface resolves, while
a higher ray charges only the additional fill and continues. Static blockers
remain authoritative until resolution.

Resolution against projected work uses the ordinary post-termination safety
buffer. The buffer adds no cost and creates no projected height. Its tiles are
safety-only: a later same-sort ray may cross them, but opposing work is fatal.

## Generated profile queries

A non-exempt generated profile may match or extend a same-operation projected
work surface. Reversing the operation is fatal. Compatible direct work receives
credit for the already projected portion and pays only the additional gap.

A non-exempt generated profile intersecting a safety-only span must continue
the same operation relative to physical terrain: a demonstrable cut may enter
cut safety and a demonstrable fill may enter fill safety. Leveling or reversing
the operation remains fatal because the heightless span cannot prove stability.

The connected generated continuation retains a clearance waiver against the
complete compatible cardinally connected fixed predecessor structure. This
ownership must survive the first fringe step and replay/materialization,
because the predecessor structure remains safe by the established physical
connection. The predecessor's projected work remains persistent and still
participates in work credit and height-conflict validation; only its
safety-only exclusion is waived. Disconnected fixed structures remain
authoritative blockers.

## Self-disruption

The currently emitting V band and its complete immediately connected
predecessor structure are safe by connection geometry and game mechanics.

An uninterrupted generated V segment using no more than two longitudinal
cardinal directions is safe from its own projected disruption. Strafes preserve
the longitudinal direction, but must emit rays from both endpoints of the newly
exposed predecessor-outer rear face as well as its lateral outer face. A
direction-introducing turn emits its exterior ray in the old direction, so
stricter checks begin only with later rays emitted in the third direction.

After a third direction, each new profile is checked against established
projected terrain and each new ray is checked against established non-exempt
origins. Previously accepted effects are never re-audited. Entering G or FV
ends the direction-safety scope; all projected terrain remains route-wide.

## Persistent representation

Each history node stores a delta collapsed by tile, operation, and the minimal
owner provenance required by the current immediate-successor waiver. Cut
constraints retain their minimum height and fill constraints their maximum.

Tile lookup walks parent deltas only on a cache miss and memoizes the merged
answer for that history and owner-exclusion context. This makes repeat queries
constant-time and changes first-query work from total constraint count to
history depth. The initial implementation records cache hits, misses, parent
steps, raw constraints, and collapsed delta tiles. A persistent spatial tree or
periodic checkpoints remain optional follow-ups if measurements show that
first-query depth is still material.

## Rollout

### Stage one: representation parity

- Build collapsed per-transition ray deltas.
- Replace `HasRayAt` and profile-envelope history scans with memoized spatial
  queries.
- Preserve presence-based same-sort termination, current safety-span storage,
  owner waivers, feasibility results, costs, and route selection.
- Require fixture parity and compare Cluster 2 transition time, cache behavior,
  and collapsed tile counts.

### Stage two: refined semantics

- Split projected-work spans from safety-only spans.
- Make same-sort termination height-aware.
- Resolve immutable FV boundary rays against earlier same-sort projected work
  with the same height-aware rule as generated rays.
- Apply the normal safety buffer after projected-surface resolution.
- Use immutable and path-local projections uniformly for work credit.
- Reject ambiguous opposing projections and enforce the self-disruption rules
  above.
- Require A* and Dijkstra agreement under the new rules before heuristic work
  resumes.

## Acceptance evidence

Stage one must preserve Cluster 2's route, cost, visited-state count, expansion
counts, and rejection behavior apart from added diagnostics. Wall time is noisy;
transition-evaluation time is the primary performance comparison.

Stage two may intentionally change route cost and search breadth. Its acceptance
depends on focused geometry fixtures, replay agreement, A*/Dijkstra agreement,
and in-game validation of the selected designations.

## Stage-one Cluster 2 baseline

The first in-game rerun on 2026-08-01 preserved the complete search result:

- cost `3841.71`;
- `56,045` visited states;
- expansions `G=17,960`, `V=38,084`;
- `21` route states and `2,540` pending labels; and
- maximum route history of `71` origins and `9,450` raw ray constraints.

Against the immediately preceding pre-index run, wall search time fell from
`89,014.23 ms` to `43,827.16 ms` (50.8%), V expansion time from `59,791.33 ms`
to `31,644.57 ms` (47.1%), and transition evaluation from `37,146.82 ms` to
`7,636.63 ms` (79.4%). Frames fell from `1,575` to `716`; maximum individual
slice time remains noisy and is not an acceptance metric.

The new overlay diagnostics reported `2,509,402` cache hits, `2,597,190`
misses, `2,596,890` stored entries, and `59,974,791` parent steps. The observed
maximum history compressed `8,850` raw constraints to `2,940` owner-aware tile
entries, almost exactly 3:1. The roughly 49.1% cache-hit rate and 23.1 parent
steps per miss leave a measurable checkpoint/deeper-index opportunity, but the
remaining `7.64 s` transition cost no longer justifies complicating stage one
before the refined semantics are implemented.
