# Access Search Laboratory

Status: approved design; implementation is queued after the primitive capture
pipeline and before the dormant farming worker. Shared understanding was
confirmed on 2026-08-24.

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

- Finish Ticket 2's large-area capture stress qualification for the worker-safe
  snapshot contract.
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
