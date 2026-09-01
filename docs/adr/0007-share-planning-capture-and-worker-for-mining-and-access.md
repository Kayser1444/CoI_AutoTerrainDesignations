---
status: accepted
---

# Share planning capture and worker for mining and access

Mining and access planning will use a common captured-world-facts format,
one modest capture pipeline with parameterized geographic coverage, and one
shared worker. Both initially retain full terrain columns. Requests, policies,
derived planning structures, and outcomes remain separate; mining replay ends
at excavation geometry before access planning. This trades some migration work
for one capture implementation to maintain and a foundation for possible later
combined execution, without making that optimization part of this milestone.

Mining becomes another case type in the existing exact-DLL laboratory. The
initial extraction preserves current geometry, including reported ore-spike
behavior, so later filters can be evaluated against independent recorded inputs
and unchanged baselines. No new viewer is required.

Shared infrastructure does not require identical lifecycle policies. Mining
is interactive, captures on activation, queues across towers, and deliberately
omits access's environmental revalidation/rescan policy. It retains existing
safety rules against captured facts and accepts rare subsequent environmental
changes. Native batch submission ends cancellation authority over that batch;
the command finishes without rollback and its actual placements are observed.
These mining choices do not weaken the access contracts in ADRs 0003, 0005,
and 0006.

The agreed scope, rationale, native batch limitations, and remaining engineering
checks are in the [mining design note](../dev/in-progress/mining-scan-worker-laboratory.md).
The maintainer confirmed the overall shared understanding and authorized
implementation on 2026-08-31. The initial common column collector retains an
access serialization adapter to preserve existing replay cases; a single
serialized world envelope remains a later compatibility migration.
