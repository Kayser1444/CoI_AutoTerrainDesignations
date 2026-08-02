# Adaptive Cut/Fill Side-Ray Traversal

Status: proposed. Do not implement until the adaptive traversal, dense
blocker coverage, and material-band assumptions are covered by deterministic
fixtures.

Drafted: 2026-08-02

Related design:

* [Accessway side-ray landscaping cost](../done/accessway-pathfinding-side-ray-cost.md)

## Problem

Cut and fill side rays currently advance one tile at a time. Cost integration
is sampled at fixed distances (`1, 2, 3, 5, 8, 13, 16`), but terrain,
material, blocker, and termination checks still follow the dense ray path.
For a large height delta this spends work on early tiles that are very
unlikely to be the contact point.

The ray should retain dense protection against small buildings and other
clearance hazards while reducing expensive terrain-contact evaluation.

## Goal

Make cut/fill rays use adaptive terrain probes driven by the remaining signed
height delta, while preserving the current ray semantics for blockers,
disturbed tiles, safety tails, unresolved rays, map edges, ocean, and route
scoring.

## Design

### Adaptive contact probes

For the current material slice, calculate the signed gap at the last terrain
probe:

```text
cut:  gap = terrainHeight - projectedRayHeight
fill: gap = projectedRayHeight - terrainHeight
```

While `gap > 0`, estimate the next contact distance from the local material
slope:

```text
estimatedAdvance = ceil(gap / materialSlope)
```

Advance outward by at least one tile and clamp the probe to the next predicted
material-band boundary and the configured maximum ray distance. Probe there,
recompute the gap, and repeat.

The accepted approximate contact must have crossed terrain but remain close to
the crossing:

```text
-5 < gap < 0
```

If a probe overshoots below `-5`, retain the previous positive-gap probe and
the negative-gap probe as a bracket. Interpolate the crossing distance and
probe inward until the accepted band is reached or the bracket is one tile
wide. Use the interpolated contact distance/height for the resulting
cut/fill footprint; do not continue outward after an excessive overshoot.

### Material bands

Do not identify material changes densely along the ray. Instead, inspect the
material column at the ray origin and treat its ordered material bands as
laterally flat for prediction. For example, a column containing a rock band
followed by a dirt band predicts a rock-to-dirt boundary at a corresponding
projected ray height.

Use the current material's slope to predict the next contact or material-band
intersection. When a probe reaches a new material slice, switch to that slice's
material and slope and continue adaptive traversal from the observed point.
If the observed slice differs from the prediction, continue from the observed
slice; do not revert the entire ray to dense material evaluation.

### Dense blocker and disruption coverage

Adaptive terrain probes must not create blind corridors. Between the previous
probe and the accepted contact, inspect every intervening tile for the
existing operation-specific blockers, including:

* buildings and their configured safety footprint when building avoidance is
  enabled;
* ocean for cuts when ocean avoidance is enabled;
* map bounds and existing designation/projected-work blockers according to the
  current side-ray rules.

The dense blocker pass is specifically required to detect small buildings;
adaptive stepping must not replace it with sparse blocker samples.

Mark every tile through the accepted contact as disturbed using the ray's
linearly interpolated projected ground level at that tile. Preserve the
existing distinction between work distances and safety-only distances, and
apply the configured post-contact safety tail. The dense pass should not
require dense terrain-height reads merely to emit these approximate levels.

### Existing semantics to preserve

The change must not alter:

* straight cardinal lateral ray geometry or the independent left/right rays;
* cut rays rising by material slope and fill rays falling by dumping slope;
* operation-specific map-edge behavior;
* forbidden-ocean behavior for cuts and fill map-edge behavior;
* unresolved-ray penalties and maximum ray-cost handling;
* projected ray constraints, clearance expansion, and safety-tail semantics;
* V2 strafe geometry, where lateral movement remains transition metadata.

## Non-goals

* Do not change route scoring or the public ray-distance settings in the first
  implementation.
* Do not perform a dense terrain/material read at every lateral tile.
* Do not use adaptive stepping to bypass blocker, clearance, or disruption
  coverage.
* Do not make material bands globally flat; the flatness assumption is local
  to the origin column and each newly observed material slice.

## Acceptance criteria

1. A large origin delta takes an adaptive probe near the predicted flat-terrain
   contact distance rather than visiting every terrain sample first.
2. A positive residual gap causes another outward adaptive advance.
3. A negative residual gap within `(-5, 0)` is accepted as approximate contact.
4. An overshoot below `-5` is bracketed and corrected inward by interpolation;
   the ray does not accept the excessive overshoot or continue farther outward.
5. Every tile between the origin and contact is checked for enabled building
   and ocean blockers, with small buildings detected even when terrain probes
   skip over them.
6. Every traversed tile receives the appropriate approximate projected ground
   level and cut/fill work classification, with no work emitted beyond contact
   except the configured safety tail.
7. A predicted rock-to-dirt or dirt-to-rock boundary switches traversal to the
   newly observed material slope without dense material scanning.
8. A mismatched prediction continues from the material slice actually observed
   at the probe.
9. Map-edge, ocean, blocker, unresolved-ray, cost-cap, and safety-tail
   behavior remains compatible with the existing implementation.
10. Existing V1 and V2 side-ray fixtures remain green.

## Required fixtures and diagnostics

Add deterministic fixtures for:

* flat terrain with a large cut/fill delta;
* a residual positive gap after the first predicted contact probe;
* an accepted negative gap in `(-5, 0)`;
* excessive overshoot followed by inward interpolation;
* a flat rock-to-dirt material-band transition;
* a mismatched material-band prediction that continues from the observed
  slice;
* a small building located inside a skipped adaptive interval;
* dense ocean/blocker rejection inside a skipped interval;
* approximate projected heights and work/safety masks through contact;
* an unresolved ray that reaches the maximum distance without contact.

At debug/trace diagnostic levels, report adaptive probe count, dense blocker
tiles checked, material-slice transitions, predicted versus accepted contact
distance, and interpolation corrections. Avoid per-tile logging in normal
operation.

