---
status: accepted
---

# Coordinate accessway work through one cooperative manager

All current access-search callers will use one transient, single-active,
cooperative `ATDAccesswayManager`. Direct interactive work has strict priority
over derived farming and Construction Assist work; equal-priority requests from
different owners queue rather than cancel one another. Search remains on the
game-owned threads, preserves legacy and V1/V2 route-selection semantics, and
advances within separate automated, interactive, and paused frame budgets.

This replaces distributed caller-owned scheduling because farming synchronously
drained a multi-cluster fixpoint and held one simulation step for up to 8.67
seconds in a tester trace. A cooldown alone would reduce recurrence without
preventing the first stall, while worker-thread execution would cross Unity/Mafi
boundaries that have not been proven thread-safe.

One derived request may commit at most one accessway before its owner
re-evaluates live reachability. Stable access obligations coalesce changing
attempt fingerprints, completed failures use a 10-to-60-second bounded
event-assisted retry policy, and heavy dry-run phases must yield or prove they
are bounded. The final small placement transaction remains atomic. Manager
state is runtime-only and re-derived across world loads so save removability is
unchanged.

Production fails closed with request-scoped status and diagnostics when the
manager cannot proceed; it never falls back to the old synchronous drain. The
first release must migrate both interactive and derived callers, retain existing
route choices, and clear the multi-cluster freeze in a live save.
