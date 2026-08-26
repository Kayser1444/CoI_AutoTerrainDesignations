---
status: accepted
---

# Replay access search from owned snapshots

ATD will use the worker-safe captured snapshot and canonical pure executor as a
second seam for an out-of-game Access Search Laboratory. The game records a
versioned request, sealed snapshot, policy, accepted canonical outcome, and
provenance; a development runner then loads the exact built
`AutoTerrainDesignations.dll` and replays pure preparation, search, and plan
materialization without loading a save or running the game.

This deliberately keeps the replay facade narrow and inside the production DLL
instead of compiling a second pathfinder or exposing its internal model. It
trades a dormant codec and tooling seam in the shipped DLL for exact-binary
semantic regression, realistic local performance cases, faster maintainer-agent
review, and bounded autonomous conformance tuning. The executable, private real
corpus, reports, and tuning controller remain development-only and are excluded
from the player package.

Recorded input and expected outcome are independent. A route-changing candidate
therefore identifies only the outcomes requiring review; it does not force
snapshot re-capture. Canonical replacement remains a maintainer action backed
by exact-scenario in-game validation. Autonomous campaigns may optimize search
duration under exact semantic conformance but cannot alter their own oracle,
promote routes, merge, or modify live capture and commit behavior.

The detailed case, corpus, benchmark, validation, and campaign contracts are
maintained in [Access Search Laboratory](../dev/planned/access-search-laboratory.md).
