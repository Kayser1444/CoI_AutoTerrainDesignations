# Accessway Search Worker

Status: approved design; not implemented.

Related design: [ATD Accessway Manager](../planned/atd-accessway-manager.md)
and [ADR 0003](../../adr/0003-coordinate-accessway-work-through-one-cooperative-manager.md).

## Objective

Move the computationally expensive access route search away from the game
thread without moving Unity/Mafi world access or terrain-designation mutation
across that boundary. The existing accessway manager remains the owner of
access requests, priority, cancellation, validation, and terminal results.

## Approved execution boundary

The **search worker** consumes one captured access snapshot and request
description, performs route search and access plan materialization, and returns
value-owned route, plan, progress, and diagnostic data.

The game thread retains:

- snapshot capture from the live world;
- access-obligation and access-request ownership;
- authoritative live validation;
- progress presentation and diagnostic rendering; and
- transactional access designation commit.

Plan materialization belongs to the worker because it is a pure derivation and
captured-snapshot validation step. The current search invokes materialization
while evaluating candidate goals; moving it back to the game thread would
introduce a cross-thread request/response boundary inside search. Designation
commit remains a game-thread operation because it reads and mutates the live
world.

## Approved snapshot ownership

Snapshot transfer is zero-copy and exclusive:

1. The game thread constructs the captured snapshot.
2. Ownership transfers to the search worker.
3. No game-thread code reads the snapshot while the worker owns it.
4. The worker may use worker-local lazy caches.
5. The worker returns value-owned route and plan data rather than exposing a
   concurrently shared snapshot.

Cancellation, staleness, or world reset may invalidate the result, but they do
not authorize concurrent game-thread access to the worker-owned snapshot.

## Approved purity enforcement

The first implementation remains in the existing ATD assembly. It does not add
a separately packaged worker DLL.

The worker boundary must nevertheless be mechanically guarded:

- no delegate or live object reference crosses in the worker input;
- worker evaluators are constructed inside the worker solely from captured
  data and scalar request settings;
- worker code resides in a dedicated namespace and source folder;
- build or architecture tests reject references from that worker module to
  Unity, game entities or managers, ATD global runtime state, UI, and logging;
- a runtime architecture fixture walks representative worker inputs and fails
  on delegates or prohibited reference types; and
- worker-owned caches are never read concurrently from the game thread.

A later separate assembly remains an available strengthening step if the
in-assembly boundary proves difficult to maintain. A separate assembly is not
the initial enforcement mechanism because the search data currently requires
Mafi value types, so the assembly would still reference `Mafi.Core` while also
adding packaging and visibility complexity.

## Approved concurrency and scheduling

ATD uses exactly one dedicated search worker and permits at most one in-flight
worker job. The worker has no independent request queue.

The accessway manager remains the sole scheduler and priority authority. It
selects one access request, completes game-thread snapshot capture, and submits
that request only when the worker is idle. Farming, Construction Assist,
interactive, planned-tower, and maintenance searches never execute
concurrently.

Interactive preemption requests cancellation of active derived work. The
interactive request remains manager-queued until the worker acknowledges that
cancellation; ATD does not start a second search thread to bypass a slow
cancellation.

The managed-work adapter's game-thread `Advance()` operation may submit a ready
job or poll worker-owned progress and completion. It must never wait for the
worker, and in particular must not block while the accessway manager holds its
coordination lock. Per-request `Task.Run` execution is excluded because a slow
cancellation could otherwise leave several expensive searches competing for
CPU outside manager scheduling.

## Approved cancellation contract

Cancellation has two stages:

1. **Logical cancellation** immediately removes the request's authority to
   commit. Any route or plan subsequently produced by that request is
   discarded.
2. **Worker cancellation acknowledgment** occurs when the running search
   reaches a safe checkpoint, stops, and returns its partial diagnostics.

The worker is cooperatively cancellable. It is never aborted, and ATD never
starts another search worker merely to bypass delayed acknowledgment. Every
potentially expensive V1/V2 expansion, handoff, history, ray, projection, and
plan-materialization loop must expose bounded cancellation checkpoints before
the worker backend can become the default. The currently observed hundreds-of-
milliseconds atomic expansion is not an acceptable final cancellation unit
even though moving it off the game thread would remove the rendered-frame
freeze.

A cancelled worker result contains no candidate plan. It retains the request
identity, cancellation reason, phase, visited and open-node counts, processing
time, rejection counters, and slowest measured search subphase. The exact
cross-hardware cancellation policy is:

- logical cancellation changes the owner-facing presentation immediately to
  `Stopping...`;
- worker acknowledgment should normally arrive within 100 ms wall time;
- acknowledgment over 100 ms records a slow-cancellation diagnostic with the
  active search subphase; and
- a repeatable acknowledgment over 250 ms in stress fixtures or the large
  farming playtest blocks default worker enablement until the remaining atomic
  operation is split.

These values are diagnostic engineering thresholds rather than player
settings. They may be retuned from measurements without changing the ownership
or cancellation model.

## Approved worker lifetime

The search worker is one lazily created process-lifetime background thread. It
starts when the first worker-eligible request is ready and sleeps without
polling while idle.

The thread survives world changes. World reset logically cancels the active
job, invalidates its world-generation token, clears manager-owned requests, and
does not join or block on worker acknowledgment. A newly loaded world's access
request may wait for the old job to acknowledge cancellation; ATD does not
create a replacement worker that would violate the single-worker rule.

Mod or process shutdown signals the worker to stop but performs no unbounded
game-thread join. The worker is marked as a background thread so process exit
does not depend on a final cancellation checkpoint. Because worker input is
captured data only, delayed shutdown retains no live world object.

## Approved staleness policy

Worker staleness distinguishes hard invalidation from environmental snapshot
dirtiness.

Hard invalidation logically cancels the worker immediately. It includes world-
generation change, owner disappearance, access-obligation or source-work
replacement, and incompatible access-mode, settings, or vehicle-clearance
change.

A relevant nearby terrain, designation, prop, tree, or entity mutation marks
the captured snapshot environmentally dirty but does not by itself interrupt
the worker. This avoids cancellation churn while simulation work and other mods
change the search region.

A successful result from a dirty snapshot undergoes complete live validation
and may commit only if its route and plan remain valid. A failed dirty result is
classified as stale rather than authoritative `NoCandidate`; it is eligible
for a fresh request without failure backoff. Diagnostics may identify that an
obsolete failure was suppressed, but dirtiness never hides the request's
terminal lifecycle record.

## Approved progress and diagnostic transport

The worker never calls ATD logging, toast, UI, or overlay APIs. It publishes an
immutable progress snapshot for non-blocking game-thread polling. The snapshot
contains the current phase and expensive subphase, visited and open-node
counts, processing time, and cancellation state. Phase changes and terminal
state are always published; ordinary progress may be rate-limited.

Terminal worker output retains complete aggregate diagnostics: timing
breakdowns, rejection counters and reasons, selected or rejected route details,
cancellation context, and dropped-live-sample counts. The game thread formats
and emits those diagnostics after accepting the terminal message.

When the diagnostic search overlay is enabled, the worker may publish sampled
node events through a bounded single-producer channel. The game thread drains
that channel when convenient. A full channel drops visualization samples and
increments a counter; it never blocks search or changes route selection. The
live flashing-dot overlay is therefore best-effort, delayed, and potentially
incomplete. The terminal result, selected path, and aggregate summary remain
authoritative.

## Approved CPU scheduling

The single search worker runs continuously while it has a job at below-normal
thread priority. ATD does not pin it to a processor, impose frame budgets or
duty-cycle sleeps, or raise its priority while the game is paused.

Below-normal priority allows the operating system to prefer the game's normal-
priority main and simulation work under contention while still allowing the
worker to consume an otherwise idle core. A paused game therefore needs no
special 30 ms search allowance. Worker wall time and progress diagnostics must
make starvation visible during playtesting.

The adaptive frame-budget controller is not used for worker search. It remains
potentially relevant to cooperative game-thread snapshot preparation,
validation, and commit work.

## Approved timeout accounting

One access request retains a configurable total ATD processing-time budget. The
existing `accessSearchTimeoutSeconds` setting remains authoritative, accepts
the current 5-600 second range, and retains its 60-second default.

The budget includes cooperative game-thread snapshot preparation, worker
session construction, route search, and access plan materialization. Queue
waiting, idle-worker waiting, interactive-preemption waiting, and ordinary time
between game-thread preparation slices do not consume it. Live validation and
the small atomic designation commit are measured separately and do not convert
a completed search into a timeout.

Diagnostics report queue age, active wall time, total processing time, and
prepare, worker, validation, and commit time separately. Exhausting the
configured processing budget causes logical cancellation and returns the same
partial diagnostics as user cancellation, with terminal classification
`TimedOut` rather than `Cancelled`.

## Approved phased rollout

Worker execution rolls out in three explicit stages:

1. **Worker opt-in.** Cooperative manager execution remains the production
   default. Developers and willing testers explicitly enable the search worker.
2. **Worker opt-out.** Worker execution becomes the default after equivalence,
   responsiveness, cancellation, staleness, and large-save criteria pass. A
   user may explicitly select cooperative execution as a temporary escape
   hatch.
3. **Worker enforced, fail closed.** Production access search always uses the
   worker. The cooperative backend may remain available to deterministic test
   infrastructure, but it is no longer a player-selectable or automatic runtime
   path. Worker infrastructure failure produces a clear diagnostic terminal
   result and never runs search synchronously on the game thread.

Promotion between stages is a release decision supported by measurements and
regression results, not an automatic time- or version-based transition.

During stages one and two, cooperative execution is an explicit selectable
mode, not an automatic fallback. A worker startup failure, protocol violation,
dead thread, or job fault fails the current request with a clear diagnostic.
The presentation may explain that the user can deliberately switch execution
mode and retry while that rollout stage still exposes cooperative execution.

One job exception is caught at the worker boundary and returns complete
diagnostics without necessarily terminating the persistent thread. If the
thread itself exits unexpectedly, ATD may attempt one clean restart while no
job is active. Repeated infrastructure failure disables worker search for the
current world and fails closed. Stage three removes the selectable cooperative
mode and is unconditionally fail closed.

## Approved execution-mode setting

Stages one and two expose one global, non-save execution policy with three
values: `Default`, `Cooperative`, and `Worker`. It belongs in
`ATDSettings.json` and the Accessways settings UI because backend selection is
an installation/runtime preference rather than world state.

In stage one, `Default` resolves to `Cooperative`. In stage two, `Default`
resolves to `Worker`. An explicit stage-one worker opt-in or stage-two
cooperative opt-out remains selected. In stage three all stored values resolve
to `Worker`; an obsolete explicit `Cooperative` value is diagnosed and ignored
or migrated. This avoids encoding a release's temporary default as a permanent
boolean choice.

## Approved save and reset behavior

An in-place save does not cancel pure worker computation. Save preparation
suspends manager polling, owner-facing presentation updates, live validation,
and access designation commit. The worker may continue against its captured
data and publish a terminal message, but the game thread does not consume that
message until saving finishes. No worker thread, snapshot, request, search
state, progress state, or result is serialized.

After saving, manager polling resumes and any completed result passes the
ordinary staleness and live-validation rules before commit. World unload or
world-generation replacement is not an in-place save: it hard-invalidates and
logically cancels the old job immediately. Mod/process shutdown retains the
approved non-blocking background-thread shutdown behavior.

## Approved snapshot backpressure

The manager does not prepare snapshots speculatively. Queued entries retain
only lightweight request/obligation metadata and factories. Snapshot capture
starts only after a request becomes active and the worker is, or will
imminently be, available.

ATD owns at most one prepared snapshot, one in-flight worker search state, and
one unconsumed terminal result. A logically cancelled job retains its snapshot
until worker acknowledgment, and the next request waits rather than allocating
another snapshot. The worker accepts no new job until the manager consumes or
explicitly discards its previous terminal result. Existing owner coalescing and
bounded manager queuing continue to suppress duplicate obligations.

This deliberately trades a small inter-request preparation gap for bounded
memory ownership and fresher captures.

One exceptionally large snapshot is additionally protected by a configurable,
conservative estimated-retained-memory ceiling. Incremental capture tracks
collection counts and the estimate before committing further growth. Crossing
the ceiling fails closed as `SnapshotTooLarge` rather than relying on recovery
from `OutOfMemoryException`.

The terminal result reports the capture bounds and sample counts, estimated
retained memory, configured ceiling, and how an advanced user may raise it in
`ATDSettings.json`. `SnapshotTooLarge` is stable for the current request
fingerprint and does not enter the ordinary unconditional 60-second retry. A
relevant area, work, settings, or configured-ceiling change may reopen it.

The default ceiling is not selected by design guesswork. Phase-one profiling
must measure the existing 124k-tile scenario and deliberately larger captures,
then choose and document a default with adequate headroom.

## Approved semantic equivalence

Worker execution is an execution backend for one shared access-search
implementation, not a second pathfinder. Cooperative and worker modes construct
the same worker-safe request and use the same session builder, V1/V2 search,
scoring, caches, heuristics, and access plan materializer. No worker-specific
route rule is permitted.

Deterministic fixtures canonicalize and require exact equality of terminal
outcome, selected route and provider, route cost, designation and cleanup plan,
and rejection counters. Timing, progress-publication cadence, and sampled live
overlay events are not semantic output and are excluded. Cancellation and
timeout fixtures require the same terminal classification and diagnostic shape
but do not require identical visited counts when the signal arrives between
different safe checkpoints.

## Approved field-validation requirement

Stage two cannot begin solely from local fixtures and developer playtests. At
least one public release must expose worker execution as an opt-in while
cooperative execution remains the default. This provides field coverage across
CPU layouts, saves, and mod combinations that local testing cannot reproduce.

Promotion remains an explicit maintainer release decision. It has no automatic
duration, adoption, or download threshold. A serious unexplained worker report
keeps the following release in stage one until the failure is understood.

## Approved stage-one promotion gates

Stage two may make worker execution the default only when all of the following
hold:

- cooperative and worker execution have exact semantic parity across the
  deterministic V1/V2 fixture suite;
- purity guards reject delegates, prohibited live references, and worker
  dependencies on Unity, game managers, ATD runtime globals, UI, or logging;
- lifecycle stress covers repeated submission, success, failure, cancellation,
  timeout, hard invalidation, environmental dirtiness, save, world reset, and
  worker restart without deadlock, duplicate completion, or thread leakage;
- the known large farming cases, interactive preemption, existing-terrain
  repair, and planned-tower ghost access pass;
- cancellation normally acknowledges within 100 ms and has no repeatable case
  above the 250 ms rollout ceiling;
- snapshot memory reaches a stable bounded plateau, the estimated-memory guard
  stops capture before dangerous allocation, and cancelled work releases its
  retained data;
- no search execution occurs on the game thread, while snapshot preparation,
  live validation, and commit satisfy the accessway manager's existing 30 ms
  atomic-operation ceiling;
- long unpaused searches show no sustained player-visible frame or simulation
  degradation under below-normal priority; and
- the required public opt-in release has no unresolved worker-related
  corruption, deadlock, freeze, or route-equivalence report.

## Approved stage-two promotion gates

Stage three may enforce worker execution only after at least one public release
has shipped with worker execution as the default and cooperative execution as
an explicit opt-out. Before promotion:

- every production access caller uses the worker-safe request boundary;
- no unresolved worker correctness, lifecycle, freeze, or compatibility issue
  remains;
- no known player report requires cooperative mode as a workaround;
- worker startup, one permitted restart, and fail-closed diagnostics have been
  exercised successfully; and
- the cooperative backend remains in deterministic test infrastructure for
  semantic parity even after removal from player settings.

Stage-three promotion is an explicit maintainer release decision with no
automatic time threshold.

## Approved execution-mode transitions

Changing the resolved execution mode hard-invalidates the active attempt. The
manager logically cancels it with reason `ExecutionModeChanged`; its access
obligation remains live and becomes immediately eligible under the newly
resolved backend without failure backoff.

A worker-to-cooperative change waits for worker cancellation acknowledgment
before cooperative work starts. A cooperative-to-worker change stops at the
next cooperative cancellation boundary. The two backends never overlap.
Queued requests require no snapshot rebuild because they resolve execution mode
only when activated. If the request has already entered atomic designation
commit, that commit finishes and the new mode applies to the next request.

## Approved terminal-result ordering

Worker terminal publication is not request completion and does not authorize
world mutation. Every worker job and message carries a unique job ID, access
request ID, and world-generation token. The game-thread manager accepts a
terminal message only while all identities still match its active request.

Logical cancellation, hard invalidation, or supersession recorded before
designation commit wins even if the worker already published success. A
mismatched terminal route or plan is discarded, while relevant cancellation or
fault diagnostics may still be retained. Once atomic designation commit begins,
it finishes or rolls back and a later cancellation applies only to subsequent
work.

## Approved worker-fault retry policy

`WorkerJobFaulted` is not eligible for the ordinary unconditional 60-second
retry while the request fingerprint is unchanged. A relevant input change,
execution-mode change, explicit workflow reactivation, or direct interactive
retry may create a new attempt. Farming does not periodically replay an
identical faulting job.

One unexpected worker-thread death permits one clean infrastructure restart
while idle, but ATD does not silently replay the failed job. Repeated thread
failure disables worker execution for the current world and fails later
requests clearly.

Fault diagnostics include request identity, phase and subphase, progress
counters, exception type and message, and a captured worker stack. Expected
cancellation contains its approved partial diagnostics but no exception stack.

## Approved relationship to adaptive budgeting

Worker search does not use the parked adaptive frame-budget controller. During
rollout, explicitly selected cooperative execution retains the current fixed
budgets so worker implementation is not coupled to another scheduler change.

Snapshot preparation, live validation, and commit remain separately measured.
Any atomic game-thread operation above the existing 30 ms ceiling must be split
or optimized; adaptive budgeting cannot repair a stall inside an atomic call.

After worker execution reaches stage two, measurements decide whether the
preserved controller should be repurposed for resumable game-thread preparation
only. If those phases are already smooth, the adaptive ticket may be reduced or
closed rather than activated without evidence.

## Approved snapshot and workspace separation

The current snapshot's captured world facts and mutable search caches become
separate concepts and code ownership:

- the **captured access snapshot** is sealed, data-only, contains no delegates,
  and never mutates after publication; and
- one **access search workspace** is created for each job and owns session
  builders, queues, histories, lazy side-ray and projected-profile caches, and
  temporary diagnostics.

The workspace references the snapshot without copying its large terrain
collections. Worker execution creates and owns the workspace on the worker
thread; cooperative execution creates the same workspace on the game thread.
Terminal output retains only value-owned route, plan, and diagnostic data, after
which the workspace and snapshot reference are released.

## Approved configuration capture

Before snapshot publication, the game thread captures every search-affecting
setting into one immutable **access search policy snapshot**. It includes all
feasibility flags, route and landscaping costs, cleanup costs, ray limits and
penalties, search limits, heuristic selection, and other mutable values used by
session construction, V1/V2 search, scoring, or plan materialization.

The request fingerprint derives from the same policy values. The worker,
workspace, scorers, heuristics, and materializer receive policy explicitly and
never read mutable `AutoTerrainDesignationsMod` static settings. Mathematical
constants may remain static.

Diagnostic sampling and presentation preferences are captured separately.
Changing semantic policy hard-invalidates active work; changing overlay or
diagnostic presentation does not alter request identity or cancel search.

## Approved evaluator reconstruction

No callback delegate crosses the worker boundary. The captured access snapshot
contains concrete primitive facts only. For each job, the access search
workspace reconstructs pure **access search evaluators** from that snapshot and
the access search policy snapshot.

Any evaluator input that currently requires a vanilla API or live game object
must instead be captured as concrete data on the game thread before snapshot
publication. Search, scoring, handoff evaluation, and plan materialization call
the request-local evaluators; they never call back into the game thread or read
live state.

The capture phase must not replace callbacks with an exhaustive table of every
possible candidate handoff. Such a table could make snapshot capture and memory
scale with the search space. Derived answers remain lazy worker-owned cache
entries in the workspace.

## Approved worker mailboxes

The manager and search worker communicate through one single-slot mailbox in
each direction, consistent with there being at most one active worker job:

1. The manager atomically publishes one immutable job and signals the sleeping
   worker.
2. The worker atomically claims that job and exclusively owns its snapshot and
   workspace while executing it.
3. The worker atomically publishes one immutable terminal result.
4. The manager polls and consumes that result during its normal game-thread
   tick.

The worker never invokes a game-thread callback, dispatcher, UI action, or
manager method. Publication may signal availability, but result handling waits
for the manager tick. The manager applies the approved job ID, request ID,
world-generation token, cancellation, and commit-authority rules before
accepting a terminal result. A result occupies its slot until consumed; no
subsequent job may overwrite it or begin before the manager has reclaimed the
previous job's terminal state.

## Approved capture consistency

Cooperative snapshot capture does not restart the entire snapshot merely
because relevant environmental state changes between slices. Large active areas
could otherwise starve indefinitely. Instead:

- capture reads concrete primitive facts once;
- every graph or other derived structure is built exclusively from those
  captured facts, without rereading live state in later derivation passes;
- capture checks hard invalidation during every slice and aborts immediately;
- relevant source revisions are recorded at capture start and completion; and
- an environmental revision change publishes the internally coherent snapshot
  already marked dirty.

A dirty snapshot may therefore be temporally approximate but must not be
structurally self-contradictory. Structures derived from different capture
generations must never be combined. The approved dirty-result rules then
require authoritative live validation for success and prevent dirty failure
from establishing `NoCandidate`.

## Approved live-plan validation

Every successful worker result is provisional, whether or not its snapshot is
already marked environmentally dirty. Before designation commit, the manager
runs a targeted authoritative validation on the game thread that:

- confirms the request still owns commit authority and its access obligation
  still exists;
- first checks whether current terrain already satisfies the obligation, in
  which case the request completes without placing the candidate plan;
- validates every proposed addition, update, cleanup, and removal against live
  terrain, bounds, tower-area policy, prototypes, conflicts, and ownership;
- reconstructs the proposed route against current live facts and confirms that
  it connects the intended provider and consumer for the required vehicle tier;
  and
- applies the same care to cleanup actions as to designation additions.

Validation is cooperative where possible, and no atomic validation operation
may exceed the existing 30 ms game-thread ceiling. It validates only the chosen
route and plan; it must not rerun the full path search. If environmental change
invalidates the candidate, the manager discards it and makes the obligation
immediately eligible for a fresh snapshot and search without failure backoff.

## Approved progress and overlay bounds

Scalar progress uses a latest-value-wins slot rather than a queue. The worker
publishes at a capped cadence and also publishes promptly when the phase
changes, cancellation is acknowledged, or work reaches a terminal state.

Detailed visualization samples use a fixed-capacity single-producer ring
buffer. If its consumer falls behind, samples may be dropped and a dropped-
sample counter is retained; search must never wait for diagnostic transport.
The immutable terminal result still owns authoritative totals and the final
selected path, independent of sampled overlay loss.

Publication cadence and ring capacity are internal tuning constants selected
from phase-one profiling. They are not player-facing settings and are not fixed
to speculative values in this design.

## Approved pure-preparation ownership

Game-thread snapshot capture performs only extraction that genuinely requires
live game APIs. It copies the primitive terrain, designation, entity, request,
and policy facts needed to answer the access question.

The execution backend owns every CPU-heavy transformation that can be derived
purely from those facts, including spatial indexes, adjacency and access graphs,
derived masks, evaluator construction, search-specific lookup tables, and
caches. Worker mode performs those stages in the access search workspace on the
worker thread. Cooperative mode runs the identical stages incrementally on the
game thread.

Derived structures are created lazily where practical and released with the
workspace, avoiding unnecessary simultaneous retention of large primitive and
fully expanded representations. This boundary maximizes off-thread work while
preserving one canonical algorithm for semantic parity.

## Approved implementation and enablement order

Implementation proceeds in this order:

1. Introduce the data contracts, purity guards, access search workspace seam,
   and dormant worker runtime.
2. Migrate and test farming automation first because it is the demonstrated
   player-facing freeze source.
3. Keep worker mode internal and unavailable to players while production
   callers are only partially migrated.
4. Migrate interactive, repair, existing-terrain, and ghost-tower access work
   through the same request and execution boundary.
5. Expose stage one's public worker opt-in only after every production access
   caller honors the selected global execution mode.

During partial migration, production must not run cooperative interactive work
concurrently with worker farming work. Hiding the incomplete mode avoids a
setting whose meaning varies by caller and preserves the approved single-search
CPU policy.

## Approved implementation slices

1. **Pure execution core.** Introduce the access search policy snapshot,
   data-only request contracts, workspace and evaluators, cooperative adapter,
   purity guards, and semantic-parity fixtures. This slice introduces no worker
   thread or production behavior change.
2. **Primitive capture pipeline.** Narrow live capture to primitive facts, move
   pure graph and index preparation behind the execution seam, and add revision
   dirtiness, memory estimation, and snapshot backpressure. Apply the seam to
   farming first while execution remains cooperative.
3. **Dormant farming worker.** Add the persistent thread, mailboxes,
   cancellation checkpoints, lifecycle and fault handling, bounded progress and
   diagnostics, and internal-only farming execution. It is not player-selectable.
4. **Complete manager integration.** Add authoritative live-plan validation and
   migrate interactive, repair, existing-terrain, and ghost-tower paths. Verify
   saving, world reset, preemption, mode transitions, terminal races, and fail-
   closed behavior.
5. **Stage-one opt-in release.** Expose the global execution-mode setting and
   translations, tune measured bounds, satisfy the stage-one promotion gates,
   playtest, and prepare the public worker-opt-in release.

Every slice keeps the normal production backend usable. Partial worker support
remains inaccessible to players until all production callers honor the same
mode contract.

## Open decisions

- None. Shared understanding was confirmed on 2026-08-17. Measured tuning
  values remain implementation evidence, not unresolved architecture.
