# Accessway Manager Presentation

Status: conceptual design for later grilling; not approved or implemented.

Related design: [ATD Accessway Manager](atd-accessway-manager.md).

## Summary

The Accessway Manager should present one runtime state through three player-
selectable phenotypes:

- **Icon**: a persistent, repositionable, low-interruption status surface;
- **Toast**: the current progress-oriented default, with clearer terminology;
- **Window**: a detailed inspector for current work, queued work, recent
  outcomes, diagnostics, and safe controls.

These are three views of one manager, not three implementations of manager
behavior. Scheduling, cancellation, suppression, queue ownership, diagnostics,
and save boundaries remain inside the manager and its owning workflows. The UI
reads immutable presentation state and submits explicit commands through one
small interface.

The primary UX goals are to serve three overlapping audiences without making
any one of them carry the others' complexity:

- players who want only reassurance that automation is idle or busy;
- players who want useful progress without permanent HUD occupancy;
- testers and advanced players who need enough observability to understand
  long searches, queue behavior, retries, stalls, and scheduling decisions.

## Context

The current implementation shows a progress toast after the active managed
request has accumulated 250 ms of processing time. It reports the current
phase, work type, visited-node count, pending-node count, frame budget, and
processing-time limit. It also offers actions to stop automatic farming access
or hide the toast until the current work completes.

This was appropriate while the manager had one automated caller and little
queue activity. As more access workflows migrate to the manager, a single
toast becomes too shallow:

- it cannot distinguish the pathfinder's open-node frontier from the manager's
  request queue;
- it has no place to explain why work exists or which tower owns it;
- hiding one request does not define behavior for later queued requests;
- cancelling one attempt can be misleading when the owning workflow can
  recreate its continuing obligation;
- health and terminal diagnostics exist, but only logs expose them;
- testers cannot inspect queue order, scheduling state, or recent outcomes in
  game.

At the other extreme, automatically opening a full diagnostic window for every
background search would be disruptive. The presentation therefore needs to
scale in information density without changing manager behavior.

## Goals

1. Offer Icon, Toast, and Window views over the same authoritative manager
   state.
2. Keep Toast as the initial default for existing players.
3. Keep Icon visible while Icon mode is selected, including while the manager
   is idle.
4. Let the player reposition the Icon so it can coexist with crowded,
   heavily-modded HUDs.
5. Make all three views use the same terminology and status interpretation.
6. Give the Window enough detail to support ordinary troubleshooting and live
   testing without requiring log inspection for every question.
7. Expose only controls whose consequences the manager and owning workflows
   can honor predictably.
8. Preserve ATD save removability: presentation and manager runtime state must
   not enter the vanilla save.
9. Keep the scheduler independent of UI lifecycle, rendering failures, and
   player presentation preferences.

## Non-goals

- Do not redesign accessway pathfinding, priority, fairness, retry, or commit
  policy as part of the presentation work.
- Do not make UI visibility control whether the manager advances.
- Do not expose raw mutable queue entries or live work objects to UI code.
- Do not promise arbitrary cancellation of an attempt when its owning workflow
  still has an unsatisfied obligation.
- Do not initially provide drag-and-drop queue reordering or unrestricted
  priority editing.
- Do not persist request handles, queue contents, recent outcomes, timings, or
  open managed work in either vanilla or mod-owned world state.
- Do not create a universal cross-mod HUD layout manager. ATD should coexist
  politely through adjustable placement, clamping, and sensible defaults.

## Terminology

### Manager state

Manager state describes work, independently of what is visible:

- **Idle**: no active request and no queued request;
- **Busy**: an active request, queued work, or both;
- **Paused**: eligible work exists but player or runtime policy has suspended
  advancement;
- **Attention**: a recent unexpected terminal outcome is worth inspecting.

"Busy" is intentionally broader than "has an active request." A request may be
queued while no request is currently advancing, especially at suspension or
transition points. A binary Icon based only on active-request presence would
incorrectly report such a manager as inactive.

### Presentation state

Presentation state describes the currently selected view:

- **Icon**;
- **Toast**;
- **Window**.

The implementation should call this `PresentationMode` or `ViewMode`, not
`Phenotype`. Phenotype is useful product-language for the concept, while
"presentation mode" is clearer in code and configuration.

### Search frontier and request queue

These must never share the label "queue":

- **Open nodes** or **search frontier**: pathfinder nodes waiting to be
  evaluated, currently represented by `PendingNodes`;
- **Waiting requests** or **request queue**: manager-owned access obligations
  waiting behind the current request, currently represented in aggregate by
  `QueueDepth`.

For example:

```text
12,430 visited · 941 open nodes · 2 requests waiting
```

## Core design

### One presentation seam

The UI should depend on one immutable presentation snapshot rather than query
manager internals differently for each view. Conceptually:

```text
ReadPresentationSnapshot()
  -> manager status
  -> active request summary, if any
  -> ordered waiting-request summaries
  -> bounded recent-outcome summaries
  -> presentation-relevant capabilities
```

The snapshot is a value projection, not a collection of handles or live work
objects. Icon, Toast, and Window are adapters at this seam. This keeps the
presentation module deep: status interpretation, terminology, capability
decisions, and diagnostic shaping are implemented once behind a small
interface.

The UI submits explicit commands through the same seam, conceptually:

```text
ApplyPresentationCommand(command)
  -> accepted or rejected with a stable reason
```

The exact C# shape is deferred. A command value avoids growing a shallow set of
view-specific manager methods, but only commands that actually exist should be
modeled.

### Orthogonal state

Manager and presentation state must remain orthogonal:

```text
Manager:       Idle | Busy | Paused | Attention
Presentation:  Icon | Toast | Window
```

A presentation transition does not cancel, pause, reprioritize, or restart
work. A manager transition does not force a different phenotype, except for
the Toast's documented activity-based show/hide behavior.

### Proposed transitions

- Toast **Minimize** -> Icon, remembering Toast as the restore target.
- Toast **Details** -> Window.
- Window **Minimize** -> Icon, remembering Window as the restore target.
- Window **Compact** -> Toast.
- Icon click -> the remembered non-Icon target.
- A direct settings choice -> that chosen phenotype and an updated restore
  target where applicable.

"Minimize" replaces the current "Hide" label. Hiding until one request
completes is no longer a stable concept once the manager has a queue and
continuing obligations.

The restore target is presentation preference only. It does not identify or
retain the request that happened to be active when the view was minimized.

## Icon phenotype

### Purpose

Icon is the durable, low-information surface. It answers the first player
question immediately: is Accessway Manager idle or does it have work?

When Icon mode is selected, the Icon remains visible while a world and normal
HUD are available, including while the manager is idle. It should respect
global HUD suppression, cinematic modes, loading transitions, and UI teardown;
"persistent" does not mean drawing over screens where the game itself has
hidden gameplay UI.

### Status language

The primary shape should communicate idle versus busy without relying on
color. Small secondary badges may express exceptional state:

- outline or resting glyph: Idle;
- filled glyph or restrained activity treatment: Busy;
- pause badge: Paused;
- warning badge: Attention.

Animation should be subtle and optional. A permanent spinner is likely to
become distracting during long searches. Color may reinforce status, but must
not be the only signal.

Hover text can add nuance without increasing permanent HUD occupancy:

```text
Accessway Manager: searching
Farming preparation access · 2 requests waiting
Click for details
```

### Adjustable placement

Players commonly run enough mods that any fixed HUD location will collide with
someone else's UI. Icon placement is therefore a requirement, not later polish.

Working assumptions:

- dragging beyond a small movement threshold repositions the Icon;
- a click below that threshold expands it, avoiding separate drag and click
  targets on a small surface;
- placement is stored as screen-relative position plus a preferred edge or
  anchor, rather than raw pixels alone;
- the Icon may snap gently to screen edges but must also support free placement
  if the UI toolkit permits it cleanly;
- the resolved position is clamped to the current safe viewport after
  resolution, window-mode, aspect-ratio, or UI-scale changes;
- the Icon cannot be dragged fully off-screen;
- settings offer **Reset icon position** even if drag state becomes invalid;
- a reposition gesture never changes manager or restore state.

Persist the placement in ATD's user/mod configuration, not the world save. The
position is a player UI preference and should follow the player across worlds.
If ATD configuration is shared across machines or resolutions, clamping and
screen-relative storage must make a foreign position recoverable.

The initial default position should follow a normal game HUD edge and avoid
known vanilla controls, but no default can guarantee compatibility with other
mods. The ability to move and reset it is the compatibility mechanism.

### Open positioning choices

The later grilling should decide:

- normalized free position versus anchor-plus-pixel-offset;
- whether edge snapping is mandatory, optional, or absent;
- whether dragging is always enabled or requires an explicit reposition mode;
- whether the Icon needs a size or opacity preference;
- whether position is shared by all UI scales or stored per scale bucket;
- whether the Toast or Window should originate visually from the Icon's
  current location.

## Toast phenotype

### Purpose and lifecycle

Toast remains the initial default for existing players. It provides useful
progress without reserving permanent HUD space while idle.

Working lifecycle:

- while Toast mode is selected, no Accessway Manager surface is shown when the
  manager is idle;
- after meaningful work passes a short delay, the Toast appears;
- it updates in place while the active request changes;
- it disappears after the manager becomes idle, subject to a brief completion
  acknowledgement if later desired;
- **Minimize** changes the selected phenotype to persistent Icon rather than
  merely suppressing the current request;
- **Details** opens Window;
- **Stop** invokes an owner-level operation with explicit semantics.

The existing 250 ms processing threshold is a reasonable starting point but is
a tuning parameter, not architectural policy.

### Proposed content

The Toast should show:

- a human-readable work type;
- tower or placement context when available;
- current phase;
- visited and open-node counts where they are meaningful;
- number of waiting manager requests;
- accumulated processing time and limit;
- selected frame budget when useful;
- Minimize, Details, and a contextually correct Stop action.

Raw owner keys and work fingerprints do not belong in the default Toast.

## Window phenotype

### Purpose

Window is both a player-facing explanation surface and a live observability
tool. Its first layer should remain understandable without knowing pathfinding
internals; tester details belong behind expandable diagnostics.

### Current work

Show, where available:

- human-readable workflow and phase;
- tower, placement, or owning context;
- request ID, kind, and priority;
- queue age, active wall time, and processing time as distinct clocks;
- visited nodes, open nodes, and configured visited-node limit;
- current frame budget and paused/running envelope;
- current cancellation or suppression capability;
- stale, superseded, retry, or validation state when relevant.

Do not manufacture a percentage from visited and open nodes. The search does
not necessarily know its final work size, so such a percentage would imply
accuracy it does not possess.

### Waiting requests

Show requests in actual selection order with:

- work type;
- tower or owning context;
- priority;
- waiting time;
- reason or obligation phase;
- paused, suppressed, dirty, or retry eligibility where applicable.

The manager currently exposes only aggregate queue depth to runtime UI. A
detailed Window therefore requires a read-only queue projection. UI code must
not enumerate or retain the manager's mutable queue entries.

### Recent activity

Maintain a small, runtime-only ring buffer of terminal summaries, for example
the most recent 20 outcomes:

- succeeded;
- cancelled;
- superseded;
- stale;
- timed out;
- queue overflow;
- failed or stalled.

This gives testers continuity when requests complete too quickly to inspect and
lets ordinary players answer "what just happened?" without opening logs. The
history is cleared on world reset and is not saved.

Expected routine outcomes should not create a persistent Attention state.
Unexpected failures may set Attention until the Window is inspected or the
condition is superseded by later healthy activity. The exact acknowledgement
rule remains to be grilled.

### Controls

Initial player controls should be conservative:

- **Pause/Resume automatic access processing**, if the manager gains an
  explicit runtime pause state;
- **Stop this automatic obligation**, delegated to the owning workflow;
- **Minimize** to Icon;
- **Compact** to Toast;
- **Reset icon position** or open the relevant settings;
- **Copy diagnostics** for support and testing.

Potential diagnostics-mode controls:

- toggle relevant terrain/search overlays;
- request a safe retry now when owner policy supports it;
- inspect raw request IDs, owner keys, reason codes, fingerprints, and phase
  timings;
- export a bounded diagnostic summary.

Do not initially expose:

- arbitrary cancellation that merely causes immediate re-enqueue;
- deletion of queue entries without owner suppression;
- manual mutation of internal priority values;
- unrestricted queue reordering;
- forcing commit without normal validation.

The snapshot should expose capabilities for each action so the Window can hide
or disable commands the current request cannot honor. The owning workflow,
rather than the Window, defines what "Stop" means.

### Window idle behavior

Window remains useful while idle because it can show recent outcomes and an
empty manager state. Whether Window automatically reopens on a later game or
world load is deliberately unresolved. Two plausible policies are:

1. persist Window as the selected phenotype and reopen it automatically;
2. remember Window only as Icon's restore target and start new sessions in
   Icon to avoid an unsolicited large surface.

This is a prime grilling question because tester convenience and ordinary
player expectations pull in opposite directions.

## Presentation snapshot

The conceptual snapshot needs enough information to serve all three adapters
without leaking scheduler implementation. Candidate shape:

```text
AccesswayManagerPresentationSnapshot
  ManagerStatus
  PresentationCapabilities
  ActiveRequest?
  WaitingRequests[]
  RecentOutcomes[]
  QueueDepth
  OldestQueueAge
  CurrentBudget
  IsGamePaused
  IsSuspendedForInteractiveWork
```

Each request summary may contain:

```text
RequestId
Kind
Priority
PlayerFacingContext
State
Phase
QueueAge
ActiveWallTime
ProcessingTime
VisitedNodes
OpenNodes
Limits
StableReasonCode?
Capabilities
DiagnosticContext?
```

`PlayerFacingContext` should not be parsed from `OwnerKey`. Requests or their
owners need to supply stable display metadata such as tower identity and a
localizable context kind. Localization itself remains in the UI adapter rather
than the scheduler.

The exact shape should be designed against real Icon, Toast, and Window needs.
Fields used only for log formatting should not automatically become part of
the presentation interface.

## Presentation preferences

Candidate configuration:

```text
PreferredPresentationMode = Toast
IconAnchorOrNormalizedPosition
IconOffset
IconSnapPreference
RestoreMode = Toast | Window
```

Only genuine player preferences belong in configuration. Transient facts such
as whether a request is active, which request the Toast currently displays,
whether Attention has been acknowledged, or which Window row is expanded do
not belong in persisted world state.

The relationship between `PreferredPresentationMode`, current runtime mode,
and `RestoreMode` needs one deterministic transition table before
implementation. Avoid storing several booleans such as `toastHidden`,
`windowOpen`, and `iconVisible`; contradictory combinations would create a
shallow and error-prone interface.

## Save removability and lifecycle

All presentation objects are runtime-only. Before an in-place save:

- manager advancement remains suspended as already designed;
- Toast, Icon, Window, transient history surfaces, and any ATD-owned
  notifications are removed or purged as required;
- no UI object, request summary, or active notification referencing an
  ATD-owned proto enters the vanilla save.

After save completion, the runtime may recreate the selected presentation from
live manager and owner-derived state. Restoration must not replay sounds or
depend on saved request state.

On world unload or generation reset:

- presentation snapshots and recent history are discarded;
- UI adapters release all references;
- configuration-backed presentation preferences and Icon placement remain;
- the next world derives fresh manager state.

## Failure behavior

Presentation is observational. If any adapter throws or cannot attach to the
game UI:

- manager advancement continues;
- the failure is logged once at an appropriate diagnostic level;
- repeated per-frame UI construction failures are rate-limited or latched;
- another presentation adapter must not inherit partially constructed state;
- save-boundary cleanup remains best-effort and idempotent.

A broken Window must not break Toast, Icon, request completion, or commit.
Adapters may share rendering helpers, but their lifecycles should be isolated
enough that one failed surface can be torn down independently.

## Accessibility and coexistence

- Status must not rely on color alone.
- Tooltips and controls use localizable player language.
- Click and drag behavior must tolerate UI scaling.
- The Icon must remain reachable after resolution and scale changes.
- The Window must stay within the safe viewport and support ordinary game
  window movement if the toolkit provides it.
- Update frequency should be bounded; text need not be rebuilt every rendered
  frame when the displayed values have not materially changed.
- Long identifiers and diagnostic strings must be truncated in the normal view
  and copyable in diagnostics.
- Icon placement must not assume an otherwise unmodded HUD.

## Verification direction

The presentation seam should permit deterministic tests without Unity UI:

- map Idle, Busy, Paused, and Attention manager conditions to a snapshot;
- distinguish open nodes from waiting requests;
- verify every presentation transition and remembered restore target;
- distinguish click from drag at the movement threshold;
- clamp stored Icon positions across aspect-ratio and UI-scale changes;
- recover from invalid or missing Icon configuration;
- ensure Stop capability follows owner semantics;
- ensure automated cancellation cannot masquerade as owner suppression;
- bound and clear recent history on world reset;
- ensure save preparation purges all three adapters;
- ensure UI failure does not change manager scheduling or results.

Manual testing should include a crowded modded HUD, multiple resolutions,
windowed/fullscreen transitions, non-default UI scales, pause/unpause, save
during active work, rapid request turnover, long queues, and interactive work
temporarily suspending the automated manager.

## Suggested implementation sequence

1. Define one presentation-state projection and correct the existing
   open-nodes/request-queue terminology.
2. Refactor the current Toast to render that projection without changing its
   scheduling behavior.
3. Add persistent Icon mode, click/drag discrimination, configurable placement,
   viewport clamping, and reset behavior.
4. Add a read-only Window showing current work and the ordered request queue.
5. Add bounded recent outcomes and expandable tester diagnostics.
6. Add only the owner-level and manager-level controls whose semantics are
   explicitly supported.
7. Revisit persistence and automatic reopening behavior after live testing.

## Decisions proposed for approval

- Icon, Toast, and Window are presentation modes over one manager, not separate
  manager behaviors.
- Toast remains the initial default.
- Icon remains visible while Icon mode is selected, even when idle.
- Icon position is player-adjustable and configuration-backed.
- Manager status and presentation mode are orthogonal.
- Icon reports Idle versus Busy, with badges for Paused and Attention, rather
  than reporting active-request presence alone.
- "Queue" is reserved for manager requests; pathfinder pending nodes are
  labeled "open nodes" or "search frontier."
- Minimize replaces Hide and remembers Toast or Window as the Icon restore
  target.
- Window reads immutable queue and history projections; it never retains live
  manager entries.
- Stop is an owner-level semantic action, not blind cancellation of an attempt.
- Recent outcomes are bounded, runtime-only, and cleared on world reset.

## Questions for later grilling

### Player experience

1. Should selecting Window persist across sessions, or should a new session
   start in Icon with Window remembered as the restore target?
2. Should Toast return automatically after Window is closed, or should close
   mean Icon?
3. Should a completed request briefly remain visible in Toast, and for how
   long?
4. Should Attention survive later successful work, require explicit
   acknowledgement, or clear automatically?
5. Is Idle/Busy plus Paused/Attention enough Icon state, or does the Icon need
   to distinguish searching from validating and committing?

### Icon placement

6. Should position use normalized coordinates, anchor-plus-offset, or an edge-
   docking model?
7. Should the Icon snap to edges by default?
8. Is always-on drag discoverable enough, or should settings provide an
   explicit **Reposition icon** action?
9. Does click-versus-drag discrimination conflict with game camera gestures or
   common mod UI behavior?
10. Are size and opacity settings necessary, or avoidable option growth?
11. Where is the least harmful initial default position in an unmodded HUD?

### Information design

12. What is the smallest useful Toast after tower context and true request
    queue depth are added?
13. Which phase and timing details belong in the ordinary Window versus an
    expandable diagnostics section?
14. Does the manager have enough stable player-facing context today, or must
    requests gain explicit display metadata?
15. What recent-history length is useful without making Window feel like a log
    viewer?

### Control semantics

16. Does Pause suspend only automated access, or all managed access after
    interactive migration?
17. Is Pause runtime-only, or should the preference survive world changes?
18. What exact owner state does **Stop this automatic obligation** set for each
    request kind?
19. Which requests may safely expose **Retry now**?
20. Are queue reordering and priority overrides genuinely useful testing tools,
    or would logs and deterministic fixtures provide better evidence?

### Architecture and lifecycle

21. Should recent terminal summaries live in the manager, the presentation
    projection module, or a diagnostic observer feeding that module?
22. How frequently should presentation snapshots update, and should each
    adapter choose its own render cadence?
23. Can Toast, Icon, and Window share enough implementation for locality
    without coupling their failure lifecycles?
24. What UI teardown and reconstruction hooks are authoritative across save,
    HUD suppression, world unload, and resolution changes?
25. Does any proposed preference accidentally become world state or create a
    mod-removal dependency?
