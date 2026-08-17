---
status: accepted
---

# Execute pure access search on one worker

Access-search execution will move from cooperative game-thread slices to one
persistent background search worker. This partially supersedes ADR 0003 only
where it requires search to remain on game-owned threads. ADR 0003's single
manager, one-active-search policy, interactive priority, request coalescing,
retry behavior, and fail-closed production behavior remain authoritative.

The earlier game-thread restriction correctly recognized that Unity and Mafi
objects are not proven thread-safe. The worker does not relax that constraint.
The game thread captures a sealed, data-only snapshot of primitive live-world
facts and an immutable semantic policy. A request-local workspace then performs
pure indexing, graph construction, V1/V2 search, scoring, diagnostics, and
access-plan materialization on exactly one execution context. No live object,
callback delegate, mutable global setting, UI action, logging call, or terrain
mutation crosses into worker execution.

The manager remains the sole owner of queuing, priority, cancellation authority,
world generation, and commit authority. It submits at most one immutable job,
polls immutable progress and terminal output, validates every successful plan
against current live state, and commits designations transactionally on the
game thread. Environmental change makes captured work dirty rather than unsafe:
success remains provisional and failure cannot establish an authoritative
negative result. Hard invalidation immediately removes commit authority.

This boundary trades additional snapshot, lifecycle, cancellation, memory, and
purity machinery for removal of unbounded search steps from player-visible
frames. It also makes cooperative and worker execution two adapters around the
same snapshot, workspace, algorithm, materializer, and result contracts rather
than two implementations of access search.

Rollout is phased: worker execution is first an explicit opt-in, then the
default with cooperative opt-out, and finally enforced fail-closed. Promotion
requires semantic-parity fixtures, purity guards, bounded memory, responsive
cancellation, lifecycle stress, large-scenario validation, and public field
experience. The detailed contracts and promotion gates are maintained in
[Accessway search worker](../dev/in-progress/accessway-search-worker.md).
