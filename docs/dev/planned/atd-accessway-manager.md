# ATD Accessway Manager

Status: design draft; no implementation yet.

## Problem

Accessway searches now serve several workflows:

- player-triggered **Create Designations** scans;
- planned mining-tower access;
- farming preparation and filling;
- Farm Placement Assist today, and the broader Construction Assist design later.

The pathfinder already exposes an incremental `AccessPathSearchSession.Step(...)`
API, but request ownership and scheduling remain distributed across its callers.
The most important current failure mode is in farming: `CreateAccessRamp` drains
`CreateAccessRampCoroutine` with `while (routine.MoveNext()) { }`. Any nested
"sliced" search therefore resumes immediately until it finishes, times out, or is
cancelled. It is asynchronous in shape but synchronous in execution, so a large
search can monopolize one simulation tick and cause a severe lag spike.

The current access flow also has process-wide coordination state:

- `s_cancelExperimentalAccessSearch` cancels unrelated request types;
- `LastExperimentalAccessSearch` and `LastExperimentalAccessPlan` are shared
  result slots;
- Create Designations implements its own single-flight queue while farming has
  no equivalent request owner;
- multiple farming clusters can invoke full access generation in one farming
  pass;
- timeout and progress reporting are implemented inside one search coroutine,
  rather than as request policy.

The proposed `ATDAccesswayManager` is a runtime-only request coordinator. It
does not replace the access graph, search session, materializer, reachability
analysis, farming session, or Construction Assist placement-intent queue.

## Goals

1. No access search may monopolize a frame or simulation tick.
2. Every search, cancellation, result, timeout, and commit belongs to one
   explicit request.
3. Repeated requests for the same live obligation coalesce instead of producing
   duplicate work.
4. Newer work can supersede stale work without cancelling unrelated towers or
   workflows.
5. A stalled or obsolete request is removed predictably and leaves no partial
   designation mutations.
6. Results are revalidated against live world state before any designation is
   placed.
7. Manager state remains transient and re-derivable so ATD stays safe to remove
   from a save.

## Non-goals

- Do not put Mafi or Unity world access on a background thread in the first
  implementation.
- Do not persist pathfinder sessions, snapshots, queue entries, callbacks, or
  manager-owned objects.
- Do not merge the Construction Assist placement-intent queue into the access
  queue. A placement intent may outlive several access requests.
- Do not change route scoring, V1/V2 graph behavior, or designation-plan
  semantics as part of this refactor.
- Do not use timeout as a substitute for bounding the cost of one `Step` call.

## Core model

### Request

An `ATDAccesswayRequest` is an immutable description of one access obligation.
Suggested fields:

| Field | Purpose |
|---|---|
| `RequestId` | Monotonic runtime ID used in logs and UI. |
| `WorldGeneration` | Reject work that belongs to an unloaded world. |
| `OwnerKey` | Stable deduplication and supersession key. |
| `Kind` | `CreateDesignations`, `PlannedTower`, `FarmingPreparation`, `FarmingFilling`, or future `ConstructionLeveling`. |
| `TowerEntityId` | Resolves the current tower without relying on a long-lived entity reference. |
| `WorkFingerprint` | Hash/key for the source origins, phase, settings, and relevant terrain/designation state. |
| `Priority` | Interactive, foreground automation, or background maintenance. |
| `SearchFactory` | Creates a fresh snapshot, request, and incremental search session on the main thread. |
| `Validation` | Rechecks that the result still applies before commit. |
| `Completion` | Delivers a request-scoped terminal result to the owner. |
| `Policy` | Queue TTL, active timeout, retry limit, and progress visibility. |

`OwnerKey` should identify the obligation, not an individual attempt. A useful
shape is `(kind, towerEntityId, phase, logicalWorkKey)`. Enqueuing the same key
and fingerprint returns the existing handle. Enqueuing the same key with a new
fingerprint supersedes the old attempt. Different towers and different farming
phases remain independent.

### Handle and result

The caller receives an `ATDAccesswayRequestHandle` with read-only state:

```text
Queued -> Preparing -> Searching -> Validating -> Committing -> Succeeded
                                              \-> Stale -> retry or Superseded
Queued/Preparing/Searching --------------------> Cancelled/TimedOut/Failed
```

Terminal results must be stored on the handle or passed directly to its owner.
They must not use `LastExperimentalAccessSearch` or
`LastExperimentalAccessPlan`. A result should contain:

- terminal status and stable reason code;
- `AccessSearchResult` and `AccessDesignationPlan`, when produced;
- visited/pending node counts and active processing time;
- placement outcome and placed origins, after commit;
- whether the manager will retry because live validation found stale input.

Cancellation is request-scoped and cooperative. Disposing or cancelling a
handle never means "cancel all access searches."

## Scheduling model

### Cooperative main-thread async first

The first implementation should be frame-budgeted cooperative scheduling on the
Unity main thread. Snapshot creation, search callbacks, entity lookup,
pathability, designation validation, and placement currently cross enough
game-facing boundaries that moving them to `Task.Run` would be unsafe without a
separate purity audit.

"Asynchronous" therefore means:

- callers enqueue and return/yield;
- the manager advances work from `AutoTerrainDesignationsTicker.Update()`;
- it stops when the frame budget is consumed;
- the next frame resumes the same request;
- simulation/world mutation occurs only during the explicit commit phase.

This fixes the farming synchronous-drain bug while preserving the game's
single-threaded ownership model. Background search can be reconsidered later if
`AccessSearchSnapshot` and every delegate captured by it are proven immutable,
thread-safe, and free of Unity/Mafi access.

### Global budget and fairness

Start conservatively with one active search session and a bounded pending queue.
That avoids interleaving code that still uses shared access caches or scratch
state. Multiple requests can be pending without being simultaneously executed.

Suggested initial policy:

- one configurable unscaled-time budget per frame, initially about 2 ms;
- a node quantum larger than the current `Step(1)`, adjusted downward/upward
  from measured slice time;
- interactive Create Designations requests have highest priority;
- Construction/Farm Assist requests have normal priority;
- maintenance/retry work has low priority;
- aging raises a waiting request gradually so continuous clicks cannot starve
  automation forever;
- at most one commit transaction per frame.

The time budget must be checked between bounded node quanta. A stopwatch around
an unbounded operation cannot prevent a spike that already occurred. Snapshot
capture and materialization must be measured separately because search slicing
does not make those phases cheap.

### Preparation and commit budgets

The manager should expose three separately measured phases:

1. **Prepare**: capture building/designation/pathability inputs and create the
   snapshot/session.
2. **Search**: call `AccessPathSearchSession.Step(nodeQuantum)` until terminal.
3. **Validate and commit**: materialize/revalidate, then place or clean up the
   complete accepted plan transactionally.

The first manager version may keep snapshot capture synchronous if necessary,
but it must log its duration and rate-limit captures. If captures still exceed
the frame budget, the next refactor should make the expensive collectors
incremental. Search work should not be moved to a worker thread merely to hide
an unsliced main-thread capture.

## Coalescing, supersession, and backpressure

### Coalescing

- Same `OwnerKey` + same `WorkFingerprint`: return the existing handle.
- Same `OwnerKey` + different fingerprint: mark the old request superseded and
  enqueue the new request.
- A farming tick that sees an already queued or active request records
  `Pending`; it does not start another ramp search.
- A completed failure may be negatively cached until its retry trigger changes
  (terrain/designation fingerprint, phase, settings, or a short backoff).

### Queue bounds

The queue should have both a global limit and a per-owner limit. On overflow:

1. remove superseded and expired entries;
2. coalesce duplicate owners;
3. drop the oldest low-priority derived request;
4. never silently drop the newest interactive request.

Dropping a derived farming request is safe only because the farming obligation
remains live and will enqueue again after backoff. The terminal reason must still
be logged.

### User cancellation

The progress toast's cancel action should call `Cancel(requestId)`. Cancelling a
Create Designations access request should cancel its owning Create Designations
operation, but must not cancel farming, another tower, or a future Construction
Assist request. Automated requests normally have no global toast; their state is
reported through their owning panel/session and diagnostics.

## Timeout and stall policy

Track these clocks separately:

- **queue age**: time waiting before activation;
- **active wall time**: unscaled time since activation;
- **processing time**: time actually spent in prepare/search/validation;
- **last progress**: last change in visited nodes, pending nodes, phase, or
  terminal state.

Recommended terminal rules:

- expire queued work whose owner/fingerprint is no longer current;
- time out an active attempt using request policy and processing time;
- classify it as stalled when several scheduled quanta make no observable
  progress;
- cancel every request on world-generation reset;
- use a small, bounded retry count only for explicitly retryable stale-input or
  transient failures.

A request cannot be forcibly killed in the middle of one synchronous method
call. "Kill stalled requests" therefore requires every heavy phase to yield at
safe boundaries. If one node expansion, snapshot collector, or materialization
step can itself take hundreds of milliseconds, that operation needs its own
incremental API.

## World consistency and mutation boundary

Search remains a dry run. Before placement, validation must confirm at least:

- the world generation still matches;
- the tower still exists and is eligible;
- the workflow phase and owner fingerprint are current;
- relevant settings and resolved vehicle clearance are unchanged;
- source work origins/designations still exist with compatible profiles;
- intended placement/cleanup origins have not gained conflicts;
- the planned-tower ghost, when applicable, is still unstarted and in scope.

If validation fails, discard the plan without mutation. Requeue only when the
owner is still live and the retry policy allows it. Placement should remain one
transaction with the existing rollback behavior; cancellation is observed
before commit, not halfway through it.

## Integration by caller

### Create Designations and planned tower access

`QueueCreateDesignations` can keep ownership of the larger scan operation, but
it should await an accessway handle instead of owning global access-search
cancellation. A newer scan supersedes the older scan's handle through its owner
key. Planned-tower access is a request kind/result belonging to that scan; it no
longer reads a global last plan.

The outer coroutine may continue to yield while the handle is non-terminal.
The manager, ticked independently every frame, advances the search.

### Farming preparation and filling

`EnsureFarmingAccessForCurrentPhase` must stop returning a synchronous boolean
that can trigger immediate ramp generation. Its conceptual result becomes:

- `Ready`: current work is reachable;
- `Pending`: a matching access request is queued/active or newly enqueued;
- `Blocked`: a terminal non-retryable result applies to the current fingerprint.

The farming session stores only the current handle ID/key and a derived status
summary. It continues its normal state machine when the matching request
completes and the next live check confirms access. Preparation and filling use
different owner keys because their work protos, reservations, and readiness
rules differ.

Do not search every inaccessible cluster to completion in one farming pass. The
request may contain the current cluster set, but scheduling and commits are
bounded. After a successful commit, farming re-evaluates live reachability and
submits only the remaining obligation.

### Farm Placement Assist and Construction Assist

The placement-intent batch remains the durable workflow owner. It does not wait
on a raw coroutine and it does not replay merely because an access search found
a route. Its farming/leveling sub-process requests access through the manager,
then continues to wait until all covered tiles satisfy their real ready
predicates.

This preserves the important separation:

```text
Placement intent: "prepare these tiles, then replay this batch"
Access request:    "provide access for this current phase and world fingerprint"
```

One placement intent may issue several short-lived access requests as terrain
changes. Removing or timing out an access request must not lose the pending
placement intent.

## Runtime ownership and save removability

`ATDAccesswayManager` is created per world generation and destroyed/reset with
the ATD ticker/runtime state. Its queue, sessions, snapshots, handles, timers,
and results are non-saveable runtime state.

Construction Assist may continue persisting its pending placement batches
through the existing config-backed state path. On load, farming/construction
state re-derives whether access is needed and enqueues fresh requests. No
manager-owned type, request, snapshot, or result enters the vanilla save.

## Diagnostics

Use one concise lifecycle log shape, guarded at the appropriate verbosity:

```text
[ATD Access Manager] id=42 owner=farm-prep/tower:77674 state=queued priority=normal dedupe=new
[ATD Access Manager] id=42 state=searching sliceMs=1.8 visited=320 pending=941 activeMs=37
[ATD Access Manager] id=42 state=stale reason=designation-fingerprint-changed retry=1/2
[ATD Access Manager] id=43 state=succeeded searchMs=84 prepareMs=6 commitMs=2 placed=7
```

Aggregate periodic health output should include queue depth, oldest queue age,
active request, per-phase time, cancellations, timeouts, stale retries, and
coalesced request count. Farming performance rows should report access-manager
status instead of charging an entire synchronous search to one farming pass.

Useful console diagnostics:

- dump queued/active/recent requests;
- cancel one request by ID;
- clear only expired/superseded work;
- temporarily override frame budget and node quantum;
- show the owner key/fingerprint responsible for coalescing.

## Suggested implementation sequence

1. Add request/handle/result types and a runtime manager with no production
   callers. Add deterministic scheduler fixtures.
2. Adapt `RunExperimentalAccessDryRunSliced` to return request-scoped results;
   remove dependence on the global last-result fields for the adapted path.
3. Route Create Designations and planned-tower access through the manager while
   preserving existing gameplay and progress UI.
4. Route farming access through the manager and remove the synchronous
   `CreateAccessRamp` drain from farming. This is the step expected to address
   the reported severe spikes.
5. Add stale-result validation, bounded retries, queue backpressure, and
   manager health diagnostics.
6. Integrate the future Construction Assist leveling facet through the same
   owner/handle contract.
7. Profile snapshot preparation and commit. Incrementalize any phase that can
   still exceed the frame budget.
8. Only after a purity/thread-safety audit, consider a worker-thread search
   backend behind the same manager interface.

## Acceptance criteria

- Farming can enqueue a large V1 or V2 access search without completing it in
  the same farming tick.
- Measured manager work stays within the configured frame budget except for
  separately identified non-incremental operations.
- Repeated farming ticks do not create duplicate searches for unchanged work.
- A new Create Designations request supersedes only its older request.
- Cancelling an interactive request does not affect farming or another tower.
- World reset leaves no queued request, running session, callback, toast, or
  request-scoped result alive.
- A terrain/designation change during search prevents stale plan placement and
  causes at most the configured number of retries.
- A timed-out or stalled request places no partial designation plan.
- Construction/Farm Assist pending batches survive access-request turnover and
  replay only after their actual ready predicates pass.
- Save/load reconstructs access demand from live/runtime-derived workflow state;
  no manager state is serialized.
- Existing access-search fixtures still choose the same routes and costs.

## Open questions for implementation review

1. Which snapshot collectors can exceed the frame budget and need incremental
   cursors before farming is migrated?
2. Can all `AccessSearchSnapshot` delegates be made pure data lookups, or must
   search remain permanently main-thread-only?
3. Should normal-priority requests be strictly single-active, or can later
   profiling prove that round-robin sessions are safe and beneficial?
4. What initial frame budget is acceptable at each game speed and while paused?
5. Which failures are genuinely retryable without a changed work fingerprint?

