---
status: accepted
---

# Coordinate accessway work through one cooperative manager

All current access-search callers will use one transient, single-active,
cooperative `ATDAccesswayManager`. Direct interactive work has strict priority
over derived farming and Construction Assist work; equal-priority requests from
different owners queue rather than cancel one another. Search remains on the
game-owned threads, uses the new access planner without importing legacy ramp
generation or candidate comparison, and advances within separate automated,
interactive, and paused frame budgets. Existing callers may retain the legacy
path temporarily until they migrate; every caller uses only the new planner
after migration.

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
manager cannot proceed; it never falls back to the old synchronous drain.
Migration starts with farming because that is the observed severe freeze. While
interactive callers remain outside the manager, their active-operation gate
suspends managed farming work so searches cannot compete. Interactive callers
then migrate to the same manager before the legacy generator is removed.

A releaseable mitigation may precede the manager. It stops a farming fixpoint
at its first failed search, negatively caches that failed obligation for a
10-to-60-second event-assisted grace period, and aggregates expected failure
diagnostics without warning stack traces. This bounds recurrence but does not
make the remaining individual synchronous search cooperative, so one isolated
hitch can still occur until farming migrates to the manager.
