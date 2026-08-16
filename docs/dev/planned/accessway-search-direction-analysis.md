# Accessway Search Direction Analysis

Status: research note; no implementation decision.

Researched: 2026-08-16

## Conclusion

There is no theoretical reason to assume that field-to-tower is always the
faster unidirectional search direction. Forward and reverse BFS, Dijkstra, or
A* can expand vastly different regions even when the final path has the same
cost. The better direction is the one with the smaller relevant search contour,
which depends jointly on graph topology, root and target sets, directional
transition costs, and—in A*—the quality and distribution of the heuristic in
that direction.

For ATD, however, reversing the production search is not an endpoint swap. The
current graph and accounting are intentionally field-rooted and
predecessor-sensitive. A correct reverse search would need explicit reverse
operators, forward-equivalent reversed edge costs, a separately proved
heuristic, and reverse forms of start/terminal validation. A quick production
direction selector is therefore premature. The useful next step is offline A/B
measurement on captured requests, including total setup cost, before building a
reverse engine or selector.

## What search theory says

### BFS and Dijkstra

BFS is uniform-cost search when all edge costs are equal; Dijkstra is the
nonnegative weighted form. On a reversible graph, reverse search means running
the same shortest-path problem on the transpose graph, assigning every
reversed edge the cost of its corresponding forward edge. Both directions find
the same optimum, but they need not settle the same number of vertices before
reaching it.

The elementary `b^d` tree model suggests choosing the smaller branching factor,
but this is only a coarse model. In a graph, duplicate merging, obstacles,
unequal costs, dead ends, and several roots or targets determine the size of the
actual cost ball. A low-degree endpoint can still lead into a huge region one
step later, while a high-degree endpoint can quickly funnel into one corridor.
Sturtevant and Chen give the analogous road-network example: a dense city at
one end and sparse countryside at the other can make reverse unidirectional
search preferable. They treat this as graph asymmetry and estimate it by
sampling bounded Dijkstra searches—not by endpoint degree alone
([ICAPS 2020, pp. 284–285](https://ojs.aaai.org/index.php/ICAPS/article/download/6672/6526/9901)).

Multiple roots do not change correctness: connect a zero-cost super-root to
all roots (or seed them all at distance zero). Therefore “many starts versus
many goals” matters through initialization cost and the union of the regions
reached from those seeds, not as a standalone rule. Many adjacent roots may
merge almost immediately; a few isolated roots may each open a large search
region.

### A*

A* orders nodes by `f(n) = g(n) + h(n)`, where `h` is a lower bound on remaining
cost. Its guarantees concern the heuristic for the chosen direction, not a
direction-independent geometric notion of closeness
([Hart, Nilsson, and Raphael 1968](https://doi.org/10.1109/TSSC.1968.300136);
[Dechter and Pearl 1985](https://doi.org/10.1145/3828.3830)). A reverse A* needs
its own admissible/consistent estimate of cost to the original start; forward
and backward heuristic conditions are distinct
([Shaham et al., IJCAI 2019, pp. 6221–6222](https://www.ijcai.org/Proceedings/2019/0867.pdf)).

Consequently, branching factor alone is especially weak for A*. Search work is
driven by the population of nodes whose lower bound competes with the optimal
solution cost, plus tie handling. The distribution of heuristic error across
the explored state space can change complexity substantially
([Huyn, Dechter, and Pearl 1980](https://doi.org/10.1016/0004-3702(80)90045-4)).
A direction with more raw successors can win if its heuristic sharply excludes
them; a direction with fewer successors can lose if its heuristic is weak over
a broad plateau.

Goal handling also matters. Optimal A* normally accepts a goal when it is
selected at the required priority, not merely when first generated. ATD goes
further: it reconstructs and fully validates a reached goal and, on rejection,
continues toward another. Reversal moves that expensive and selective boundary
to the other endpoint, so direction can change both the number and the kind of
terminal candidates tested.

## Can a cheap pre-analysis select the faster direction?

It can provide a useful estimate, but not a reliable general answer.

Any fixed-radius local probe can be defeated by two graphs that are identical
inside the probed neighborhoods but differ just beyond them: attach the large
branching region beyond the forward probe in one graph and beyond the reverse
probe in the other. The probe returns the same measurements, while the faster
direction flips. This is a direct indistinguishability argument, not a claim
that sampling is useless.

The primary literature does use inexpensive sampling of heuristic values,
near-goal states, and bounded Dijkstra expansions to estimate asymmetry, but
explicitly describes the resulting measures as estimates with limitations
([Sturtevant and Chen 2020, sections 4.1–4.4](https://ojs.aaai.org/index.php/ICAPS/article/download/6672/6526/9901)).
For one ATD request, a small search pilot in both directions would be stronger
than static endpoint inspection, yet it has three costs:

* both correct directional engines and heuristics must already exist;
* the losing pilot work is discarded unless the implementation can reuse it;
* early frontier behavior can still fail to predict a later obstacle,
  heuristic plateau, or rejection-heavy terminal region.

Thus a pilot is best understood as a budgeted empirical policy, not an
optimality theorem. Its success must be measured over the actual request
distribution, and selector overhead must be included in elapsed time.

## ATD-specific assessment

The current production direction is from one or more fixed field-work profiles
toward tower-connected ground or reusable fixed providers. It is not currently
symmetric:

* V1 explicitly accepts `FixedProfiles` as starts and rejects ground starts.
* V1 expansion and final replay share predecessor-sensitive generated-profile
  checks; reached goals can be rejected after full-path materialization and
  search then continues.
* V2 has synthetic-companion seeds that are start-only, while reusable provider
  frontages have different eligibility rules.
* V1/V2 history charges newly introduced origins and deduplicates cleanup and
  disturbance work along the candidate path. V2 turns, strafes, handoffs, and
  some ground-successor pruning use entry direction and recent history.

These are valid forward-state semantics. A reverse implementation would have
to define a reverse state rich enough to reproduce the same eventual forward
plan and cost. Calling the forward successor generator from the tower side
would not establish that equivalence.

ATD already takes a scientifically sound middle course: it keeps the exact
search field-rooted while doing goal-side reverse preprocessing for guidance.
V1 incrementally collects tower-ground/fixed goals into a height-aware goal
index. V2's sparse route potential is built by reverse shortest path from
goal-connected ground and fixed-provider contacts, while exact search retains
forward construction semantics. This captures information from the broad
tower-side goal set without turning the exact construction search around.

## Recommended experiment

Do not add a per-request direction chooser yet. First build a benchmark corpus
from immutable accessway request snapshots and retain the current direction as
the production default. Record at least:

* width, start count, goal count, goal components/frontages, and bounds;
* session-build time, including V1 goal index or V2 potential construction;
* queue pops, accepted expansions, stale/dominated entries, goal pops and goal
  rejections, rejection classes, peak queue/history, and elapsed time;
* initial heuristic value and a few bounded frontier-ring statistics; and
* success, exact route cost, and materialized-plan identity against Dijkstra.

If those traces show a stable class of expensive requests plausibly favored by
reverse search, implement a fixture-only reverse Dijkstra first. It must prove
cost and materialized-plan equivalence before reverse A* is considered. Then
compare three policies over held-out snapshots: always forward, always reverse,
and a cheap selector. A selector is worthwhile only if its end-to-end latency,
including analysis and mispredictions, beats the better simple default by a
meaningful margin.

The most likely near-term win is therefore stronger or cheaper goal-side
potentials and better diagnostics, not automatic reversal of the exact search.
