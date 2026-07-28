# AutoTerrainDesignations

AutoTerrainDesignations plans and evaluates terrain work needed to keep
Captain of Industry structures accessible.

## Accessway pathfinding

**Terrain-extrema landscaping heuristic**:
An admissible V2 A* estimate of unpaid future landscaping work derived from
favorable terrain extrema and a proven charge horizon, independent of how the
extrema are queried.
_Avoid_: Recursive-diamond landscaping heuristic

**Charge horizon**:
A lower bound on the number of future charge-owning V2 slices that every legal
continuation must enter before reaching any compatible terminal. Straight and
strafe transitions each consume one charge. Turns consume a traversal move but
own no generated slice, so they affect charge-bounded spatial reach without
advancing the charge index.
_Avoid_: Contact distance, travel horizon

**Charge-indexed contact reach**:
The union of contact supports across the exact charge-bounded relaxed V2
transition envelope. The relaxation preserves transition displacement,
orientation, enabled band profiles, charge ownership, zero-charge turns, and
the mandatory charged ramp straight after a turn, while ignoring obstacles,
history, tower bounds, ocean, and candidate feasibility. It is therefore a
conservative spatial superset of real contact reach.
_Avoid_: Contact horizon, local-fallback radius

**Band floor**:
The minimum physical corner elevation of the current enabled two-lane V2 band.
It is the scalar height used by the baseline terrain-contact relaxation.
_Avoid_: Band center, maximum frontage height

**Terrain-contact equality tolerance**:
`0.0001` physical height. Authoritative V2 crest signs and smooth-leveling
compatibility treat a profile and precise terrain within this tolerance as
level, so a charge-separation proof must remain strictly above the terrain
ceiling plus this value.
_Avoid_: Floating-point guard, arbitrary epsilon

**Contact support**:
The full discrete perimeter of the current 4×8 two-lane V2 band, through which
any legal terrain terminal could first form.
_Avoid_: Work stencil, shared support stencil

**Work support**:
The six unique physical corner positions of the two origins owned by one
charge-owning V2 transition. Each origin contributes four scorer samples, with
the two shared seam corners deduplicated only for geometric terrain-extrema
queries. For an ordinary straight, the two exterior-ray roots are among these
six positions. Cost evaluation retains the authoritative per-origin
multiplicity, including counting a shared corner once for each origin.
_Avoid_: Contact stencil, shared support stencil

**Work domain**:
The exact swept union of terrain samples that any of the first `Kcharge`
straight or strafe transitions in the relaxed V2 envelope could inspect:
direct-work corners, ray roots, and complete configured ray traces. Turns can
change this shape but contribute no work samples. The domain is generally
directional and is not a Manhattan diamond.
_Avoid_: Work diamond, synthetic-route footprint

**Work gap**:
A conservative band-floor-to-terrain-ceiling separation using one global
maximum over the complete swept work domain. The resulting single favorable
flat plane is used for every slice and ray in the charged prefix. The table
generator restores the exact per-corner ramp profile from the band floor; the
scalar gap does not replace that geometry. The work gap is independent of any
stronger terrain separation used only to prove a charge horizon.
_Avoid_: Contact gap, terminal gap

**Synthetic straight dominance**:
On the favorable flat-terrain plane, a maximum-grade ramp-down straight is no
more expensive than a strafe for the same charged slice and is unchanged by
rotation. Strafes spend a charge without lowering the band; turns spend no
charge and make no progress toward the plane. A straight maximum-grade descent
therefore minimizes synthetic dumping work for a fixed charge prefix.
_Avoid_: Straight-only assumption

**Disjoint heuristic composition**:
The existing V2 potential and the terrain-extrema landscaping bound may be
added because they lower-bound disjoint portions of every continuation cost.
The potential covers traversal, generated fixed overhead, spokes, terminal
fees, and the ground suffix; the landscaping bound covers only unpaid direct
and exterior-ray work in its proven charged prefix.
_Avoid_: Maximum composition, duplicated landscaping

**Terrain-mask coverage**:
Exact contact and work masks are intersected with the physical map because
exterior positions cannot be exploited by a legal continuation. Tower-area and
ocean samples remain included. If any required in-map precise-terrain sample is
missing from the immutable snapshot, the landscaping heuristic for that state
fails open to zero.
_Avoid_: Missing-sample omission, exterior sentinel terrain

**Authoritative relaxed transition envelope**:
The relaxed mask generator reuses the production V2 transition geometry and
profile advancement. It relaxes eligibility and history availability, but does
not reimplement displacement, turn geometry, post-turn obligations, or work
ownership. A transition consumes one charge exactly when its authoritative
`Delta` is nonempty.
_Avoid_: Heuristic movement model, duplicated transition geometry

**Heuristic artifact ownership**:
Contact masks are process-static geometry artifacts. Work masks are keyed by
ray-trace configuration. Synthetic work tables are keyed by every scorer
setting that changes their values. Any later cache of translated terrain
extrema is owned by the immutable terrain snapshot. A key mismatch rebuilds the
artifact or weakens the heuristic to zero.
_Avoid_: Global terrain cache, partial configuration key

**Experimental heuristic gate**:
The first implementation is a non-persistent A* ordering experiment. Coverage
or invariant failure weakens it to zero. It remains disabled unless A* matches
Dijkstra optimal results and improves end-to-end search time and queue pressure
after including its own evaluation overhead.
_Avoid_: Visited-count success criterion, serialized heuristic state

**Heuristic numerical policy**:
V2 profile heights retain their exact integer half-level representation and
terrain retains its captured `float` values. Contact uses the authoritative
`0.0001f` equality tolerance. Runtime-generated shared-scorer table values are
stored one representable `float` step toward zero, with no other
heuristic-specific epsilon or rounding.
_Avoid_: Arbitrary safety epsilon, serialized work table
