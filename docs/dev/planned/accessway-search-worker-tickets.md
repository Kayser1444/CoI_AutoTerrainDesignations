# Accessway search worker implementation tickets

Status: approved sequential implementation queue; Ticket 1 is implemented,
Ticket 2 is in progress, the cooperative search-slicing sub-slice is approved
and in progress, the Access Search Laboratory replay foundation is approved
between Tickets 2 and 3, and Ticket 3 remains queued.

These tickets implement the approved
[accessway search worker design](../in-progress/accessway-search-worker.md) and
[ADR 0005](../../adr/0005-execute-pure-access-search-on-one-worker.md). The
[Access Search Laboratory](access-search-laboratory.md) and
[ADR 0006](../../adr/0006-replay-access-search-from-owned-snapshots.md) govern
the replay slices. These design documents are authoritative for cross-cutting
semantics. Each ticket must preserve unrelated work and leave the normal
production backend usable.

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

### Current implementation

Ticket 2 is partially implemented in the cooperative path. It captures
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
checks. Vanilla mining/dumping designation-readiness callbacks are now
answered from captured terrain, prop, stump, and exact layout-occupancy
primitive facts; the cooperative evaluator no longer calls the live
designation manager during preparation. The estimator includes the retained
readiness facts, and the capture fixtures cover the vanilla fulfillment rules.
Oversized snapshots now fail a preflight before initial reachability
classification, and out-of-area origin checks share one bounded tower flood.
The terminal snapshot-size reason is preserved in cluster diagnostics.

Ticket 2 remains in progress. Production workspaces now reconstruct their
handoff evaluator from snapshot data and policy rather than receiving
caller-built delegate closures. Synthetic fixtures may still inject evaluators
for deliberately artificial materializer cases; the immutable snapshot no
longer accepts callback-shaped compatibility inputs. Readiness fixtures cover
terrain, props, stumps, and exact layout-occupancy edge cases. The capture pipeline still
needs the specified large-area stress evidence for its 30 ms atomic ceiling and
bounded-memory behavior, and the pure handoff helper must be isolated from the
runtime type before worker execution.
The cooperative search-slicing sub-slice below is independent of worker-thread
execution and may land while those capture items remain open. Ticket 3 remains
blocked on the Ticket 2 acceptance items.

The first deeper search slice is now also present: V2 frontier expansion keeps
the exact straight/strafe/turn order but resumes between transition items. The
previous one-node atomic expansion could dominate a frame on large, data-
dependent transition evaluation; it is now reported as the existing V2
frontier continuation phase and shares the same deadline and cancellation
checkpoint as the other continuations. Snapshot capture, lane candidate
generation, ray-overlay internals, and fixed-size transition geometry remain
atomic and continue to be measured separately.

### Ticket 2 cooperative search-slicing sub-slice

#### Outcome

Cooperative V2 search preserves exact search semantics while yielding inside
large data-dependent expansions instead of treating one visited node as an
atomic frame unit.

#### Scope

- Add one active, typed continuation per search session and a shared absolute
  deadline/cancellation budget.
- Resume a continuation before another priority-queue node is popped.
- Slice ground suffix traversal, fixed-navigation path/portal traversal, and
  handoff candidate pairing/entry enumeration at item boundaries.
- Keep snapshot capture, plan materialization, ray-overlay internals, lane
  candidate generation, and fixed-size transition geometry atomic initially,
  with aggregate overrun diagnostics.
- Preserve transactional semantics: no live designation mutation occurs from a
  partial continuation; cancellation discards the private continuation.

#### Acceptance

- Existing cooperative V1/V2 results and route/materialization behavior remain
  unchanged in the established manual regression cases.
- A continuation can yield and resume without redoing completed path, portal,
  suffix, candidate, or entry work; cancellation at a checkpoint leaves no
  candidate plan to commit.
- Aggregate diagnostics report continuation stage, slice count, total time,
  maximum slice, and slow atomic steps without per-checkpoint log flooding.
- The focused project build and existing fixture suite pass.

## Ticket 2A: Replay seam and single-case round trip

Depends on Ticket 2, including pure-helper isolation and the worker-safe
snapshot contract.

### Outcome

One real, game-validated access search can be recorded and reproduced exactly
outside the game through the exact built `AutoTerrainDesignations.dll`.

### Scope

- Add the dormant internal replay facade and a versioned compressed case codec
  with a readable manifest.
- Add developer-only `arm next search` recording at the exclusively owned
  snapshot seam, with captured policy, accepted canonical outcome, hashes,
  provenance, and in-game phase timing.
- Add a development-only runner that loads an explicitly selected Release DLL
  and resolves the matching installed game assemblies.
- Separate exact canonical outcome fields from observational diagnostics.
- Qualify identical replay across fresh processes and fail closed on schema,
  policy, input-integrity, or nondeterminism problems.

### Acceptance

- One armed real search produces one atomically completed inbox case without
  sharing or duplicating the owned snapshot and without unarmed runtime cost.
- The runner invokes the exact recorded/built DLL, reconstructs the opaque input,
  and reproduces the terminal classification, structured reason, provider,
  ordered route, exact cost bits, and materialized plan exactly.
- The report identifies case, DLL, build configuration, policy, and game
  assemblies by hash and shows in-game and runner phase timing separately.
- The executable and corpus are absent from the player package.
- The focused project and runner builds pass.

Ticket 3 depends on this slice.

## Ticket 2B: Corpus regression and benchmark runner

Depends on Ticket 2A.

### Outcome

The local real-case corpus becomes a strict semantic regression gate and a
repeatable directional performance benchmark before worker parity work expands.

### Scope

- Add the external user-local inbox and promoted corpus, content addressing,
  deduplication, required family/suite-role metadata, explicit migrations, and
  maintainer-approved game-assembly compatibility attestations.
- Invoke the existing pure synthetic fixture gate before private cases.
- Add exact canonical comparison, structured diffs, bounded parallel semantic
  execution, child-process isolation, external watchdogs, and strict exit codes.
- Add sequential low-contention Release benchmarking for end-to-end pure
  execution, with phase, wall-clock, CPU, memory, allocation, and GC evidence.
- Add the staged target-family/full-corpus benchmark funnel and preserve
  observational diagnostics without making them semantic equality fields.

### Acceptance

- At least one trivial semantic case and one representative expensive case run
  through the complete foundational corpus workflow.
- Repeated fresh-process runs prove exact determinism for promoted cases.
- Semantic cases may execute concurrently, while authoritative performance
  cases remain sequential and single-search/single-threaded.
- Candidate regressions, hangs, crashes, memory ceilings, incompatible schemas,
  and unauthorized game-assembly changes fail with clear evidence.
- Ordinary builds and public CI remain independent of the private corpus; the
  committed synthetic fixture gate remains runnable publicly.

Substantial Ticket 3 parity work depends on this foundational slice.

## Parallel Access Search Laboratory follow-ons

These approved slices follow the
[Access Search Laboratory design](access-search-laboratory.md) but do not block
the worker thread itself:

- **Collaborative route review** adds standalone route/plan visual diffs,
  read-only in-game candidate import, exact-scenario fingerprinting, live
  validation attestations, and case-specific maintainer promotion.
- **Autonomous conformance tuning** adds fixed-baseline campaign manifests,
  dedicated worktree/branch isolation, immutable harness/oracle guards, staged
  experiments, bounded stopping rules, passing-candidate commits, and concise
  failed-approach journals. It begins only after regression and benchmark
  evidence are trusted.

## Ticket 3: Dormant farming worker

Depends on Tickets 2, 2A, and the foundational Ticket 2B regression runner.

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
