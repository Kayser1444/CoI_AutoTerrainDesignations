# Accessway search worker implementation tickets

Status: approved sequential implementation queue; Ticket 1 implemented in
cooperative mode; Ticket 2 capture contracts and backpressure guard are in
progress, while later tickets remain queued.

These tickets implement the approved
[accessway search worker design](../in-progress/accessway-search-worker.md) and
[ADR 0005](../../adr/0005-execute-pure-access-search-on-one-worker.md). The
design document is authoritative for cross-cutting semantics. Each ticket must
preserve unrelated work and leave the normal production backend usable.

## Ticket 1: Pure execution core

### Outcome

The existing access algorithm runs through one worker-safe execution seam in
cooperative mode, with no production behavior or threading change.

### Scope

- Introduce the immutable access search policy snapshot and eliminate search,
  scoring, heuristic, and materializer reads from mutable global settings.
- Replace snapshot callbacks and live references with data-only request inputs.
- Separate the captured access snapshot from the request-local access search
  workspace and pure evaluators.
- Make V1, V2, scoring, caches, and access-plan materialization use the same
  canonical executor through a cooperative adapter.
- Add forbidden-reference/build guards and a representative runtime object-
  graph fixture.

### Acceptance

- Existing deterministic V1/V2 success, failure, scoring, and materialization
  fixtures produce canonical results equal to the pre-extraction baseline.
- Architecture checks reject delegates, live game references, mutable global
  configuration reads, and game-state mutation across the execution boundary.
- Cooperative play behavior and diagnostics remain unchanged.
- The project builds and the focused test suite passes.

## Ticket 2: Primitive capture pipeline

Depends on Ticket 1.

### Outcome

Game-thread preparation captures only live primitive facts; pure preparation
executes behind the common backend seam. Farming is the first migrated caller,
but remains cooperative in production.

### Scope

- Introduce resumable primitive capture and move pure indexes, graphs, masks,
  lookup tables, and evaluator setup into the workspace.
- Capture source revisions and distinguish hard invalidation from environmental
  dirtiness without restarting coherent snapshots.
- Add pre-allocation memory estimation, the configurable conservative ceiling,
  `SnapshotTooLarge`, and one-snapshot backpressure.
- Route farming access requests through the new capture and workspace seam.
- Instrument capture, pure preparation, search, materialization, validation,
  and commit separately.

### Acceptance

- No game-thread capture operation exceeds the existing 30 ms atomic ceiling
  in stress fixtures; any offender is split or optimized.
- The known roughly 124k-tile farming case and at least one larger fixture show
  bounded memory with no speculative or overlapping snapshot allocation.
- A coherent snapshot changed during capture is marked dirty; hard invalidation
  cancels it; dirty failure cannot establish authoritative `NoCandidate`.
- Cooperative farming results remain semantically equal to the Ticket 1 core.
- The project builds and the focused test suite passes.

### Current implementation slice

The first Ticket 2 slice is now present in the cooperative path. It captures
the start/completion revisions, records environmental dirtiness separately
from hard world/policy invalidation, estimates retained snapshot memory before
and after growth, and fails closed with `SnapshotTooLarge` when the configured
ceiling is exceeded. The farming request receives the stable diagnostic reason
and the capture fixture gate covers the revision, single-capture backpressure,
and estimator contracts. Tower-local ramp/clearance revisions are carried into
the snapshot and invalidate capture when they change. Prop and tree enumeration
is also copied into value-owned primitive cleanup facts before cleanup policy
preparation. Building occupancy and fixed-height facts are likewise copied at
capture entry and reused by cleanup and durability preparation rather than
reading the mutable farming cache mid-capture. Captured precise terrain
heights now feed handoff geometry, exact-profile, leveling-face, and rank-work
checks; vanilla designation-readiness callbacks remain to be extracted from
their live API inputs.

The remaining Ticket 2 work is the full extraction of live terrain/prop facts
from pure graph, mask, evaluator, and lookup preparation. Until that extraction
and its bounded stress fixtures are complete, this slice must not be treated as
worker-ready or as evidence that all preparation is off the game thread.

## Ticket 3: Dormant farming worker

Depends on Ticket 2.

### Outcome

Farming can exercise the canonical executor on one internal worker for testing,
without exposing a player-selectable or default production mode.

### Scope

- Add the lazy process-lifetime below-normal-priority background thread and its
  single-slot job/result mailboxes.
- Add logical cancellation, inner-loop checkpoints, terminal acknowledgment,
  timeout accounting, and manager-owned result authority.
- Add save/world lifecycle handling, one permitted infrastructure restart,
  worker fault diagnostics, and fail-closed behavior.
- Add latest-value progress and fixed-capacity sampled overlay transport.
- Wire farming to the internal worker path while preventing concurrent
  cooperative access search.

### Acceptance

- Farming search, pure preparation, and plan materialization execute off the
  game thread; live capture and mutation do not.
- Cancellation normally acknowledges within 100 ms and has no repeatable case
  above 250 ms in the approved stress fixtures.
- Success, failure, cancellation, timeout, save, world reset, thread fault,
  restart, and stale-terminal races produce one authoritative outcome without
  deadlock, duplicate commit, or thread leakage.
- Diagnostic backpressure never blocks search and terminal diagnostics retain
  authoritative totals and the final selected path.
- Worker and cooperative farming fixtures have exact canonical result parity.
- The project builds and the focused test suite passes.

## Ticket 4: Complete manager integration

Depends on Ticket 3.

### Outcome

Every production access caller can use the worker-safe boundary, and every
worker success is authoritatively validated before game-state mutation.

### Scope

- Add cooperative targeted live-plan validation and transactional commit.
- Migrate interactive designation work, repair, existing-terrain access, and
  ghost-tower access to the common execution boundary.
- Complete interactive preemption, mode-transition, cancellation, save/reset,
  environmental retry, and terminal linearization behavior.
- Exercise the internal tri-state resolution needed by the staged rollout,
  while keeping incomplete public worker selection unavailable.

### Acceptance

- All production callers use the worker-safe request, snapshot, workspace, and
  result contracts with no synchronous search fallback.
- Live validation can detect an already-satisfied obligation, accept a current
  route and plan, or reject stale additions and cleanup without rerunning full
  search on the game thread.
- Farming, interactive, repair, existing-terrain, and ghost-tower scenarios
  preserve semantic parity and never run competing searches.
- Validation and commit respect the 30 ms atomic ceiling, except for the final
  demonstrated-small transactional mutation.
- Lifecycle and mixed-priority stress fixtures pass.
- The project builds and the focused test suite passes.

## Ticket 5: Stage-one opt-in release

Depends on Ticket 4.

### Outcome

Worker execution is a supported explicit opt-in for public field validation;
cooperative execution remains the stage-one default.

### Scope

- Expose the global `Default` / `Cooperative` / `Worker` execution setting, with
  stage-one `Default` resolving to `Cooperative`.
- Add localized UI, stopping/fault/disabled status, work-type toast text, and
  complete diagnostics for configuration and cancellation outcomes.
- Profile representative hardware and choose documented initial snapshot-memory,
  progress-cadence, overlay-capacity, and warning values.
- Run every stage-one promotion gate in the approved design and update player
  documentation and release notes.
- Prepare the package for a public opt-in release. Publishing, tagging, and
  marking the release complete still require explicit release confirmation.

### Acceptance

- Every production caller honors the selected global execution mode.
- Purity, parity, lifecycle, cancellation, memory, main-thread phase, large-
  scenario, and mixed-caller gates pass with captured evidence.
- Worker startup or protocol failure fails closed and never silently falls back
  to cooperative execution.
- Normal cooperative users see no behavior change unless they opt in.
- The project builds, package contents are verified, and the complete playtest
  checklist is ready for the public opt-in release.
