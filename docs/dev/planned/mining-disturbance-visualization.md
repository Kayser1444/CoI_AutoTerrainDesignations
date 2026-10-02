# Mining disturbance visualization and hazard fence

Status: Stage 1 overlay and Stage 2 fence-point preview code implemented;
awaiting in-game visual validation and marker-spacing tuning. Terrain-disruption
refreshes have been removed, and a temporary 30 ms cooperative-slice build is
ready for in-game validation. Mining-only action batching and worker/cache
changes remain pending. Core concept decisions confirmed on 2026-09-27. Stage 3
fence remains planned.

## Summary

Show the land ATD predicts may be affected by collapse from live mining work.
The feature has three separate visualizations and three separate toggles:

1. **Hazard overlay** — a public, terrain-following yellow-and-black striped
   overlay on the predicted collapse envelope. It is shown only while the
   game's existing terrain-designation overlay is visible.
2. **Fence-point preview** — a developer-only view of proposed post positions
   as orange dots. This exists only to tune and debug the fence placement.
3. **Hazard fence** — a public, non-interactive world visual following the
   exact same hazard area and post positions.

The overlay and fence are independent public options. The post preview has its
own developer-only toggle and is absent from the player-facing settings. Any
combination may be enabled while developing or reviewing the feature.

The visualization describes ATD's **predicted collapse envelope**. It is a
geometric prediction based on the terrain/material slope model and configured
safety assumptions, not a probability estimate or physics simulation. The
visuals themselves are read-only; the shared mining-cut projection also feeds
accessway planning and disrupted-tree selection, so changing that model can
change those predictions.

## Motivation

Mining designations can put nearby ground at risk of collapse. ATD already
uses material collapse slopes and safety buffers when predicting this risk,
but the affected area is not as easy to see as the mining work itself. A
terrain overlay can make the predicted envelope legible; a physical fence can
later present the same boundary as part of the world.

ATD has mining safety calculations in
[`MiningSafety.cs`](../../../src/Mining/MiningSafety.cs) and uses projected
terrain effects when selecting trees that may be disrupted in
[`ATD.Scan.cs`](../../../src/ATD.Scan.cs). The overlay and fence should be
based on the existing collapse-safety model, not the tree-harvesting tile set.
Expose that result as a stable, read-only runtime value so renderers do not
depend on mining execution or access-search presentation code.

## Goals

- Make ATD's predicted collapse envelope visible on demand.
- Let developers inspect the exact proposed fence-post locations before
  rendering the fence.
- Render the finished fence as a diegetic world object: terrain and buildings
  occlude it, and normal game UI renders above it.
- Keep visuals derived from the live world and safe when ATD is removed from a
  save.
- Keep all three visualizations independent so their geometry and appearance
  can be compared.

## Non-goals

- Simulating landslides, soil stability, or probabilistic damage.
- Changing mining-designation planning, safety checks, or execution.
- Making posts or fence spans interact with trucks, workers, or terrain work.
- Adding saveable ATD-owned props, entities, or prototypes.
- Replacing vanilla mining-designation coloring.

## Terminology and area definition

Use **predicted collapse envelope** for the tiles the current ATD mining
safety model identifies as potentially affected by collapse from live Mining
designations. Avoid presenting the envelope as a probability or guarantee of
collapse.

Derive the envelope from **all live Mining designations**, whether they were
placed by ATD or by the player. The predicted area is a function of current
terrain, materials, designations, and applicable ATD safety settings; it must
not require ATD ownership records. Merge overlapping envelopes before
rendering so the overlay and fence show one coherent area.

The overlay and fence must use exactly the same envelope. The fence follows
its perimeter without adding a visual-only safety margin.

## Toggles and presentation

| Toggle | Audience | Visualization | Proposed default |
| --- | --- | --- | --- |
| Show predicted collapse overlay | Player | Striped collapse-envelope tiles, gated by the existing terrain-designation overlay | Off |
| Show fence-point preview | Developer/tester | Orange dots at proposed post positions | Off |
| Show mining hazard fence | Player | World-space fence along the exact collapse-envelope perimeter | Off |

The public overlay and fence each have their own persisted player-facing
toggle. The overlay is shown only while both its ATD option and the game's
existing terrain-designation overlay are enabled. The fence has its own
independent toggle and remains visible with the designation overlay hidden.
Both public options are persisted; the fence is off by default.

The fence-point preview has its own persisted developer-only toggle, is off by
default, and does not appear in player settings or player-facing documentation.
Use `atd_mining_hazard_fence_preview` to toggle it in the current world, or pass
`on` / `off`; its value is saved with that world.

The overlay uses a subdued, semi-transparent diagonal hazard stripe pattern
that remains readable over varied terrain colors. It follows the terrain
surface and uses normal scene depth so terrain and buildings hide it. It must
not use screen-space GUI projection for the public visualization.

The overlay derives from projected cut tiles produced by the shared
material-aware mining projection. For mining cut rays, if an outward terrain
column exposes a material with a shallower (more mobile) cut slope, the ray is
backtracked and reprojected from its origin with that slope. The slope can only
become shallower as the ray advances, so a later harder material does not
steepen it again. This intentionally conservative rule is shared with
accessway disturbance planning and mining tree-harvest selection; it does not
change dumping/fill or leveling projections. The overlay includes all live
Mining designations and excludes access-planning safety-only tail tiles. Its
toggle is saved per world and defaults off; saving world settings as global
defaults also updates the default for new worlds.

Performance work must preserve the shared combined-projector behavior: rays
may read work projected by earlier rays in the same calculation. Do not replace
it with independent per-designation projections that are merely unioned unless
that modeling change is reviewed separately.

The fence is a lightweight boundary treatment: striped posts at sampled points
with a narrow warning tape or similarly light span between them. It has no
colliders and does not block movement. Render it in the world scene with
depth-tested materials and the game's normal world/UI ordering. Do not use
always-on-top gizmo materials for the public fence.

The developer preview draws an orange dot for every post anchor produced by
the placement algorithm. An x-ray or screen-space style is acceptable for this
diagnostic view if it helps inspect points hidden by terrain or buildings; it
must remain clearly distinct from the public fence.

## Placement model

1. Build the predicted collapse envelope from all current Mining designations
   using ATD's material slope and safety assumptions, including the
   conservative mining-cut material transition rule above.
2. Merge overlapping envelope tiles and split them into edge-connected regions.
3. Compute a convex hull from the tile corners of each region. This deliberately
   bridges inward bays and tile stair-steps so the preview follows a simplified
   outer rim; separate regions remain separate.
4. Sample post anchors along each hull edge, keeping hull corners and spacing
   intermediate posts around four tiles apart. Deduplicate shared anchors.
   The spacing is a named tuning value for the preview and future fence.
5. Place fence anchors along the simplified hulls on current terrain.
6. Render the overlay, preview dots, and fence from the same derived result.

Spacing should be a named tuning value. The first pass should favor a sparse
perimeter over outlining every tile; camera distance can reduce visual detail
or hide distant spans if needed. The initial implementation should avoid
per-post game entities and batch the render data where practical.

## Runtime and save lifecycle

All masks, perimeters, anchors, and render objects are runtime-only derived
state. Rebuild them from live terrain and designation state after world load;
do not serialize visual instances or require a saved ATD payload to restore
them. Destroy renderer objects and release generated meshes/materials when the
world ends or the renderer is disabled.

Persist visualization toggles, but never persist the derived hazard coverage or
worker cache. Recompute after load only when at least one hazard visualization
is enabled. When all three visualization toggles are off, do not capture or
project hazard inputs; enabling a visualization requests a result if there is
no usable in-memory result.

### Agreed freshness and scheduling contract

- Only Mining designation edits schedule hazard recomputation. Terrain and
  material changes on their own do not dirty the cache or start a refresh. The
  predicted mining-hazard area may therefore remain stale after excavation
  until a later Mining designation edit or until the visualization is enabled
  without a usable result.
- A designation-triggered job captures the then-current terrain. It may
  discover that cached projection dependencies changed, but terrain changes
  alone never schedule that job.
- A single-tile player action produces one update. A multi-tile drag pattern is
  one logical action and produces one update, not one job per tile event.
- Addition feedback should normally appear within 1–2 seconds. Removal and
  correction of stale coverage may take several seconds.
- A newer Mining designation action supersedes an obsolete projection job.
  The last successfully completed visualization stays visible while the newest
  result is being prepared; a cancelled or failed job never replaces it.
- The projection remains one combined, order-dependent pass over all live
  Mining designations in the current designation collection order. Later rays
  may read work projected by earlier rays. Do not sort the inputs or compute
  independent per-designation envelopes and union them as a performance
  shortcut.

### Agreed performance direction and optional worker/cache follow-up

The current implementation advances the projection coroutine from render
updates and also refreshes during terrain disruption. That behavior is being
replaced. In the 2026-09-27 game log, a case with 6,276 live Mining
designations and 111,258 hazard tiles took 38.6 seconds for the initial
rebuild, then about 40 seconds for each repeated terrain-disruption rebuild.
The repeated terrain-triggered work is not wanted under the freshness contract
above.

The next performance step is deliberately smaller than a worker migration:
remove unnecessary refreshes, then test cooperative slice budgets in the game.
The previous DLL used a nominal 2 ms projection budget per render update. At 30
FPS that allows about 60 ms of projection work per second (6% of one thread),
so a large result can take much longer to publish. The temporary trial build
uses 30 ms, which allows up to 900 ms per second (90%) at 30 FPS and leaves
about 3 ms for the rest of that frame. It may produce visible frame-time
spikes, but is a useful high-throughput comparison. Do not treat either budget
as validated until the same map is measured in game.

Older logs report about 38.6 seconds for an initial large-map rebuild and
roughly 40 seconds for repeated terrain-disruption rebuilds, but those entries
are elapsed completion times, not isolated CPU timings. The newer detailed log
for a four-designation case reports 112 ms of projection work across 52 slices
and 1.094 seconds from start to completion. The latest large-map session has a
visibility-on entry but no completion record as of its last log write; that is
consistent with delayed completion, not proof of it. Use `projectionMs`, slice
count, `wallMs`, and observed frame rate from the same run when comparing
budgets.

Cooperative slicing tests the frame-time/completion-time tradeoff; it does not
reduce total CPU work. A full rebuild may still miss the 1–2 second addition
target, so safe cache reuse is needed for that target. If 2 ms delays the result
too long and 30 ms hurts responsiveness, move the expensive projection to a
worker and use the cache to limit how much work a designation edit repeats.

If a worker is needed, the redesign will:

1. Capture the ordered Mining designation inputs and the terrain facts needed
   by the projector on the game thread, using bounded work per frame where
   capture itself is large.
2. Pass only an immutable, data-only snapshot and immutable projection policy
   to a background worker. Projection code on that worker must not access live
   Mafi or Unity objects, invoke callbacks, log, mutate terrain, or touch UI.
3. Keep a latest-job-wins lifecycle with cooperative cancellation checkpoints.
   A stale job loses authority to publish; the controller never creates an
   unbounded `Task.Run` for each edit.
4. Keep an in-memory combined-result cache for the current world. On a
   designation-triggered capture, reuse only work whose ordered inputs and
   required terrain dependencies still match; replay from a safe
   combined-projector checkpoint through the affected suffix. Checkpoint
   spacing and retained memory must be chosen from measured cost; a cache hit
   must never change projector ordering or semantics.
5. Return immutable tile coverage and fence-point data to the game thread.
   Create and swap Unity meshes only on the game thread, in bounded batches.
   Replace the visible result only after the newest job completes successfully.

The existing `AccessSearchWorker` and its accepted one-worker/data-only
boundary are the first integration point to evaluate. Hazard projection is
advisory, so it must not block the game thread or displace an authoritative
mining/access request without an explicit scheduling policy. Before choosing
the final job arbitration, verify how a hazard request behaves while that
worker is occupied and measure whether it can meet the 1–2 second addition
target without causing CPU contention.

### Implementation plan

1. **Stop unnecessary invalidations.** Remove terrain-disruption polling as a
   hazard refresh trigger. Track only successful Mining designation adds and
   removals while the visualization is requested, and skip projection while
   all toggles are off. `AddTerrainDesignationsCmd` and
   `RemoveDesignationsCmd` carry arrays processed as one manager action; verify
   that the UI submits one command per drag gesture and use that batch boundary
   so tile events do not launch separate jobs.
2. **Validate cooperative slices.** After removing terrain-triggered refreshes,
   compare the existing 2 ms budget with the temporary 30 ms budget on the same
   small and 6,276-designation maps. Measure actual maximum per-update
   projection time, render-application time, total elapsed time, completion
   latency, and observed frame rate. Treat 30 ms as an experiment: at 30 FPS it
   can use 90% of the main thread. Choose a worker/cache path if no cooperative
   budget gives both acceptable responsiveness and timely output.
3. **Extract a pure projection request if needed.** Define immutable ordered designation
   and terrain/material snapshot records, captured on the game thread. Refactor
   the combined projector to consume those records without live manager calls,
   while preserving the existing result for identical captured inputs.
4. **Add worker ownership and cancellation if needed.** Submit one current hazard request
   through the shared worker infrastructure (or a measured, compatible worker
   lane if arbitration requires it). Carry world generation and job identity;
   poll completion without waiting; cooperatively abandon superseded jobs.
5. **Add safe cache reuse if needed.** Record enough ordered-prefix checkpoints and
   dependency information to resume only where the combined result can no
   longer be reused. Start with correctness-preserving invalidation, then tune
   checkpoint granularity against memory use and addition/removal latency.
6. **Publish and render worker results.** Accept only the newest successful terminal
   result. Keep the last completed visualization visible and perform mesh
   creation/swap on the game thread with bounded slices.
7. **Validate in game.** Compare the same large-map case and a small map before
   and after each change. Confirm that map loading and designation
   actions stay responsive, drag gestures submit one update, removals converge
   within the accepted several-second window, and terrain-only disruption does
   not schedule another projection. If the worker path is needed, also verify
   cancellation, cache correctness, memory use, and result handoff.

Do not relax the removable-save contract: the cache, snapshots, job handles,
and visuals are runtime-only and must be discarded on world teardown. If the
envelope cannot be recomputed, retain the last completed visualization and
report the failure through ATD diagnostics; never publish partial or
order-inconsistent output.

## Delivery stages

### Stage 1: public disturbance overlay

Implementation status: code is in place; in-game presentation and terrain
occlusion still need validation.

- Derive projected cut tiles for all live Mining designations.
- Add the persisted public overlay toggle and gate visibility on the existing
  terrain-designation overlay.
- Render striped tiles as runtime-only, chunked world meshes with normal scene
  depth testing.
- Check overlap, material slopes, cliffs, map edges, paused simulation, and
  large mines.

### Stage 2: developer fence-point preview

Implementation status: code is in place; marker density, terrain seating, and
visibility need in-game validation.

- Compute a convex hull per edge-connected hazard region and sample orange
  post anchors along it, preserving hull corners and keeping gaps to about four
  tiles. This intentionally omits concave boundary details and tile stair-steps.
- Add the independent, persisted developer-only preview toggle through the
  `atd_mining_hazard_fence_preview` console command.
- Draw orange markers in one batched world mesh at the exact anchors intended
  for the finished fence.
- Check corners, narrow corridors, overlapping envelopes, access routes, and
  terrain-height changes before authoring the final visual.

### Stage 3: public world fence

- Add the independent public fence toggle, persisted and off by default.
- Render posts and lightweight spans along the validated envelope perimeter.
- Verify world depth occlusion, UI ordering, terrain seating, camera-distance
  readability, and render cost in game.
- Keep the preview available for development, but exclude it from player
  settings and ordinary player-facing documentation.

## Validation criteria

- The overlay covers the intended predicted collapse envelope and no longer
  shows stale geometry after designations change or are removed.
- Overlapping designation envelopes produce one clean region and one fence
  perimeter, without duplicate posts.
- Orange preview dots match the finished fence post anchors exactly.
- The overlay follows the existing terrain-designation overlay and its own
  persisted option; the fence has an independent persisted option and remains
  visible when the designation overlay is hidden.
- The developer preview has its own persisted toggle and is disabled by
  default.
- Public visuals are depth-tested against terrain and buildings and render
  underneath game UI windows.
- Visuals do not create saved entities, alter pathfinding, or affect terrain
  work. A save/load cycle and loading without ATD leave no missing-prototype or
  stale-visual state.
- Additional Mining designations remain allowed inside the predicted envelope;
  this feature only visualizes risk.
- Dense and distant regions remain readable without an excessive render cost.

## Remaining design decisions

- Choose the exact lightweight fence treatment, such as striped posts with
  warning tape between them.
- Confirm the exact envelope boundary and presentation in game; the overlay
  defaults off.
- Confirm the player-facing settings surface and persistent storage location
  for the future public fence toggle.
- Select the renderer/decal path that provides reliable terrain depth behavior
  in the current game version.
