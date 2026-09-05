# Reduced access snapshot implementation plan

Status: core production implementation landed; replay-schema expansion,
real-world qualification, and extended geometry cases remain follow-up work.

This plan implements
[ADR 0008](../../adr/0008-shrink-oversized-access-snapshots-with-geometry-only-masked-corridors.md).
It is a future execution guide for a maintainer or coding agent. The ADR and
`CONTEXT.md` remain authoritative for semantics; this document owns sequencing,
module seams, verification, and completion gates.

## Outcome

When the normal full-area preflight exceeds the retained-memory ceiling, ATD
constructs one request-local, geometry-only reduced domain, captures only its
required facts, and runs the canonical access search against it. Reduced
success follows normal live validation and commit. Reduced failure returns
`ReducedAreaNoPath` and is explicitly inconclusive.

Requests whose full-area snapshot fits must retain their existing capture,
route, cost, materialization, validation, retry, and commit behavior.

## Constraints

- Reduce only after normal preflight reports `SnapshotTooLarge`.
- Shape depends only on source geometry, goal coordinates, managed-area
  geometry, immutable search geometry policy, and a conservative budget.
- Terrain, designations, pathability, buildings, and props never steer shape.
- Bounded world reads may establish endpoint coordinates before reduction.
- Build one reduced snapshot per active request/source cluster.
- Build one maximum useful domain, not progressive captures.
- Every included source or goal branch gets a complete viable corridor.
- Prefer low-turn straight corridors; turns consume maneuver area.
- Preserve inside-first and bounded outside-area fallback behavior.
- Missing facts fail closed; capture context never grants work authority.
- Memory scales with covered tiles or endpoint count, not enclosing-box area.
- Reduced success is provisional; reduced failure is never canonical `NoPath`.
- Reducer inputs and exact output are deterministic, versioned, and replayable.
- Persist no reduced-domain state into saves.

## Non-goals

- Reproducing the route or score selected by a full-area search.
- Claiming full-area modeled-rule preservation after reduction.
- Terrain- or obstacle-guided corridor selection.
- Replacing V1, V2, scoring, materialization, or live validation.
- Adding a player-facing mask editor or reducer setting initially.
- Making all normal full snapshots sparse merely for uniformity.

## Baseline facts to re-check before implementation

- `GetExperimentalCaptureBounds` builds rectangular tower and ground bounds.
- `BuildExperimentalAccessSnapshotCore` estimates and captures the complete
  rectangle and currently terminates as `SnapshotTooLarge`.
- Most retained facts use tile-keyed dictionaries and sets.
- `AccessGoalDistanceBuildSession` allocates `float[width * height]`; the
  durability spatial grid is also rectangular at 16-tile resolution.
- `AccessSearchSnapshot` exposes bounds that some V1/V2 code treats as semantic
  eligibility rather than indexing metadata.
- Ordinary ground goals are derived during capture by flooding from tower
  access; planned requests may provide explicit ground goals.
- `AccessPathRequest.Start` may contain an origin cluster. V2 derives multiple
  frontage launches and ordered start tiers from it.
- Primary and outside-area searches currently use separate attempts.
- Replay schema 1 has no reduced-domain coverage.

The worktree may change these facts before this plan is executed. Locate code
by responsibility rather than assuming line numbers remain stable.

## Deep module and seams

### Reduced access domain planner

Create one pure in-process deep module, tentatively
`ReducedAccessDomainPlanner`, with one external interface:

```text
ReducedAccessDomainPlan Plan(ReducedAccessDomainRequest request)
```

The value-owned request contains the normalized source footprint/frontage
candidates, tagged explicit and tower-proxy goals, immutable area geometry and
physical bounds, geometry policy, conservative budget, and reducer version.

A successful result owns selected endpoints, diagnostic spine geometry,
primary search coverage, outside-fallback coverage where authorized, shared
capture coverage, conservative accounting, and a stable fingerprint. Failure
returns a structured reason such as `NoEndpointPair`,
`MinimumCorridorExceedsBudget`, or `InvalidGeometry`, with no usable partial
domain.

Hide discrete path construction, turn bulbs, marginal branch selection,
balanced widening, halo overlap accounting, mask representation, and tie
breaking inside this module. Do not expose one shallow helper per algorithmic
stage. Production callers and fixtures test through `Plan`.

### Immutable tile coverage

Use one owned immutable type, tentatively `AccessTileCoverage`, instead of an
interface with only one adapter. Its small interface supports deterministic run
or tile enumeration, `Contains(tile)`, count, enclosing bounds, stable encoding,
and fingerprinting. Choose scanline runs, chunked bitsets, or another compact
implementation through benchmarks; do not expose `HashSet<Tile2i>`.

Coverage must classify at least:

- primary search-state eligibility;
- outside-fallback search-state eligibility;
- generated-origin authority for each phase; and
- capture-only context for ground, clearance, durability, rays, and buffers.

This preserves outside ground navigation without granting outside terrain-work
authority accidentally.

### Capture seam

Replace raw rectangular capture parameters with one value-owned capture-domain
contract. Full and reduced domains pass through the same primitive capture
pipeline. The domain owns eligibility and deterministic enumeration; capture
owns live reads and value copying. Derived graphs, masks, heuristics, and caches
remain workspace-owned under ADR 0005.

### Search seam

Keep rectangular bounds as metadata and index protection, not evidence that a
tile is captured or authorized. Audit every bounds use and classify it as:

1. physical-map boundary;
2. captured-fact availability;
3. search-state eligibility;
4. generated-origin authority;
5. primary/outside policy; or
6. rectangular indexing only.

Do not mechanically replace every check with one membership call. Validate the
complete V1/V2 transition footprint and delta, not only its anchor.

## Budget contract

Introduce a versioned geometry-only `AccessReductionBudget`, derived from the
configured ceiling. Account conservatively for fixed overhead, search coverage,
capture halo, terrain centers/aligned origins, mask storage, worst-case retained
facts, and headroom for collections not predictable from geometry.

The planner consumes this value; it never asks the live world how dense a
candidate region is. Actual capture retains pre-allocation and post-growth hard
checks. Estimator drift fails safely and diagnostically; do not respond by
looping through progressively smaller captures.

## Geometry contract

1. Normalize and stably order source frontage candidates and goals.
2. Build discrete candidate spines in the policy-authorized domain.
3. Cost each by incremental corridor, turn-bulb, and capture-halo coverage.
4. Select the cheapest viable source-to-goal spine. Stable ties prefer fewer
   turns, shorter travel, source coordinate, goal kind, and goal coordinate.
5. Add the cheapest remaining source/goal branch that fits at full minimum
   width, then repeat.
6. Spend remaining budget through balanced outward widening.
7. Build primary/fallback authority and their shared capture coverage.
8. Recalculate final cost from the immutable result and reject budget overflow.

Marginal covered cost is primary. Turn count is not lexicographically dominant:
an arbitrarily long one-turn detour must not beat a compact two-turn corridor.
Minimum geometry comes from immutable policy, not duplicated constants.

## Implementation tickets

Each ticket leaves the normal full-area path usable and preserves unrelated
worktree changes.

### Ticket 0: Baseline and bounds inventory

Record current Debug/Release fixture results and exact-DLL corpus reports.
Inventory every capture/search bounds use with the six classifications above.
Record one oversized request's dimensions, estimate, timings, and presentation.
Add a measurement-only thin-diagonal scenario comparing mask and box counts.

Acceptance: every production bounds gate is classified; canonical baselines are
recorded; production behavior is unchanged.

### Ticket 1: Pure reducer and fixtures

Add reducer request/policy/budget/result/coverage contracts under
`src/Access/Reduction/` or its current equivalent. Implement `Plan` with no
Mafi entity, manager, designation, terrain provider, callback, or mutable
setting dependency. Add purity guards and fixtures through the external seam.

Required fixtures:

- straight, diagonal, concave, and narrow-neck domains;
- shared goal branches and alternative source frontages;
- explicit turn-bulb cost and full minimum width;
- balanced widening with deterministic partial final growth;
- outside fallback without generation-authority leakage;
- minimum corridor over budget and no endpoint pair;
- input permutation producing byte-identical coverage/fingerprint;
- overflow-safe large coordinates; and
- thin diagonal storage proportional to coverage.

Acceptance: results are deterministic, connected, policy-valid, and within
budget; failures publish no partial plan; the module is live-world-free.

### Ticket 2: Coverage-aware snapshot contracts

Add full/reduced capture-domain contracts, coverage provenance, and explicit
captured/search/generated queries to snapshots. Preserve bounds as metadata.
Make the full-domain adapter answer exactly as current behavior does. Extend
memory diagnostics and estimator versions for coverage and bounds structures.

Acceptance: no production caller selects reduction; full-domain fixtures and
canonical replays remain exact; coverage is immutable and worker-safe.

### Ticket 3: Remove bounds-sized reduced workspace allocations

Replace the rectangular any-goal field for reduced snapshots with a mask-aware
lower-bound provider. Benchmark on-demand octile distance for small goal sets
against sparse or compact mask-indexed storage for larger sets. Preserve A*
admissibility and weaken to zero on invariant failure. Measure the durability
grid and convert it only if material. Audit V2 graphs, cleanup maps, useful-
height structures, and caches for hidden box scaling.

Acceptance: no material reduced-workspace allocation scales with a thin
corridor's box; A* matches Dijkstra; full-domain outcomes remain exact; memory
fits estimator headroom.

### Ticket 4: Masked primitive capture

Make terrain, centers, columns, ocean, pathability, buildings, props, cleanup,
durability, designations, readiness, and fixed profiles consume the capture
domain. Enumerate deterministic resumable runs/chunks. Preserve vanilla query
span limits without scanning uncovered rectangles or duplicating seam facts.
Capture complete required footprints and classify unavailable reads as
`ReducedSnapshotBoundary`.

Preserve revisions, dirtiness, hard invalidation, cancellation, slicing, and
one-snapshot backpressure. Never mutate caller-owned work collections while
filtering them to coverage.

Acceptance: no live read happens merely because a tile lies in the enclosing
box; collections are coherent and coverage-bounded; cancellation releases
partial data; atomic game-thread work stays below 30 ms or is split; full
capture remains exact.

### Ticket 5: Coverage-aware V1/V2 execution

Replace semantic bounds assumptions using the inventory. Validate complete V1
origins, V2 bands/deltas/turns, clearance masks, handoffs, fixed navigation,
cleanup footprints, and ray spans. Distinguish physical edge, missing snapshot,
and reduced boundary. Permit captured outside ground context while preserving
generation policy. Search primary coverage before enabling outside fallback
against the same facts.

Acceptance: holes and boundaries cannot be crossed through anchor-only checks;
missing data never becomes safe terrain; inside routes precede outside fallback;
worker and cooperative adapters produce the same reduced plan; cancellation
publishes no candidate.

### Ticket 6: Endpoints and production trigger

Change full preflight overflow from immediate rejection to `requires reduction`.
Preserve origin clusters and known fixed goals. Derive value-owned source
frontage geometry without full capture. Use tower access/docking position as
the proxy when ground goals are unavailable, then run the normal ground flood
inside captured coverage to derive actual goals. Plan/capture only the active
cluster and share one capture across primary/fallback phases. Never reduce a
request whose normal preflight fits.

Acceptance: normal requests use the unchanged branch; oversized farming and
interactive requests reach reducer diagnostics; a nearby trivial route inside
a huge polygon succeeds; distant clusters do not form one union; endpoint
preparation does not enumerate the oversized area.

### Ticket 7: Result, retry, and presentation semantics

Carry structured `ReducedAreaNoPath` through dry run, ramp result, blocked
analysis, manager result, farming status, diagnostics, and progress. Never map
it to authoritative `NoPath`/`NoCandidate`. Reuse bounded event-assisted retry
when relevant inputs change. Reduced success keeps current validation and
immediate retry-without-backoff when stale. Update transient notification text
without saved state and clear it on success or obligation removal.

Acceptance: failures remain inconclusive without tick spinning; invalidation
and ownership rules remain unchanged; save/load/removal fixtures serialize no
mask, retry record, or ATD notification.

### Ticket 8: Replay and diagnostics

Bump replay schema to encode reducer version, geometric inputs, policy, budget,
selected endpoints, spines, coverage, fingerprint, and terminal reason. Read
schema 1 as full-domain input; never rewrite its canonical payload. Replay
recorded coverage instead of rerunning the current reducer. Add deterministic
coverage codec round trips and corruption checks.

Report full estimate, reduced budget, mask counts, box-to-mask ratio, actual
estimate, capture/search timings, terminal reason, and fallback use. Avoid
per-tile normal logs. Record at least one real reduced success and failure.

Acceptance: schema-1 corpus remains exact; reduced cases reproduce their exact
input, classification, route, cost bits, and plan through archived DLLs;
corrupt coverage fails closed.

### Ticket 9: Qualification and rollout

Keep production reduction behind an internal qualification gate until fixtures,
replay, and real evidence pass. Qualify farming first, then interactive/planned
and remaining callers through the same manager seam. Profile capture, pure
preparation, search, heap, working set, and cancellation. Exercise world reset,
save, removal, execution-mode transitions, preemption, fallback, and repeated
environmental change. Add changelog/translations only when ship-ready.

Acceptance:

- below-ceiling corpus is unchanged;
- representative huge-area trivial farming and interactive routes succeed;
- reduced capture stays below ceiling with no overlapping retained snapshot;
- memory does not scale with a thin corridor's box;
- worker and cooperative results match;
- game-thread atomic work respects the established ceiling; and
- no unresolved corruption, deadlock, freeze, authority, or removability issue
  remains.

## Verification matrix

Pure geometry verifies connectivity, minimum width, turn bulbs, branch order,
balanced widening, budget saturation, policy authority, and determinism.

Capture verifies coverage-bounded retained facts, complete evaluator context,
missing-fact rejection, coherent dirty snapshots, cancellation cleanup, and
time slicing.

Search verifies complete transition containment, A* admissibility, inside-first
fallback, provisional success, and inconclusive failure.

Lifecycle verifies backpressure, preemption, cancellation acknowledgment, hard
invalidation, environmental dirtiness, mode changes, owner removal, world
replacement, save/load, and mod removal.

Performance covers straight, diagonal, winding, and branched masks; boxes at
least two orders of magnitude larger than masks; minimum through near-ceiling
budgets; small and large goal sets; and separately reported capture,
preparation, and search time.

## Expected file areas

- `src/Access/Reduction/`: reducer, coverage, budget, and fixtures.
- `src/Access/AccessCaptureContracts.cs`: capture domain and estimator.
- `src/ATD.ExperimentalAccessPathfinding.cs`: preflight, endpoints, capture,
  and request construction.
- `src/Access/AccessSearchModels.cs`: snapshot coverage and derived indexes.
- `src/Access/AccessPathSearch.cs` and `src/Access/V2/`: coverage gates.
- `src/ATD.RampGeneration.cs`, `src/ATD.FarmingAccess.cs`, and
  `src/ATD.AccesswayManagerRuntime.cs`: integration and lifecycle.
- `src/Access/AccessSearchReplay.cs`: schema and coverage codec.
- Capture, V1/V2, retry, manager, worker, replay fixtures, and
  `tools/AccessV2FixtureRunner/`: verification entry points.
- Notifications, localization, and translations only for qualified presentation.

## Build and evidence gates

At each ticket:

1. Run `git diff --check`.
2. Run focused fixtures through the established runner.
3. Run `dotnet build AutoTerrainDesignations.sln -c Debug`.
4. For replay/performance tickets, build Release and run the exact-DLL corpus
   in isolated processes.
5. Record real in-game evidence only after synthetic and replay gates pass.

Do not accept a ticket merely because it builds. Do not rebaseline canonical
full-domain outcomes to make reduction pass; any full-domain semantic change
requires independent review.

## Completion definition

Complete only when all oversized production access callers share the same
reducer/capture/search seam; normal full behavior remains exact; reduced success
is live-validated and failure inconclusive; memory scales with coverage; old and
new replay cases pass through exact DLLs; real farming/interactive cases satisfy
memory, responsiveness, lifecycle, and removability gates; and shipped docs do
not promise route equivalence or authoritative reduced failure.

## Measured implementation choices

- scanline runs versus chunked bitsets;
- on-demand versus sparse/indexed goal-distance threshold;
- whether the small durability grid merits conversion;
- conservative estimator coefficients/headroom; and
- qualification-gate mechanics and diagnostic sampling limits.

These do not reopen the architecture. If evidence requires terrain-guided
selection, progressive captures, authoritative reduced failure, tower-wide
scope, or bypassing live validation, update ADR 0008 before continuing.
