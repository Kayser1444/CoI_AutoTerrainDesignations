# Access Search Laboratory: Hazard Projection Extension

Status: draft for design grilling. This proposes extending the existing Access
Search Laboratory; it does not propose a separate laboratory, runner, corpus,
or capture workflow.

Related designs: [Access Search Laboratory](access-search-laboratory.md),
[mining disturbance visualization](mining-disturbance-visualization.md), and
[mining scan and replay laboratory](../in-progress/mining-scan-worker-laboratory.md).

## Intent

Extend the existing case recorder, exact-DLL replay runner, corpus, and reports
so a replay can reproduce both the ordered combined designation projection
and the access pathfinding result that consumes it. From that same projection,
derive the predicted mining-hazard area as a separately inspectable result.

The lab remains development-only. It diagnoses the terrain and designation
model; it does not simulate landslides or claim that the projected envelope
will equal the eventual in-game collapse.

## Agreed first milestone

Confirmed during design grilling on 2026-10-02: extend existing access replay
with inspection of rays generated during pathfinding, including the winning
route's predicted disturbance. Preserve the current calculations and canonical
route/plan results while exposing this evidence.

Start with existing access cases, which already replay candidate-ray scoring
from captured inputs. Broader terrain capture, projection-model corrections,
and unification with live-designation hazard projection follow investigation;
they are not prerequisites for this first milestone. The proposals below remain
unconfirmed future design and must be reconciled with the review findings.

## Current behavior and gap

An access capture currently gathers live designation and terrain facts, runs
`BuildProjectedDesignationDisturbedTilesSliced`, and stores projected cut,
fill, safety, and source coverage in `AccessSearchSnapshot`. The subsequent
path search consumes those projected results. The existing access replay
therefore replays pathfinding from the stored existing-designation projection
output. It does not re-run that prepass, but it does execute candidate rays
during pathfinding. Those search-generated rays are the first milestone's
inspection target.

## Agreed ray inspection view

Confirmed during design grilling: selecting a ray shows a side profile of
terrain height against the projected slope, with sampled materials labeled.
The view must distinguish recorded material facts from unavailable material
facts; existing cases must not gain invented labels. This view is intended to
explain where and why the current ray extends or terminates.

Confirmed during grilling: retain only the winning route's rays for the first
milestone. Do not retain rays from other explored candidates for inspection;
their data volume was rejected. Per-ray detail remains available for the
winning route through the agreed side-profile view.

Confirmed during grilling: the top-down route view also shades the predicted
disturbance tiles from the winning route's rays, exposing gaps and overlaps in
their combined coverage. This displays the disturbance computed by the search;
equivalence with the public live-designation hazard overlay remains to be
investigated.

Confirmed during grilling: shade safety-margin tiles differently from predicted
terrain-work tiles. Keep both contributions distinguishable where they overlap;
a safety contribution must not hide predicted work on the same tile.

Confirmed during grilling: cutting and filling disturbance use distinct colors
in the route view. Preserve their separate contributions where both affect a
tile, alongside the separate safety-margin classification.

Confirmed during grilling: existing cases remain usable when material facts at
intermediate ray tiles are absent. Display the available terrain profile and
ray behavior, and label missing material samples "not captured". Do not invent
material identities or require a fresh capture merely to inspect that case.

The access snapshot does not retain the complete ordered projection request or
all terrain/material samples the ray pass may read. Terrain columns outside
the access capture's coverage can still be read from the live terrain manager
during projection. The mining replay cases record full material columns for
mine planning, but their origins and terrain coverage are not the complete set
of live designations and ray samples used by an access projection.

The public hazard overlay currently invokes the combined projector separately
with Mining designations only, while access capture projects the active
designation set and clips stored results to the access request's relevant
terrain bounds. The lab must make the intended relationship explicit instead
of treating those two output sets as already interchangeable.

## Proposed replay boundary

```text
captured access request + ordered live designation inputs
    + captured ray terrain facts + resolved projection policy
    -> combined designation projection
       -> access disturbance and safety view
       -> predicted mining-hazard area
    -> access pathfinding and plan materialization
    -> canonical projection and access outcomes
```

The combined designation projection keeps its existing processing order and
shared projected-work behavior. Do not replace it with independently
projected designations unioned afterward.

The hazard view is derived from projected cut work caused by live Mining
designations. It excludes access-planning safety-only tails and speculative
candidate-route rays. The access view retains the cut, fill, and safety data
needed by pathfinding. Both views come from one recorded projection input and
one deterministic projection execution.

For a complete replay, the capture must retain enough terrain facts to run the
projection without consulting Unity or live Mafi objects. Those facts include
the precise sampled surface heights and material layers/slopes needed by the
ray and material-transition rules, plus physical map bounds and the exact
resolved policy values. Capture each required terrain column once even when
many rays sample it.

## Case and report behavior

- Add projection input and expected projection output to cases recorded by
  the existing lab. Keep the current exact built DLL, game-assembly identity,
  request metadata, inbox, corpus promotion, and runner workflow.
- Preserve the captured designation order and each designation's type,
  origin, and four corner target heights. Record the mining classification
  required to derive hazard coverage.
- Compare projected cut/fill coverage, safety-only coverage, and the
  Mining-cut hazard subset separately from the canonical access route and
  materialized plan. A projection change must be visible even if the selected
  route happens to remain the same.
- Add a top-down tile visualization of designation footprints, projected
  coverage, and hazard coverage. Provide ray-step diagnostics on demand,
  including sampled height/material, selected slope, any shallower-material
  restart, termination, and projected-work reads.
- Generate ray-step diagnostics from the replayed input by default rather
  than storing a verbose copy of every step in each case. Include aggregate
  diagnostics in the report and allow focused trace export for selected rays.
- Keep existing access cases valid for pathfinding-only replay. They lack the
  raw projection inputs required for full projection replay and must not be
  migrated by inventing terrain samples or designation state.
- Version the new projection input and its semantic policy explicitly. Reject
  incomplete or unsupported cases before replay, following the lab's existing
  fail-closed compatibility rules.

## Size and runtime constraints

The designation profiles are expected to be small relative to terrain facts.
The size risk is the unique set of terrain columns needed to replay rays beyond
the current access capture region. Avoid serializing the same column once per
ray, and reuse the existing primitive captured-layer representation where its
semantics match.

Before setting a case-size limit, measure on a small controlled case and a
representative dense map. Report unique sampled tile count, total material
layer count, uncompressed and compressed payload sizes, capture time, replay
time, and peak retained memory. Use bounded capture and fail closed if required
facts cannot be recorded. Ordinary unarmed access searches must not incur
recorder I/O or diagnostic-trace allocation.

## Validation sequence

1. Record one accepted access search and verify that replay regenerates the
   same projection output before it reproduces the same access result.
2. Use homogeneous terrain and simple designation shapes to validate ray
   starts, orthogonal/diagonal reach, designation footprint dimensions, and
   map-edge termination.
3. Add material transitions and overlapping designations; inspect order,
   slope changes, and shared projected-work reads.
4. Compare the replay's hazard subset with the public overlay generated from
   the same captured world state.
5. Keep post-simulation collapse comparison as in-game evidence; the replay
   validates deterministic model behavior, not physical truth.

## Deferred decision: projection coverage

An access search projects live designations across the physical map but stores
projected results only within that request's relevant terrain bounds. A full
hazard-area result may therefore require broader output coverage and more
terrain facts than a pathfinding-only case.

Decide coverage after inspecting the existing search-generated rays and
clarifying their relationship to the live-designation projector. A complete
projection followed by local filtering is not yet proven equivalent to the
current clipped, stateful projection; broader coverage must not be treated as
a behavior-preserving lab change without that evidence.
