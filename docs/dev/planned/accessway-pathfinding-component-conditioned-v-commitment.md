# Component-Conditioned V Commitment Potential

Status: deferred. Do not implement without a transformed-graph dominance proof
and evidence that sparse `P` leaves material queue work to remove.

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

The last condition is unresolved: cleanup, projected history, alternate
handoffs, adapters, profiles, and ties may invalidate it. Such an origin must
become a merge seed or the field must weaken to `P`.

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
fixed/source V              owner GlobalP
```

One origin may have `P(o)`, `S_C1(o)`, and `S_C2(o)`; ownership is not a global
partition of physical origins.

## Proof and validation questions

Before revisiting this, settle:

1. the graph fact that makes each useful same-`C` shortcut reach P-owned V;
2. cleanup and history effects that invalidate it;
3. the minimal safe `B_C` and label-dominance key;
4. band query semantics and directed handoff/spoke costs; and
5. whether this still improves runtime after sparse `P` and terrain extrema.

Reference Dijkstra must use the same ownership and graph pruning as A*.

