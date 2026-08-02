# Component-Conditioned V Commitment Potential

Status: transformed ownership foundation implemented on 2026-08-02. Frontier
diagnostics showed that G-launched V descendants account for most late V
expansion. Exact labels now retain source-component ownership until they cross
a center edge that static G cannot reproduce; pre-commit returns to that
component are suppressed, while post-shortcut returns and returns to another
component remain valid. The numeric `S_C` strengthening remains pending.

Related designs:

* [Sparse V-Type Route Potential](accessway-pathfinding-sparse-v-route-potential.md)
* [Terrain-Extrema Landscaping Heuristic](accessway-pathfinding-terrain-extrema-landscaping-heuristic.md)

## Purpose

This is a possible strengthening of sparse global route potential `P` for a V
label launched from non-goal ground component `C`. It is not part of the
initial heuristic and must not silently change V2's exact graph.

The intended field `S_C` applies while a label is committed to reaching a safe
merge fringe `B_C`:

```text
S_C(o) = min over q in B_C of VCost(o, q) + PBoundaryCost_C(q)
VCost(o, q) = originSteps(o, q) * (4 + F)
```

`PBoundaryCost_C(q)` is normally `P(q)`. If global `P(q)` returns directly to
`C`, an immediate useful handoff must instead seed the handoff edge plus its
downstream `P` value explicitly. Arbitrary boundary costs require a
multi-source weighted reverse flood/Dijkstra even though generated V steps are
uniform.

## Terms and merge fringe

`PotentialOwner` is the identity carried by a V label selecting either global
`P` or `SourceGroundComponent(C)`. The *P-owned V region* includes all V
origins whose Bellman-minimal `P` continuation uses generated V travel,
compatible fixed targets, or useful exits; it includes all equal-cost choices,
not a single shortest-path-tree parent. The *V shadow of C* is the sparse V
domain where `S_C` is relevant.

Candidate `B_C` members are:

* V origins whose Bellman-minimal `P` continuation uses V;
* equal-cost V ties and compatible fixed targets;
* useful handoffs to G components other than `C`; and
* a return to `C` only after `P` has represented its useful V portion.

The last condition does not make same-component returns categorically invalid.
A pre-fringe return is dominated by staying on `G` in `C` and avoiding the V
overhead. A post-fringe return remains part of the exact graph because the V
prefix may be a genuine shortcut through `C`. Merely finding an origin in the
sparse `P` dictionary is not sufficient: the implemented sparse field contains
eligible generated origins over `C` as well. The merge test must identify a
P-owned continuation that represents non-G-equivalent progress.

## Required exact-state semantics

`S_C` replaces `P`; it is never added:

```text
Hroute = P when globally owned; S_C when V-committed from C
```

It is unsafe to attach a larger heuristic while retaining an immediate
V-to-`C` return: that continuation could cost less than `S_C`. A sound model
would use the corresponding transformed graph, likely with `PotentialOwner` in
the V-label key:

```text
G in C -> generated V       owner C
V owned by C -> shadow V    owner C
V owned by C -> B_C         owner GlobalP
V owned by C -> other G D   leave V; later launch uses D
V globally owned -> G in C  legal same-component return
fixed/source V              owner GlobalP
```

One origin may have `P(o)`, `S_C1(o)`, and `S_C2(o)`; ownership is not a global
partition of physical origins.

## Proof and validation questions

The numeric field implementation and validation must settle:

1. the minimal conservative test for non-G-equivalent V progress;
2. cleanup and projected-history cases that require weakening rather than
   excluding a route;
3. band query semantics and directed handoff/spoke costs; and
4. whether the transformed field reduces queue work on Cluster 2.

Reference Dijkstra must use the same ownership and graph pruning as A*.
