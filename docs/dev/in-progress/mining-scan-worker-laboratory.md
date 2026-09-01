# Mining scanning, worker execution, and laboratory

Status: the production mining worker, shared capture path, native batch commit,
replay laboratory, and initial ore-spike filter ship in v0.8.0. Further corpus
capture and laboratory tooling remain open.

Architecture decision: [Shared capture and worker for mining and access](../../adr/0007-share-planning-capture-and-worker-for-mining-and-access.md).

## Motivation

The maintainer reports very deep, narrow ore spikes in some vanilla-generated
maps. These can pull mine designation bottoms downward and cause excessive
rock excavation, particularly with ore quality enabled. Three captures now
show isolated deep-bottom anomalies, including a reported fresh, unrun map;
see the ore spike filter design and sample characterization reports. The
precise generator defect has not been diagnosed.

## Agreed scope

On 2026-08-31, the maintainer agreed that the first milestone will preserve
current mining results exactly, including the reported spike behavior, while
making the mining pipeline sliced, worker-capable, and replayable.

Capture will retain individual terrain columns before aggregation, and replay
will extend through final mine-designation geometry. This provides the detail
needed to investigate spikes and a baseline for comparing later filters.
Filter implementation and intentional changes to mining results follow this
baseline milestone; they are not part of the initial extraction.

The subsequent [ore spike filter](../done/ore-spike-filter.md) has a
world-settings toggle under **Vanilla fixes**, separate from ore quality. The
initial bedrock-neighborhood r4 policy is qualified for release; raw captures
remain intact so later parameter tuning can replay the same terrain.

## Agreed terrain capture detail

Each captured mining column will retain its coordinates, surface elevation,
bedrock boundary, and every material layer, including rock and dirt rather than
only selected ores. This deliberately accepts larger captures so experiments
can compare ore retained with waste excavated and change ore selection without
recapturing the terrain. Capturing these facts does not authorize a change to
the baseline planning algorithm.

The initial footprint includes all eligible designation cells and the exact
additional columns demanded by current safety checks. A future neighborhood
filter may require broader geographic coverage; missing facts fail closed.

## Agreed replay boundary

Mining replay ends at the mine excavation plan, before access-ramp planning.
It includes depth selection, filtering, connectivity, bottom flattening,
corner smoothing, and mining safety checks. Access planning remains in its
existing worker/laboratory, and actual designation placement requires in-game
verification.

The maintainer considers combining mining and access execution a time
optimization with no functional benefit for this work. Combining them is
outside this milestone. Both planners will share capture infrastructure and
one worker, without combining their planning algorithms.

## Implementation

- `ATD.PlanningCapture.cs` captures full primitive terrain columns. Thickness
  and cumulative native resource depth are recorded separately from float
  elevation endpoints to preserve the original mining arithmetic.
- `ATD.MiningExecution.cs` captures on the game thread with elapsed-time yields,
  then publishes sealed inputs to the existing single `AccessSearchWorker`.
  Three passes discover body geometry, direct safety, and exterior-ray coverage.
  The last pass produces the complete excavation plan. No live callback enters
  the worker and missing terrain never means empty space.
- `MiningPlanner` retains the old ore, geometry, and safety decisions. Original
  geometry/depth helpers remain temporarily as an independent fixture oracle.
- Create Designations has a FIFO of tower intents around the complete existing
  mine-then-access workflow. A new request replaces only the same tower's
  pending intent, retaining its queue position; a same-tower active request is
  superseded before submission. Each mining/access phase uses the existing
  manager and shared worker. The outer workflow queue avoids nesting a managed
  request that waits on another request in that same manager.
- Live placement prepares one immutable `AddTerrainDesignationsCmd` batch.
  Submission ends cancellation of that batch. Completion checks actual proto
  and corner data before claiming ownership; partial placement is reported,
  retained, and does not produce an accepted mining replay baseline.
- The laboratory has `caseKind=mining`, policy `mining-planning-v1`, independent
  captured inputs and canonical geometry, exact DLL/game identity checks,
  compressed files, candidate replay, audit, compute and codec benchmarks.
  Empty accepted plans can be recorded. Access expansion tracing is explicitly
  unsupported for mining; there is no additional viewer.

The common format currently stops at primitive column facts. Access calls the
same full-column collector and projects into its existing `AccessTerrainColumn`
representation; its serialized snapshot layout remains unchanged so existing
access cases still load. Mining retains all ore/bedrock facts in its case.
There is not yet a single serialized world envelope combining both request
shapes, nor capture-instance reuse across mine placement. A broader schema
migration would need explicit compatibility handling in the strict graph codec.

Capture consistency is deliberately observational across slices. Buildings
are copied at activation; columns are read once and retained. No environmental
rescan or final terrain-fingerprint comparison is added. World lifetime,
destroyed owners, changed area/ore/settings, and explicit request cancellation
remain authority checks. Existing building collection and collection sealing
still contain atomic work and need profiling; this is not a hard frame-time
bound. Safety coverage currently re-executes geometry up to three times.

Debug diagnostics separate terrain-read time and slowest column, estimated
retained bytes, worker computation per pass, and submission-to-observed-batch-
completion wall time. The latter includes scheduling/frame delay and is not a
measurement of the native processor alone.

## Using the mining laboratory

In a Release build, arm the existing one-shot console command before creating
fresh mining designations:

```text
atd_access_replay_arm ore-spike-01 ore-spike mining
```

The third argument defaults to `access`, preserving existing command usage.
The same cancel command, DLL archive, inbox, case extension, promotion and
runner conventions apply. Capture encoding runs after verified placement;
the manager toast can abort recording without undoing the submitted mine.
A newly recorded access or mining case includes the active map's name as
`mapName` manifest metadata. Legacy island maps use the registered map manager;
current world-region maps use the loaded-world config value. This does not
affect replay identity, and older cases remain valid without the field.
A no-op Create Designations action or an access-only repair does not consume
an armed mining capture. Use an explicit ore selection and a changed/cleared
mine when a fresh scan is required.

Replay with the manifest-pinned Release DLL using the existing runner:

```text
AccessV2FixtureRunner replay <ATD DLL> <CoI Managed directory> <case directory>
AccessV2FixtureRunner candidate-replay <candidate DLL> <CoI Managed directory> <case directory>
AccessV2FixtureRunner benchmark <candidate DLL> <CoI Managed directory> <case directory> 5
```

## Local evidence and remaining qualification

- Debug and Release builds succeeded with no warnings or errors.
- 180 synthetic combinations compare the extracted planner against unchanged
  legacy helpers across all five ore-quality levels, clearances, depth limits,
  flattening, irregular surfaces, empty columns and a narrow deep ore interval.
  Every input also survives the actual graph-codec round trip with exact output.
- Compressed mining-case fixtures use the production writer/reader and verify
  exact Release DLL identity, corrupted-input rejection and independent
  expected-geometry comparison. Safety demand coverage, missing-fact failure,
  worker equality, cancellation and sealed input isolation are exercised.
- Existing access/worker/manager/placement fixtures pass. The existing trivial
  access corpus case reproduced its canonical outcome exactly through the
  candidate Release DLL with matching game assemblies.
- The expensive access case was stopped after substantial memory pressure;
  the full existing corpus has not been requalified. Synthetic fixtures do not
  prove live capture correctness or native designation acceptance.

Before release, follow [the in-game checklist](../../test/mining-worker-laboratory.md).
Real spike cases, large-area timings (including paused operation), queue/Stop/
settings/world-reset behavior, native batch cost and full corpus qualification
are still required. This working build uses the worker path with no new player
switch; it is not a claim that the rollout qualification gate has passed.

## Agreed direction: shared capture infrastructure

The maintainer proposed a common mining/access snapshot format and one capture
process to maintain, providing a foundation for eventual combined mine-and-ramp
execution without combining the planners in this milestone.

There is existing overlap: access terrain columns retain layer elevations,
material identity, and slope. Mining also needs exact resource quantities,
material-to-product mapping, and explicit bedrock information. Access inputs
add navigation, vehicle, designation, obstruction, and cleanup facts; its
current snapshot also contains derived search structures.

The maintainer accepted a parameterized common capture pipeline provided it
does not become a large design project. Terrain is the expected dominant shared
input; the relative cost of building and other fact capture is not yet measured.
Keep planner requests, policies, derived workspaces, and outcomes separate.
Geographic coverage will be parameterized. The precise parameter set, optional
fact groups, and coverage contract remain to be settled; do not build a
general-purpose capture framework speculatively.

Both consumers will initially capture full terrain columns. Vertical truncation
is deferred until measurements justify it. Current access capture already
enumerates all terrain layers; layers are intervals rather than one record per
unit of depth, so depth alone does not determine capture size.

The maintainer identified the useful envelope as a possible basis for a future
cutoff, but noted that the envelope depends on parameters. A cutoff justified
for one parameter set would not automatically support later lab experiments
with different parameters. Full-column capture avoids that limitation. Any
future cutoff still needs to cover every required terrain query.

One capture implementation does not automatically mean one reusable capture
instance: the current mining flow places mine designations before requesting
access planning. Reuse across that boundary would require current-world
validation and handling changed designations, or a separately designed
projection of the mine plan.

## Agreed worker allocation

Mining and access jobs share one worker. Mining requests are always interactive.
There is no separate mining worker or concurrent mining/access execution.
Mining requests from different towers queue rather than cancelling each other.
Replacement is limited to requests for the same tower. The workflow intent
queue preserves tower order; active phases use the shared manager. This intentionally changes the existing global
Create Designations gate, where a request on tower B supersedes tower A's
operation. It does not change the planned geometry for unchanged inputs.

The implementation boundaries and outstanding live checks are recorded above.

Mining capture starts only when the manager activates the queued request, not
when the player submits it. Queued mining requests do not hold an early terrain
snapshot. This follows the existing access manager's deferred work-factory
pattern and avoids aging mining inputs through the ordinary queue wait.

## Mining environmental staleness

The maintainer rejected importing access's general finish-then-live-validate
and recapture policy into mining. Mining is expected to complete much faster,
and ore location largely fixes mine placement, so the added machinery is not
considered proportionate to the chance of an intervening obstruction. This is
a design trade-off, not a measured guarantee that the world cannot change.

Do not add a general environmental revalidation or automatic ore-rescan loop
for this milestone. Existing ore-selection and mining safety rules remain part
of planning against captured facts; this decision does not remove them. Rare
world changes after capture are an accepted limitation of that approach.
Access retains its existing environmental-staleness policy even though it
shares the capture format and worker with mining.

Cancellation, replaced requests, world/tower lifetime, and settings changes
are separate authority checks, as described above. Capture-at-activation avoids the ordinary queue wait but is not a
guarantee of zero delay between capture and placement.

## Laboratory presentation scope

No new visual inspector, cross-section viewer, or ore/mine-geometry viewer is
required for this milestone. The maintainer considers the in-game viewer
sufficient for visual analysis. Laboratory work remains focused on capture,
replay, result comparisons, and timings.

## Agreed laboratory integration

Mining is another case type in the existing laboratory, using the same
capture/replay commands and reporting conventions. Replay executes through
the actual built mod DLL, not a separately compiled copy of the planner.
Captured inputs and expected results are stored separately, and mine geometry
is compared exactly. Filter experiments may produce candidate results without
overwriting the baseline.

## Native batch placement

The maintainer prefers native batch placement and accepted retaining partial
placement on cancellation only if no suitable native batch operation exists.
That fallback condition is not met: `AddTerrainDesignationsCmd` accepts a
terrain-designation proto ID and an immutable array of `DesignationData`.
AFD already schedules this command for forestry, and the vanilla processor
resolves a general `TerrainDesignationProto`, so mining can use it too.

Verified against the local 2026-08-22 decompilation and the installed
`Mafi.Core.dll` timestamp. The relevant implementation is
`TerrainDesignationsManager`'s `IAction<AddTerrainDesignationsCmd>.Invoke`.

The batch executes a synchronous loop over `AddOrReplaceDesignation`.
It is one scheduled command, not a transactional or intrinsically faster
bulk-insertion API: it has no rollback, ignores each placement's Boolean
return, and reports all submitted origins in its success result. Actual
placements must therefore be checked for result/ownership bookkeeping rather
than treating command success as proof that every cell was placed. This does
not imply an environmental revalidation or ore-rescan loop.

Use the native batch path as the intended placement approach. Submission is
the cancellation cutoff: cancellation may prevent submission during capture
or planning, but once submitted the native batch is allowed to finish.
Cancellation after submission does not request rollback. Completion and actual
placement bookkeeping must still be observed for that submitted command.
This does not authorize placing into a different world after a world reset;
world lifetime remains a separate concern.

Continue collecting representative large-mine commit timings after release.
In-game qualification already established a large player-visible placement
improvement from native batch submission.
The earlier proposal of cancellable sliced placement with retained partial
work has not been accepted as the primary approach.

## Agreed execution rollout

After semantic-parity and in-game checks pass, the shared worker becomes the
normal mining execution path without a new player-facing toggle. Live capture
remains sliced on the game thread, pure calculation runs on the shared worker,
and placement uses the native batch command. Worker failure is reported rather
than silently falling back to game-thread calculation.

## Remaining engineering checks

The agreed constraints below remain the review checklist for qualification:

- Identify every input to the current ore/depth and safety decisions, including
  terrain needed beyond the tower boundary. Do not silently restrict existing
  safety rays or infer missing facts from empty/default values.
- Preserve the numeric information needed to reproduce the current algorithm;
  deriving layer thickness from rounded elevation endpoints may not reproduce
  the original thickness exactly.
- Keep request cancellation and world/tower lifetime separate from the rejected
  environmental-rescan policy. Define settings changes and submitted-batch
  ownership bookkeeping without reviving the old global cancellation gate.
- Establish baseline mining cases before changing the planner, and verify that
  extracting shared capture does not change existing access replay results.
- Measure capture slices, retained memory, pure calculation, and native batch
  commit separately on representative large mines, including paused operation.

These checks do not authorize adding a filter, a viewer, a combined planner,
or a large generic capture framework. The maintainer confirmed the overall
shared understanding and authorized implementation.
