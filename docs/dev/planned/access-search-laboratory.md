# Access Search Laboratory

Status: approved design; Tickets 2A and foundational 2B are implemented and
qualified. Real trivial and representative expensive searches reproduce their
canonical outcomes exactly through their manifest-pinned Release DLLs. The
complete two-case semantic regression and sequential five-run expensive
benchmark pass. Collaborative route review and autonomous conformance tuning
remain optional follow-ons. Shared understanding was confirmed on 2026-08-24.

Related design: [Accessway Search Worker](../in-progress/accessway-search-worker.md),
[worker implementation tickets](accessway-search-worker-tickets.md), and
[ADR 0006](../../adr/0006-replay-access-search-from-owned-snapshots.md).

## Objective

Create a development-only Access Search Laboratory that records real pure
access-search inputs and accepted outcomes in game, replays them through the
exact built `AutoTerrainDesignations.dll` outside the game, detects semantic
regressions automatically, benchmarks representative expensive searches, and
supports bounded autonomous performance-tuning campaigns.

The laboratory serves three goals:

1. automatic access-search regression testing;
2. faster maintainer-agent testing and route review; and
3. autonomous iterative development that normally reduces search duration
   without changing the selected route or materialized plan.

## Scope boundary

The laboratory tests the pure execution interval:

```text
recorded request + captured access snapshot + captured policy
    -> pure preparation
    -> V1/V2 search and scoring
    -> access-plan materialization
    -> canonical access-search outcome
```

It does not claim to test snapshot capture correctness, worker scheduling,
cancellation, timeout, interruption, staleness, save/reset behavior,
authoritative live-plan validation, designation commit, Unity physics, or
vehicle navigation. Existing in-game fixtures and playtests retain those
responsibilities.

## Conformance and route-quality tuning

The default autonomous activity is **access search conformance tuning**. It may
change implementation and performance but must preserve every canonical
outcome exactly.

**Access route-quality tuning** is a separate, explicitly initiated activity.
It may use the existing route-cost metric under a frozen, versioned baseline
policy, but any changed route is only a proposal until the maintainer approves
it after exact-scenario in-game validation. An autonomous process never
promotes or re-baselines its own changed outcome.

## Exact binary under test

The out-of-game runner loads the exact built mod DLL selected for the run. It
does not compile a second copy of the pathfinder source. Every run identifies
the DLL by hash, build timestamp, configuration, and source commit when known.

The production DLL contains a dormant internal developer-tooling seam:

```text
load recorded case -> opaque replay input
execute pure search -> canonical outcome + observational diagnostics
```

Snapshot reconstruction and canonical execution remain inside the mod DLL.
The runner owns corpus selection, immutable comparison rules, benchmarking,
visualization, and reports. The seam is not a supported third-party mod
interface. The executable and corpus do not ship in the player package.

Dormant replay support performs no file I/O, allocations, polling, or other
runtime work unless explicitly armed by a local developer command. A special
instrumentation-only DLL is rejected because it would weaken exact-production-
binary testing.

## Replay case

An **access replay case** separates stable input from replaceable expectation.
It contains:

- one immutable access request, captured access snapshot, endpoint set, and
  complete access search policy snapshot;
- a separately stored player-approved canonical outcome;
- schema and semantic-policy versions;
- input, outcome, DLL, and relevant game-assembly hashes;
- case name, scenario family, and suite role;
- capture and validation provenance; and
- observational in-game phase timings and diagnostics.

Changing a pathfinder outcome does not require re-recording the input. The
laboratory identifies only the cases whose outcomes changed. Those cases need
review, exact-scenario in-game validation, and explicit re-baselining. A new
capture is required only when the recording lacks newly required facts, cannot
be migrated without invention or loss, or is no longer representative.

### Case format

The large snapshot and exact numeric values use a versioned compressed binary
payload. A small readable manifest carries identity, provenance, hashes,
scenario metadata, policy fingerprint, and outcome summary. Reports and route
diffs use readable Markdown, JSON, and/or SVG.

Compatibility fails closed. A reader either performs an explicit migration
using facts already present in the old case or rejects the case for re-capture.
It never invents search-affecting defaults. Corrupt or unreasonable collection
sizes also fail before large allocation.

Game-assembly identity changes are always detected. They do not automatically
invalidate the corpus: the maintainer may authorize a compatibility run, and a
successful determinism and exact-conformance pass records a separate reusable
compatibility attestation without rewriting original provenance.

## Canonical outcome

Exact semantic comparison includes:

- deterministic terminal classification and structured reason code;
- selected provider and ordered route;
- route cost, compared by exact floating-point bit pattern; and
- ordered materialized designation and cleanup plan.

Human-readable failure text is not canonical. Visited-node counts, rejection
counters, frontier and phase counts, timings, allocations, GC observations,
and other execution diagnostics are recorded and diffed but do not fail
semantic conformance. Worker-versus-cooperative parity may retain stricter
diagnostic equality because those adapters are expected to run the same
algorithm.

The same DLL and case must reproduce the exact canonical outcome across fresh
processes. Nondeterminism blocks case promotion and autonomous tuning. The
algorithm must make search-affecting enumeration deterministic rather than
having the codec preserve accidental dictionary order.

## Recording workflow

The first recorder uses explicit **arm next search** semantics. A developer
console command supplies an optional case name and scenario family, and arms
exactly one eligible access search.

The execution context that exclusively owns the snapshot serializes the armed
case outside measured search time and before releasing ownership. It does not
share the snapshot concurrently or allocate an enormous second copy. The one
recorded request may therefore finish later; unarmed production searches pay
no recording cost. The recorder completes the case atomically only after a
deterministic terminal outcome is available.

Recorded cases first enter an untrusted, content-addressed, deduplicated inbox.
Promotion requires a stable name, scenario family, and suite role:

- semantic-only;
- performance-only; or
- semantic and performance.

A successful outcome is eligible for the canonical corpus only when the game
accepted it through authoritative live validation. A deterministic negative is
eligible only when its clean snapshot made that failure authoritative. Dirty,
stale, cancelled, timed-out, worker-faulted, or live-rejected recordings may be
retained as diagnostic material but do not claim validated correctness.

The promoted corpus is read-only to ordinary replay and tuning commands. Only
dedicated capture, promote, migrate, compatibility-attest, and re-baseline
commands may mutate it.

### Ticket 2A operator workflow

Build and install the Release DLL, then arm one capture in the in-game console:

```text
atd_access_replay_arm <case-name> <scenario-family>
```

Arming archives the exact currently installed DLL under the private Laboratory
`binaries/<sha256>` directory before accepting a search. The manifest points to
that immutable cached binary, so later development builds cannot make a valid
case unreplayable.

When the matching access or mining snapshot capture begins, the current game
is also queued for a save using the sanitized case name. The save is requested
only once per arm, and access arms do not trigger on mining captures (or vice
versa), so the saved world remains aligned with the recorded snapshot.

The arm remains dormant until a routed candidate is accepted by authoritative
post-placement validation. A dirty, rejected, cancelled, or timed-out search
does not produce a validated case. The completed directory is written
atomically beneath:

```text
%APPDATA%\Captain of Industry\AccessSearchLaboratory\AutoTerrainDesignations\inbox
```

Encoding and compression run on a background capture operation after ownership
of the immutable request has transferred from the accepted search. A lightweight
sizing pass gives the encoder an exact work-unit denominator; the calling
coroutine remains alive and yields while the progress surface reports the
current capture stage and percentage. The sizing pass reports coarse 1–4%
milestones before the exact 5–75% graph-write range begins, so large captures
no longer remain visually at 0% while the denominator is being computed.
During this post-commit interval, the
button is relabeled **Abort replay capture** and cancels only the recorder. It
does not invalidate or roll back the already accepted access route. Cancellation
is checked during sizing, encoding, payload hashing, and chunked compression;
temporary output is removed and no completed case is published. No live game
object is accessed by the background operation.

The first medium-area capture visibly paused around 10, 16, 23, 31, 33, 41,
47, 56, 61, and 68 percent. Treat those plateaus as capture-codec evidence,
not search benchmark samples: they likely identify uneven graph sections,
sorting, or buffer growth within otherwise exact work-unit progress. The pure
benchmark deliberately excludes this encoding and file-loading work.

An expensive cluster-2 capture on 2026-08-29 exposed an acceptance boundary:
`AcceptedPendingPropRemoval` is not authoritative live validation. The recorded
plan contained its cleanup origins, but all six dense-debris operations later
ended as `PlayerOverride`, leaving the visible route incomplete. The manager had
treated a vanilla-removed temporary preview as a player replacement, and Quick
cleanup had not first registered overlapping pathfinder work as the original
designation to restore. Both lifecycle faults now have focused fixtures. The
recorder also keeps the encoded case staged at 99 percent until every cleanup
request succeeds, its original designation is restored, and the live V2 route
passes validation. Rejected or aborted staging is deleted. The original case
remains diagnostic inbox evidence and is not eligible for semantic or
performance promotion; a fresh in-game cluster-2 run must qualify the fix.

The corrected live run published
`expensive-v2-01-14f13de0700549b8.atd-access-case` only after all six cleanup
requests completed as `Removed` with their original designations restored.
Replay then identified a separate canonical-codec defect: the sliced in-game
route and unsliced standalone route contained identical step values but
different reference aliasing between equal internal transition objects. Object
identity is not pathfinder semantics. Canonical route-step encoding now detaches
each step before graph serialization, and schema-1 readers normalize stored
canonical records the same way. A focused fixture proves that shared and copied
reference topologies normalize to identical, idempotent bytes. Two fresh-process
candidate replays of the corrected case produced normalized SHA-256
`c314032209478f01bdecab31401579fab29e1ef3bb80dba15dd3354fbfd96d07`
with no semantic difference. Final normalized capture
`expensive-v2-01-3a322d4481323a82.atd-access-case` binds that canonical form to
exact archived Release DLL SHA-256
`d61dbd376d4ce9675914aa412018b9cc32d4e861ca3d19ff45b934c151f48af8`.
Two fresh exact-DLL processes reproduced it with `diff=none`. It is promoted as
`semantic-performance`, and the serial two-case corpus regression plus all
synthetic fixtures passed in report `20260829-120441-regression`.
The quiet-machine five-repetition benchmark passed in report
`20260829-122404-benchmark`: exact semantics on every run, median pure search
42.542 seconds, median total 42.583 seconds, 42.491--42.920 second total range,
970 MB peak working set, and about 697 MB retained managed-heap delta. These
desktop .NET Framework timings are directional evidence; the recorded in-game
Mono search took 73.438 seconds.

The first Laboratory-only conformance optimization pass on 2026-08-29 used a
fresh busy-machine baseline (`20260829-165957-benchmark`) of 42.107 seconds pure
search and 967 MB peak working set. Reusing cached ray envelopes from parent
histories was rejected after it regressed the same exact case to 45.351 seconds
without reducing memory. An exact accumulated-ray-bounds rejection was retained:
report `20260829-171217-benchmark` reproduced the canonical outcome at 40.218
seconds pure search, 885 MB peak working set, and about 697 MB managed-heap
growth. This single-repetition comparison is directional rather than a new
quiet-machine baseline. All three promoted private cases plus synthetic
fixtures subsequently passed report `20260829-171407-regression`.

A planned-tower capture on 2026-08-30 added a targeted case audit for two
materialization anomalies. The canonical plan contained one isolated
exact-terrain source-launch origin at `(976,1036)` and eleven dense-debris
props. Current materialization reuses that exact source terrain instead of
placing a no-op leveling designation or charging generated-V fixed overhead.
At the exact prop probes, six of the eleven props are covered by leveling cuts
whose targets lie below the props' placement heights. Restrictive and Never
cleanup therefore leave those six to the planned excavation; Always may still
prefer Quick remove. The other five props have no covering route designation
and retain separate cleanup. The `audit-case` runner command checks these facts
without rerunning the expensive search, while the full candidate replay proves
that the route remains structurally identical apart from the intended cost and
materialized-plan change.

Replay the case with the existing development runner and an explicit Release
DLL plus the matching installed game `Managed` directory:

```text
AccessV2FixtureRunner replay <AutoTerrainDesignations.dll> <Managed> <case-directory>
```

`codec-benchmark` reports decompression, deserialization, serialization, and
compression separately for capture-format investigations.

`qualify-replay.ps1` invokes that mode in two fresh processes and requires both
to reproduce the same approved canonical hash. Its `AssemblyPath` argument may
be omitted; the script then uses the archived DLL named by the case manifest.

The replay rejects mismatched schemas, semantic policy, payload hashes, ATD
binary identity, build configuration, or Mafi assembly fingerprints before it
executes the request. `atd_access_replay_cancel` clears an unused arm.

## Corpus ownership

The initial scope is local collaboration between the sole maintainer and an
agent. Real save-derived cases live in one stable user-local corpus outside all
Git worktrees and are gitignored by construction. The public MIT repository
contains the format, tooling, and deliberately authored synthetic cases, not
private real captures by default.

An individual real case may enter Git only after explicit review confirms that
publishing it and representing it under the repository license is appropriate.
Backup is an explicit local integrity-check and export operation; no automatic
cloud upload or synchronization is introduced.

Promoted cases are grouped into named scenario families. Aggregate performance
weights families rather than raw file count so near-duplicate recordings cannot
silently dominate. Trivial cases may remain semantic-only, while representative
expensive cases form the performance corpus.

The local operator entry point is
`tools/AccessV2FixtureRunner/access-lab.ps1`. Typical Release commands are:

```powershell
./tools/AccessV2FixtureRunner/access-lab.ps1 -Mode promote `
    -CaseDirectory <inbox-case> -Name <case-name> -Family <family> `
    -Role semantic-only
./tools/AccessV2FixtureRunner/access-lab.ps1 -Mode list
./tools/AccessV2FixtureRunner/access-lab.ps1 -Mode regress
./tools/AccessV2FixtureRunner/access-lab.ps1 -Mode benchmark `
    -BaselineAssemblyPath <baseline-dll> -Repetitions 5
```

Promotion copies a content-addressed case into the private corpus, attaches
required family and suite-role metadata, and makes its files read-only.
Regression runs synthetic fixtures first, then semantic cases in up to six
bounded fresh child processes. Benchmarking selects performance cases, remains sequential,
and defers while Captain of Industry is running unless an explicitly
directional busy-machine smoke run is requested. Both commands write readable
Markdown and machine-queryable JSON reports beneath the laboratory `reports`
directory; parsed key/value observations accompany the original child output.

## Regression execution

The laboratory is an explicit post-build gate rather than part of every
`dotnet build`:

1. invoke the existing pure synthetic fixture gate from the exact DLL;
2. qualify determinism where required;
3. run the complete semantic corpus with strict process exit codes; and
4. emit exact structured and visual differences.

Semantic cases may run with bounded child-process concurrency because their
timings are irrelevant. Each execution is externally watchdog-protected so a
hang, crash, runaway memory use, or leaked static state terminates only its
disposable process. The watchdog is test infrastructure and does not claim to
exercise ATD cancellation.

Public CI runs committed synthetic cases only. The local regression command
adds the private real corpus without making ordinary builds depend on it.

## Performance measurement

The primary metric is wall-clock time for the entire pure execution interval
from an already deserialized input through pure preparation, search, and plan
materialization. Process startup, DLL and replay-file loading, serialization,
and JIT warm-up are excluded. Preparation, search, and materialization are also
reported separately so work cannot be hidden by moving it between phases.

CPU time, peak working set, managed allocation, and GC observations are
supporting evidence. Production-equivalent lightweight diagnostics remain
enabled during authoritative timing; detailed profiling runs separately.

Captured requests retain their estimated snapshot memory and ceiling inside
the schema-1 request graph. The benchmark reports those values alongside the
replay process measurements, so existing cases can be compared without being
rewritten. An explicitly armed new capture also records optional
`captureMemoryMeasurement` evidence in its manifest: managed-heap, working-set,
and private-memory counters before and after snapshot construction, collection
counts, elapsed probe time, and the estimator version. This probe is
observational, does not control the live guard, and is absent from old cases.
Replay managed-heap growth remains a search-allocation proxy rather than a
retroactive measurement of the original in-game snapshot capture.

Authoritative performance runs:

- use `Release` DLLs;
- are sequential and single-search/single-threaded;
- require a low-contention machine and defer when the game or another obvious
  heavy workload is active;
- alternate the fixed baseline and candidate to reduce thermal and background
  drift; and
- use repeated distributions rather than one timing sample.

Material improvement, permitted per-case slowdown, watchdog multipliers,
repetition count, and significant memory growth are campaign-specific measured
limits. Improvement must exceed observed noise. A fixed hard memory ceiling
still prevents accidental runaway allocation, and significant memory growth
requires maintainer approval.

Captain of Industry uses Unity's embedded Mono runtime while the first runner
may use the ordinary .NET Framework runtime. At the expected 10-100 second
search durations, runner measurements are accepted as a directional signal for
large algorithmic improvements rather than millisecond tuning. Every promoted
performance candidate normally receives one exact-scenario in-game `Release`
execution. It must show the same improvement direction and consistent phase
behavior; a contradictory result blocks promotion. Exact Mono hosting is
reconsidered only if runner gains repeatedly fail to transfer.

### In-game timing provenance

Promoted performance cases record the in-game wall-clock breakdown for pure
preparation, search, materialization, and total pure execution. Queue, capture,
live-validation, commit, and recording time remain separate. These single
samples are observational, not canonical, but allow accumulated correlation
between runner and game performance.

## Benchmark funnel

An autonomous iteration uses a staged funnel:

1. build the candidate and run the complete semantic corpus;
2. benchmark the targeted expensive case or family;
3. reject clearly inferior candidates early;
4. benchmark promising candidates against the fixed baseline across the full
   performance corpus; and
5. retain a final comparison only after repeated measurements stabilize.

Bounded performance trade-offs are allowed when the aggregate gain is
substantial, no case crosses its regression guard, and every consistent
slowdown is reported. Target and guard families are reported separately.

## Ordered expansion traces

The development runner's `trace-candidate` mode replays one candidate case and
writes every V2 expansion in exact execution order to CSV. It is opt-in and is
not enabled by either production worker mode. Each row records elapsed time,
ground or generated kind, center and displayed height, enqueue age and cost,
ground-relaunch status, axis and entry direction, handoff qualification,
fixed-navigation portal identity, adapter state, history identity and size,
potential owner, and search-key hash. The canonical result is still compared
before the trace command succeeds. Ground rows additionally record whether a
goal or suffix succeeded and how many G and V child enqueue attempts survived
normal label dominance.

Trace rows also retain diagnostic-only G-relaunch provenance: the launch
ground center and component, the center where component-conditioned potential
ownership first weakened to global, the component of a returned handoff G
entry, and any already validated ordinary-G cost at that same concrete key.
This bookkeeping is allocated only when Laboratory expansion tracing is
enabled; production workers do not carry it.

The first trace of `expensive-v2-01` on 2026-08-29 reproduced the visually
observed late backward wave. Between 30 seconds and completion, 4,713 of 4,776
ground-colored expansions were history-qualified V-to-G handoff entries, all
newly enqueued by generated V labels rather than neighboring ground labels.
They covered 790 ground centers under 3,885 distinct histories. The concurrent
generated wave contained 6,552 ground-relaunched labels out of 6,569 and had
already returned to global potential ownership. Thus the overlay was not
showing an exact-label reopen or an ordinary ground flood: it was collapsing a
large family of distinct shallow generated states and their repeated handoff
probes onto previously painted centers. The behavior follows the current
modeled-rule-preserving search, but the volume is a credible dominance or
heuristic optimization target.

A consequence trace of the same exact replay then showed that 12,757 of the
12,768 history-qualified ground entries produced no accepted G child, no
accepted V child, and no successful suffix. Those entries performed 55,494
ground enqueue attempts, of which only 21 survived; no history-qualified ground
entry attempted a G-to-V launch. In the late backward wave, 4,713 of 4,715
entries were completely dead, while one accepted a ground child and one found
the winning suffix.

The first conservative optimization applies an optimistic ordinary-G
cost-dominance check before history, cleanup, and local-escape validation of a
handoff-derived ground successor. It rejects only when every concrete successor
key is already cheaper even with zero cleanup cost; entries with a possible
goal suffix are unchanged. A focused fixture proves that the dominated history
no longer invokes its ground validator while an earlier productive history
still traverses the same seam, and the existing fixture continues to prove that
a later history remains eligible when it alone can validate the goal suffix.
The exact expensive case improved from 40.218 seconds pure search and about
697 MB managed-heap growth to 33.636 seconds and about 604 MB in report
`20260829-175240-benchmark`. All three promoted private cases plus synthetic
fixtures passed report `20260829-175422-regression`.

A second exact gate moves the same proof to handoff-entry enumeration for
plain physical G. If the entry has no ground goal distance, is not at a
canonical G-to-V launch coordinate, does not involve projected or fixed
navigation, and every geometrically traversable ordinary-G neighbor already
has an equal or cheaper label at the zero-cleanup lower bound, the
history-qualified entry is not queued. Cluster 2 retained canonical SHA-256
`c314032209478f01bdecab31401579fab29e1ef3bb80dba15dd3354fbfd96d07`
while expansion count fell from 75,643 to 74,053; the 1,590 removed rows were
all G handoff entries and V expansions remained 44,578. A broader exact
tower-area precheck removed only one additional entry and was discarded as
insufficient benefit for added hot-path work. A three-run timing comparison
was noisy (34.5--38.1 seconds, 37.9-second median), so this slice claims the
measured frontier reduction but no independent wall-clock improvement. The
final retained DLL passed all three promoted private cases plus the synthetic
suite in report `20260829-181937-regression`.

The screenshot-correlated provenance trace then identified one dominant loop:
3,207 same-component handoff returns descended from G launch `(832,1670)` in
component 27, and 3,202 crossed the global-ownership merge fringe at
`(814,1672)` before returning. Across the case, 5,858 handoff entries returned
to their launch component and every one was consequence-free. A previously
validated ordinary-G label already reached 5,854 of their exact centers more
cheaply; the late loop cost roughly 4,300--5,000 while those labels cost about
170--180. Cost alone was not used to prune because the generated history can
theoretically enable a different future continuation.

Instead, entry dominance now enumerates the exact possible G-to-V label keys at
a zero-additional-cost lower bound and applies the same bounds, transition
resolution, useful-height-envelope, and accumulated-origin-history checks as
real G-to-V expansion. The entry is skipped only when all ordinary-G and
G-to-V consequences are already dominated or structurally impossible; any
possible unique successor retains the history-qualified entry. The Cluster 2
canonical hash remained unchanged while expansions fell from 74,053 to 62,906,
handoff-G expansions fell from 11,178 to 31, same-launch-component returns
fell from 5,858 to 3, and the `(832,1670)` family emitted no G returns. V
expansions remained 44,578. A three-run benchmark improved from the preceding
37.905-second median to 34.833 seconds with a 34.818--34.900-second range.
All three promoted private cases plus the synthetic suite passed report
`20260829-191517-regression` against the retained DLL.

A bounded follow-up on 2026-08-30 extended the same exact replacement proof
from one terrain level to two. All three private cases and synthetic fixtures
remained exact, and managed-heap delta fell by about 44 MB, but the controlled
three-run Cluster 2 median regressed from 33.780 seconds to 34.187 seconds while
CPU time increased; peak working set improved by only about 4.8 MB. The
candidate was rejected and the one-level gate retained. Any broader reuse of
the proof should first remove repeated launch-position and seam evaluation
work rather than merely increasing the height window.

The next experiment investigated the player-visible order of ordinary-G to
generated-V work. In the captured ghost-tower case, the ground phase spent
10.62 seconds in 10,464 eager G-to-V calls, evaluating 156,215 profiles and
about 1.31 million bridge steps before the useful mountain attack became
visible; the visualization callback itself peaked at only 0.16 ms. A candidate
deferred individual launch directions and tried them by relaxed continuation
potential while retaining every direction behind an admissible lower-bound
gate. A focused fixture changed its seam-evaluation order, and corpus replay
retained the exact trivial and Cluster 2 outcomes.

In-game review and an ordered trace showed that this did not change the real
ghost-tower frontier. The first diagnostic had recorded a geometrically valid
seam at visited label 2,202 even though normal label dominance rejected its
resulting V label. The first surviving V label was actually enqueued at visit
6,181 and expanded immediately at visit 6,182 after 5.33 seconds; all preceding
6,181 expansions were G. The difficulty is therefore predicting which ground
approach will yield the eventual winning concrete V launch, rather than merely
reordering seam evaluation at already explored ground. That is heuristic work.
The deferred scheduler and its focused fixture were reverted. The corrected
`firstEnqueueVisited` diagnostic and the trace findings were retained; no
performance or responsiveness improvement is claimed for the experiment.

## Route-change review

Every changed canonical outcome produces a standalone visual diff from the
recorded snapshot. It shows baseline and candidate routes; added, removed, and
unchanged terrain work; cleanup actions; provider and goal; exact costs; phase
performance; and why in-game validation is required.

A developer-only game command may import a candidate outcome, display it in the
diagnostic overlay, and run authoritative live-plan validation without
rerunning search or committing designations. Re-baselining requires **exact-
scenario validation**: a fresh captured-input fingerprint must match the replay
case and live validation must accept the candidate. Validation against a
changed current world is useful evidence but cannot replace the canonical
outcome.

Promotion is two-step and case-specific:

1. the runner creates a re-baseline proposal with old/new visual diff, exact
   semantic changes, DLL identity, policy, and performance evidence; and
2. an explicit promote command consumes the maintainer-approved exact-scenario
   validation attestation.

There is no general update-all-expected-results switch.

## Autonomous tuning campaigns

Each campaign operates in a dedicated Git worktree on a `codex/` branch created
from an explicitly selected baseline. A campaign manifest pins the baseline
commit and DLL, game assemblies or approved compatibility attestation, corpus
manifest, policy fingerprints, build configuration, benchmark protocol, machine
identity, target family, improvement target, resource limits, and stopping
budget.

The campaign may alter only explicitly allowed worker-safe pure preparation,
search, scoring, and materialization source. It may not modify cases, canonical
outcomes, comparison rules, recorder, codec, benchmark acceptance rules,
snapshot capture, lifecycle logic, live validation, or commit. Harness and
oracle hashes plus a Git-diff allowlist mechanically enforce this constraint.

One search remains single-threaded. Parallelizing the algorithm would change
the approved CPU policy, determinism, cancellation, and player impact and
requires a separate architectural decision.

The pathfinder never receives case names, hashes, provenance, or corpus
metadata. Candidate code may not branch on exact coordinates, fingerprints, or
recorded geometry. Every retained candidate explains the general algorithmic
reason for its improvement and passes guard families to discourage corpus
overfitting.

A campaign may automatically commit a candidate on its branch only when it
builds, passes the full semantic corpus, and has reproducible benchmark
evidence identifying the exact DLL and baseline. It never pushes, merges,
promotes a route, re-baselines a case, or alters the maintainer's active worktree.

Campaigns stop on their requested improvement, exhausted budget, repeated
materially different attempts without progress, semantic/determinism failure,
corpus incompatibility, or need for a forbidden change. Reports retain passing
candidate commits, the fixed baseline, final comparisons, and concise failed-
approach lessons. Bulky failed DLLs and routine traces are disposable.

## Implementation slices

### Slice A: Replay seam and single-case round trip

- Finish Ticket 2's pure-helper isolation and worker-safe snapshot contract.
- Add the dormant opaque replay facade and versioned case codec.
- Add `arm next search` recording with terminal provenance and phase timing.
- Add a development executable that loads the exact `Release` DLL.
- Prove one real case reproduces its exact canonical outcome outside the game.

This slice blocks Ticket 3.

### Slice B: Corpus regression and benchmark runner

- Add inbox promotion, content addressing, explicit schema migration, and
  compatibility attestations.
- Invoke the existing synthetic fixture gate, determinism qualification, exact
  canonical comparison, external watchdogs, and process isolation.
- Add semantic/performance suite roles, scenario families, staged timing,
  memory evidence, strict exit codes, and readable reports.
- Prove the complete foundational runner against at least one trivial and one
  representative expensive case.

This foundational slice also blocks substantial Ticket 3 parity work.

### Slice C: Collaborative route review

- Add visual route/plan diffs.
- Add read-only in-game candidate import, exact-scenario fingerprinting, live
  validation attestations, and case-specific promotion.

This slice may proceed alongside early Ticket 3 work and does not block the
worker thread itself.

### Slice D: Autonomous conformance tuning

- Add campaign manifests, worktree/branch isolation, immutable-harness guards,
  staged experiment orchestration, candidate commits, stopping rules, and
  concise experiment journals.

This begins only after regression and benchmark evidence are trusted. It does
not block worker implementation.

## First milestone acceptance

The first milestone is intentionally one vertical round trip:

```text
arm one in-game search
    -> record one versioned, validated case
    -> load the exact Release ATD DLL outside the game
    -> reproduce the canonical outcome exactly
    -> report phase timing plus DLL and case hashes
```

Corpus weighting, campaign automation, candidate import, and polished visual
diffs are not required until this seam is proven.

## Open decisions

None. The exact local corpus path, binary field layout, benchmark repetition
counts, watchdog multipliers, and campaign-specific time and memory thresholds
remain measured implementation choices.
