# ATD Accessway Manager

Status: approved design; implementation steps 1-4 complete. Adaptive budgeting
has a preserved prototype but is intentionally unwired until step 11. Interactive
migration and the remaining heavy-phase incrementalization remain.

Decision record: [Coordinate accessway work through one cooperative manager](../../adr/0003-coordinate-accessway-work-through-one-cooperative-manager.md).

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

The motivating tester trace used ATD v0.5.6 and recorded 2,550 failed cluster
searches for one tower in 63 synchronous bursts over 13 minutes 33 seconds. The
median burst held one simulation step for 4.93 seconds and the longest for 8.67
seconds. One unchanged cluster was searched as many as nine times in one step.
The immediate cause was the farmland fixpoint loop draining several complete
searches and placement passes synchronously; warning stack traces amplified the
log volume but were not exceptions and were not the primary stall.

## Releaseable mitigation before the manager

The first implementation slice deliberately retains current route selection
and designation placement. When one farming access search returns `Failed` or
`NotAccessible`, the current fixpoint stops immediately and records a transient
failure against the farming phase's access obligation fingerprint.

No retry is eligible for 10 seconds of unscaled real time. From 10 through 60
seconds, changes visible in the current work, tower area, ramp width, or vehicle
clearance fingerprint may reopen it; at 60 seconds it retries even without a
detected change, covering terrain events and other-mod mutations not visible to
this interim fingerprint. Farming suppresses per-cluster warning stacks and
emits one request-level informational summary instead.

This is a recurrence mitigation, not cooperative scheduling. One legacy search
can still monopolize its simulation step. The farming-first manager slice below
removes that remaining hitch by replacing synchronous generation with bounded
new-planner work.

A successfully placed accessway remains one pending farming obligation while
any compatible generated designation is still present. Current-terrain
reachability cannot invalidate projected work before simulation fulfills it.
If the complete generated plan disappears or is replaced while the farming
work remains inaccessible, the obligation enters the same bounded retry policy
instead of immediately launching another search.

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
8. Direct player work takes precedence over derived farming, Construction
   Assist, maintenance, and retry work without allowing unrelated interactive
   owners to cancel each other.

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
- Do not fall back to synchronously draining a search when manager preparation,
  scheduling, or enqueueing fails.

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

`OwnerKey` identifies the stable access obligation, not an individual attempt
or mutable work key. Its useful shape is `(kind, owningWorkflowId,
towerEntityId, phase)`, omitting components that change merely because terrain
work progressed. Enqueuing the same key and fingerprint returns the existing
handle. Enqueuing the same key with a new fingerprint supersedes or dirties the
old attempt; it never creates a second live request for the same obligation.
Different towers and different farming phases remain independent.

The owning workflow interprets progress, terminal results, and user
cancellation. A request ending does not itself end its continuing access
obligation.

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

Approved initial policy:

- exactly one active access request; other requests remain queued rather than
  round-robin interleaving live search sessions;
- managed work adapts within a 1-15 ms range while simulation is running and a
  1-30 ms range while paused, backing down when frame timing deteriorates;
- direct interactive work retains strict scheduling priority when it migrates
  to the manager, but uses the same adaptive envelope once selected;
- a node quantum larger than the current `Step(1)`, adjusted downward/upward
  from measured slice time;
- direct interactive Create Designations, Mining Designations, and accessway
  requests have strict highest priority;
- farming and Construction Assist are derived work and have normal priority,
  even when Construction Assist originated from a player placement;
- maintenance/retry work has low priority;
- aging orders requests only within one priority class and never raises derived
  work above newly submitted direct interactive work;
- a new interactive request preempts active derived work at the next slice
  boundary; the cancelled derived obligation becomes immediately eligible after
  interactive work ends;
- equal-priority interactive requests from different owners queue FIFO and do
  not cancel one another;
- after one derived request commits one accessway or otherwise terminates, its
  continuing obligation rejoins the back of the eligible queue;
- at most one commit transaction per frame.

The legacy `accessSearchFrameBudgetMs` setting is deprecated rather than mapped
from its unsafe 30 ms default. Until adaptive scheduling is revisited in step
10, the automated manager retains its fixed 10 ms running budget and 30 ms
paused budget. The interactive 15 ms setting is retained for compatibility
while interactive migration is pending.

The time budget must be checked between bounded node quanta. A stopwatch around
an unbounded operation cannot prevent a spike that already occurred. Snapshot
capture and materialization must be measured separately because search slicing
does not make those phases cheap.

### Adaptive frame-budget policy (prototype parked)

The intended controller observes
rendered-frame cadence, measures ATD's own elapsed work separately, and keeps a
slow-biased estimate of non-ATD time. Healthy frames gradually probe for spare
capacity; slow frames, high external cost, and ATD slice overruns cut the next
allowance immediately. Future request kinds inherit this policy when they
migrate to the manager. Its prototype and deterministic tests are preserved,
but the runtime path remains on fixed budgets until the controller learns a
real baseline instead of treating 60 FPS as universal headroom.

Approved operating envelope:

- never allocate less than 1 ms to an eligible active request; continued slow
  progress under load is intentional;
- cap unpaused work at 15 ms per rendered frame;
- cap paused work at 30 ms per rendered frame, accepting approximately 30 FPS
  UI responsiveness while long paths run and using otherwise idle CPU;
- reduce the budget quickly after frame-time deterioration or an ATD overrun,
  but restore it gradually and with hysteresis when sustained headroom returns;
- keep independent running and paused estimates, and reseed or strongly
  discount stale measurements after pause transitions, speed changes, world
  loads, long callback gaps, or other discontinuities;
- priority remains a queue-selection rule: interactive work runs before farming
  and Construction Assist, but all managed work uses the available adaptive
  envelope once selected;
- the hard caps remain configurable expert limits, while diagnostics report the
  selected budget, observed ATD time, estimated non-ATD time, reductions,
  recoveries, and cap/floor hits.

The controller must not infer headroom from instantaneous FPS alone. Frame
limiting, intentional engine sleep, simulation speed, and paused updates can all
make callback intervals look cheap or expensive for different reasons. Its
rolling estimate should favor recent slow frames, react asymmetrically (fast
decrease, slow increase), and be verified against fixed-budget mode. The exact
filter, safety margin, increase rate, and decrease factor remain tuning details;
the 1/15/30 ms envelope and prompt backoff under stress are policy.

### Preparation and commit budgets

The manager should expose three separately measured phases:

1. **Prepare**: capture building/designation/pathability inputs and create the
   snapshot/session.
2. **Search**: call `AccessPathSearchSession.Step(nodeQuantum)` until terminal.
3. **Validate and commit**: materialize/revalidate, then place or clean up the
   complete accepted plan transactionally.

Preparation, search, validation, and dry-run materialization must be resumable
or demonstrated to be bounded. The final world mutation remains one small
atomic transaction: cancellation is accepted through validation, but once the
request enters `Committing`, it finishes or rolls back. If a supposedly bounded
operation exceeds 30 ms during stress testing, it is a release blocker and must
be split or optimized. Search work must not move to a worker thread merely to
hide an unsliced main-thread phase.

Paused frames may advance preparation, search, and validation with the higher
adaptive budget. A completed plan is revalidated and its single atomic mutation
is dispatched through the simulation-safe command-processing boundary, at most
one commit per callback. Farming phase progression itself may wait for normal
simulation updates.

## Coalescing, supersession, and backpressure

### Coalescing

- Same `OwnerKey` + same `WorkFingerprint`: return the existing handle.
- Same `OwnerKey` + different fingerprint: mark the old request superseded and
  enqueue the new request.
- A farming tick that sees an already queued or active request records
  `Pending`; it does not start another ramp search.
- `NoCandidate` is negatively cached under a bounded event-assisted retry
  policy. No automated retry is eligible for 10 seconds of unscaled real time.
  From 10 through 60 seconds, a known relevant event may reopen the obligation.
  At 60 seconds one retry is allowed even without a detected event. Another
  failure restarts the window; explicit direct player work may bypass it.
- Known world events are spatially filtered through a retry watch region
  covering the tower area, source clusters, providers, and configured search
  margin. Initial triggers are terrain-height changes; designation add, remove,
  replacement, fulfillment, and reachability changes; relevant entity
  construction/removal; manager commits; and owner phase, tower area, access
  mode, clearance, or relevant setting changes. Tree/prop events participate
  where reliable hooks exist; the 60-second retry covers undetectable changes
  and other-mod mutations.
- Hard identity changes cancel an active attempt at the next slice boundary.
  Environmental changes in the watch region mark its snapshot potentially
  stale; search may finish, but live validation is authoritative before commit.
  `NoCandidate` from a dirtied snapshot is not trusted as a stable negative.

### Queue bounds

The queue should have both a global limit and a per-owner limit. On overflow:

1. remove superseded and expired entries;
2. coalesce duplicate owners;
3. drop the oldest low-priority derived request;
4. never silently drop the newest interactive request.

Dropping a derived request is safe only because its obligation remains live and
will enqueue again through the bounded retry policy. Every dropped request gets
a diagnostic terminal result. Queue size, toast delay, adaptive ramp rate,
stall-quantum count, and warning thresholds are conservative tuning parameters,
not architectural policy.

The implemented manager permits 32 pending requests plus the single active
request. Owner-key coalescing enforces one live request per owner. At capacity,
the oldest queued request in the lowest priority class no higher than the
incoming request is completed as retryable `QueueOverflow`; a lower-priority
arrival cannot evict interactive work, and the newest interactive request is
never silently discarded.

### User cancellation

Progress cancellation delegates to the request owner rather than cancelling one
attempt that would immediately requeue. Create Designations cancels its owning
operation; future Construction Assist cancels its placement intent; current
farming suppresses automatic access attempts until the user explicitly
reactivates the obligation. Cancellation never affects another owner.

## Timeout and stall policy

Track these clocks separately:

- **queue age**: time waiting before activation;
- **active wall time**: unscaled time since activation;
- **processing time**: time actually spent in prepare/search/validation;
- **last progress**: last change in visited nodes, pending nodes, phase, or
  terminal state.

Approved terminal rules:

- expire queued work whose owner/fingerprint is no longer current;
- interpret `accessSearchTimeoutSeconds` as cumulative processing time rather
  than wall time; the default remains 60 processing seconds even when low-
  priority slicing spreads them across several wall-clock minutes;
- classify it as stalled when several scheduled quanta make no observable
  progress;
- cancel every request on world-generation reset;
- use a small, bounded retry count only for explicitly retryable stale-input or
  transient failures.

A healthy request may remain active for multiple wall-clock minutes. Queue age,
active wall time, processing time, and last progress remain distinct metrics;
slow CPU allocation alone is not failure. The visited-node limit remains an
independent deterministic work cap.

A request cannot be forcibly killed in the middle of one synchronous method
call. "Kill stalled requests" therefore requires every heavy phase to yield at
safe boundaries. If one node expansion, snapshot collector, or materialization
step can itself take hundreds of milliseconds, that operation needs its own
incremental API.

Interactive preemption and fingerprint supersession permit immediate
replacement and do not incur failure backoff. `NoCandidate`, node/processing
limit, stall, queue overflow, and exhausted stale validation enter the bounded
retry policy. User cancellation suppresses its owner; owner completion and
world reset terminate without retry.

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

For managed farming, the manager invokes a cheap live-validation callback before
activation and before every cooperative slice. World generation, owner/session
identity, tower area, tower access settings, and the global access-planning
settings fingerprint are checked directly. A bounded transient designation
mutation journal spatially filters additions, replacements, removals, and
fulfillment against the tower/search watch region; overflow fails
conservatively. Only a relevant mutation rebuilds the full farming-work
fingerprint. Changed work or nearby provider work completes as retryable
`Stale`; completed or removed ownership completes as non-retrying cancellation.
Because world mutation cannot interleave on the same game thread after this
check and before the slice returns, the final search slice reaches placement
under the validation performed at its start. Placement retains its existing
live terrain/conflict checks and rollback.

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

Do not search every inaccessible cluster to completion in one farming pass. One
request represents the stable tower-and-phase obligation and may cooperatively
test several clusters and both required route backends, but it commits at most
one newly generated accessway. It then terminates; farming re-evaluates live
reachability and submits only the remaining obligation. This preserves the
ability to try a farther cluster when a nearer one cannot yet connect without
mutating through several fixpoint passes in one request.

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

Before an in-place save, manager advancement is suspended and transient
progress UI is purged. After save, the same runtime restores owner-derived UI,
revalidates, and resumes. Loading or unloading a world cancels all old-world
work with diagnostics; live automated owners derive fresh obligations in the
new runtime.

## Diagnostics

Use one concise lifecycle log shape, guarded at the appropriate verbosity:

```text
[ATD Access Manager] id=42 owner=farm-prep/tower:77674 state=queued priority=normal dedupe=new
[ATD Access Manager] id=42 state=searching sliceMs=1.8 visited=320 pending=941 activeMs=37
[ATD Access Manager] id=42 state=stale reason=designation-fingerprint-changed retry=1/2
[ATD Access Manager] id=43 state=succeeded searchMs=84 prepareMs=6 commitMs=2 placed=7
```

Every terminal cancellation records request/owner identity, work type, tower or
placement context, reason, lifecycle phase, queue age, active wall time,
processing time, visited/pending nodes, and retry eligibility. Expected
cancellation (preemption, supersession, owner completion, world reset) logs once
at `Info`; unexpected termination (stall, limits, retry exhaustion, invariant
failure) logs once at `Warning`; ordinary cancellation has no stack trace.

Cluster failures are aggregated into one terminal request warning with attempted,
succeeded, and blocked counts, top reasons, timings, and retry eligibility.
Individual cluster outcomes belong at `Debug` and are not repeated across
passes. A request that committed one accessway while other clusters remain logs
`Info`, because the remaining obligation will be re-evaluated.

Progress UI is an owner-facing generic toast contract, not tower-owned farming
UI. After a short anti-flicker delay it identifies the work type and owner,
shows state, accumulated processing time versus its limit, visited nodes versus
the node limit, and explains queue/wall time and current budget in secondary
detail. It never presents processing time as a wall-clock countdown. Direct
interactive work and derived work use owner-specific cancellation labels.

Aggregate periodic health output should include queue depth, oldest queue age,
active request, per-phase time, cancellations, timeouts, stale retries, and
coalesced request count. Farming performance rows should report access-manager
status instead of charging an entire synchronous search to one farming pass.

The implemented ten-second Debug health row reports active ID, active wall and
processing time, visited/pending nodes, queue depth and oldest age, plus
coalesced, superseded, stale, dropped, and completed totals. Health sampling is
disabled below Debug level. A central terminal observer records every
non-success result, including reset, supersession, validation, cancellation,
and queue-overflow paths, with the complete request-owned context above.

Useful console diagnostics:

- dump queued/active/recent requests;
- cancel one request by ID;
- clear only expired/superseded work;
- temporarily override frame budget and node quantum;
- show the owner key/fingerprint responsible for coalescing.

## Suggested implementation sequence

1. **Implemented.** Release the farming failure mitigation: stop at the first failed search,
   coalesce the unchanged obligation under the 10-to-60-second retry policy,
   and aggregate expected diagnostics without warning stacks.
2. **Implemented.** Adapt `RunExperimentalAccessDryRunSliced` to return request-scoped results;
   remove dependence on the global last-result fields for the adapted path.
3. **Implemented.** Add the runtime manager and route farming access through it using only the
   new planner. Remove the synchronous `CreateAccessRamp` drain from farming;
   temporarily suspend farming work while legacy interactive access is active.
4. **Implemented.** Add stale-result validation, bounded retries, queue
   backpressure, and manager health diagnostics. Farming stale/overflow results
   feed its existing 10-to-60-second owner retry policy; owner completion and
   explicit cancellation remain non-retrying.
5. **Next.** Make farming snapshot and search-session preparation cooperative.
   First stop running deterministic V1/V2 fixture suites for every live
   snapshot: validate them once during initialization, cache the terminal
   result, and fail closed if validation fails. Then move snapshot collection,
   projected-designation analysis, pathability/reachability preprocessing,
   immutable graph construction, and search-session initialization behind
   manager-owned incremental work. The farming toast reports the active phase
   (`Capturing terrain`, `Projecting designations`, `Building navigation`, or
   `Preparing search`) and remains cancellable throughout. Capture records the
   relevant world revisions before its first slice and validates them before
   publishing the immutable snapshot; a changed capture is discarded and
   enters the existing bounded retry policy. Finalization must transfer or
   freeze builder-owned collections without one large defensive-copy step.
6. Route Create Designations and planned-tower access through the manager with
   strict interactive priority and request-owned cancellation.
7. Remove legacy ramp generation, candidate comparison, global result state,
   and every synchronous fallback after all callers have migrated.
8. Integrate the future Construction Assist leveling facet through the same
   owner/handle contract.
9. Profile materialization and commit, and incrementalize any remaining phase
   that can still exceed the frame budget. Keep the final designation mutation
   transactional and atomic even if preparation for that mutation is sliced.
10. Only after a purity/thread-safety audit, consider a worker-thread search
   backend behind the same manager interface.
11. Replace fixed frame budgets with the preserved adaptive-controller prototype:
    1 ms minimum, 15 ms unpaused cap, 30 ms paused cap, fast stress backoff,
    gradual recovery, diagnostics, configuration migration, and deterministic
    timing tests. The prototype and its deterministic tests are preserved but
    intentionally unwired after the initial implementation exposed an incorrect
    fixed-60-FPS baseline assumption.

The farming-first manager slice may release before interactive migration because
it addresses the reported path. During that transition, the existing
interactive-active signal must suspend manager advancement so interactive and
farming searches do not compete. Every managed request fails closed rather than
invoking the old synchronous drain. The legacy generator is removed only after
interactive callers migrate.

The implemented farming slice has one active request, owner/fingerprint
coalescing, strict priority/FIFO queue selection, request-scoped cooperative
cancellation, and runtime-only reset behavior. Preparation and filling keep
separate owner keys. It advances from the rendered-frame ticker under the
fixed 10/30 ms running/paused scheduling envelope, accumulates actual processing time for
timeout accounting, and shows the selected budget in a work-type-specific
progress toast. The toast's stop action suppresses its farming phase until
automation is explicitly disabled and re-enabled.

This slice removes the synchronous farming drain, but its time budget is checked
only between coroutine yields. Snapshot preparation, materialization, or one
`Step(1)` expansion can therefore still exceed the nominal budget. Live farming
tests measured snapshot preparation at 994-1390 ms and search-session creation
at up to 251 ms before the first search yield, despite subsequent search slices
remaining near 5 ms. Implementation step 5 must bring both phases under manager
scheduling; step 9 handles any later materialization/commit overruns.

## Acceptance criteria

- Farming can enqueue a large V1 or V2 access search without completing it in
  the same farming tick.
- Deterministic V1/V2 fixture validation runs once during initialization, not
  once per production snapshot.
- Snapshot and search-session preparation expose bounded cooperative progress,
  cancellation, and phase diagnostics; the measured 124,711-tile farming case
  no longer produces a monolithic pre-search frame stall.
- A world revision change during cooperative capture discards the partial
  snapshot before it can be searched or materialized.
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
- Eligible managed work receives at least 1 ms, adapts no higher than 15 ms
  unpaused or 30 ms paused, and backs down promptly when non-ATD frame cost or
  ATD overruns rise, apart from a separately measured small atomic commit.
- Managed requests use only the new planner and never invoke legacy generation
  or candidate comparison as a fallback. Existing legacy-created designations
  in saves remain valid and are not rewritten by migration.
- Direct interactive work strictly outranks farming and Construction Assist;
  unrelated equal-priority interactive owners queue rather than cancel each
  other.
- Deterministic fixtures cover budgeting, coalescing, preemption, fairness,
  bounded event-assisted retry, staleness, cancellation, save/world reset, and
  backpressure.
- A live multi-cluster farming save reproduces the former multi-pass workload
  and demonstrates that no automated dry-run phase monopolizes a frame. The
  reporter's `(2312, 1737)` save is preferred when available.

## Remaining implementation measurements

1. Which snapshot collectors, legacy-generator phases, materialization steps,
   or individual node expansions exceed their approved slice bounds and need
   incremental cursors?
2. What initial values should be used for queue capacity, toast delay, adaptive
   budget safety margin/filter/increase/decrease rates, stall-quanta detection,
   and warning thresholds within the approved 1/15/30 ms envelope?
3. Which reliable tree/prop/entity events can augment the required spatial
   retry triggers without global invalidation churn?
4. Can a later purity/thread-safety audit prove that immutable graph search is
   safe on a worker thread? Worker-thread execution remains out of scope for
   this fix regardless of that future answer.
