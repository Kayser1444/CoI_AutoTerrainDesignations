# AutoTerrainDesignations

AutoTerrainDesignations plans and evaluates terrain work needed to keep
Captain of Industry structures accessible.

## Accessway pathfinding

**V2 source launch**:
A feasible two-slice, 2×2-origin prefix whose initial slice contains a source
obligation and whose successor slice proves the first longitudinal Mega move.
Each origin may reuse compatible fixed work or use newly planned route-profile
work, including one bounded V2 transition adapter; the initial slice adds no
traversal, while its successor does.
_Avoid_: Synthetic companion, flat 2×2 seed, exposed source frontage

**V2 source center**:
The set of source origins sharing the minimum distance from the source
cluster's arithmetic center. Every tied origin is equally central; none is
selected by a coordinate tie-break.
_Avoid_: Tie-broken start origin, single center tile

**V2 source-center distance tier**:
A set of source origins sharing the same distance from the source cluster's
arithmetic center. V2 tries tiers from the center outward and advances to a
less-central tier only when every route from the current tier fails. A backup
tier is skipped unless it contributes a launch search state that no earlier
tier explored; coordinate reuse with a distinct axis, profile, or navigation
identity remains a novel fallback.
_Avoid_: Perimeter seed scan, synthetic source-distance penalty

**V2 fixed-navigation profile**:
An existing terrain-work shape through which a width-two search may navigate
using its exact finished geometry and physical clearance. Ordinary flat, ramp,
and V-prime corner profiles are current examples; eligibility is not a shape
whitelist and does not imply that an accessway may generate the shape.
Navigation eligibility is independent of origin-cluster ownership and does not
by itself establish a grounded terminal.
_Avoid_: Endpoint-only profile, generatable profile

**V2 route profile**:
A terrain-work shape that a V2 accessway may generate and propagate. The
current set contains ordinary flat and axis-aligned ramp profiles, excluding
corner and saddle profiles. Existing traversable profiles are instead projected
into ground navigation at their finished target surface.
_Avoid_: Fixed-navigation profile, any traversable profile

**V2 transition adapter**:
The bounded width-two generated repair at a projected-ground boundary. It uses
one longitudinal slice for a jagged fringe or one complementary two-slice pair
for a slanted fringe; a third slice is forbidden. Its far side may be a
propagating route-profile band or another projected surface, and each lane may
reuse compatible fixed work or use newly planned V or canonical V-prime work.
It is admitted by exact Mega connectivity, has no projected-side orientation,
and never becomes a propagating move type or satisfies a route-profile
predecessor requirement. After acceptance its slices become ordinary fixed
target context and may seed transition work in a later provisioning search.
_Avoid_: V-prime route profile, transition-band chain, lateral-exit special case

**V2 transition crossing**:
An exact Mega vehicle-center path through a resolved V2 transition adapter,
charging unit cardinal or square-root-of-two diagonal travel independently of
the band’s generated work. It has no categorical straight, strafe, turn, or
spoke travel fee.
_Avoid_: Transition surcharge, synthetic strafe

**Accessway-owned origin**:
An origin deliberately included in newly planned accessway terrain work. It
remains an explicit designation when its target already matches current terrain,
so later disturbance is restored while the provider is being established; a
candidate is invalid if that designation cannot be retained.
_Avoid_: Exact-terrain no-op, omitted companion

**V-prime candidate origin**:
A non-fixed origin with one to three cardinal non-conflicting fixed-target
neighbours, marking where lazy transition-adapter resolution may consider
generated V-prime work. Every terrain-designation target seeds the catalog
regardless of current Mega connectivity, while physical ground does not;
diagonal neighbours constrain shared corner heights during resolution but do
not themselves add or pre-remove entries. Only catalogued origins may receive
newly planned V-prime profiles; generated history never seeds more candidates
within the same search. The catalog is refreshed whenever accepted accessway
work changes the fixed-target snapshot.
_Avoid_: V-prime eligible origin, diagonal candidate halo

**FV navigation space**:
The directionless, clearance-exact Mega navigation space over compatible
width-two fixed-navigation bands. Cardinal travel costs four, while diagonal
travel costs four square-root-of-two and is legal only when both corresponding
cardinal sweeps are free.
_Avoid_: Propagating V space, optimistic fixed-origin adjacency

**Projected fixed-ground graph**:
The unified clearance-exact Mega navigation graph joining FV navigation space
to physical ground. It permits travel through fixed work without V-prime
generation transitions or exposed fixed frontages.
_Avoid_: Optimistic provider field, generated V graph

**Projected-ground heuristic relaxation**:
The G-like lower-bound layer over projected fixed terrain and possible V2
transition adapters, with unit cardinal and square-root-of-two diagonal travel.
It omits transition construction cost and may admit edges that fail real
clearance or steepness, including chains of candidate bands forbidden by exact
search, because extra heuristic edges can only shorten the estimate; exact
search still validates and charges every route.
_Avoid_: Exact projected-ground graph, clearance or steepness proof

**Global mixed route potential**:
A proposed reverse shortest-path lower bound over strict-diagonal physical G,
relaxed-octagonal fixed navigation, and actual 4x4 generated V origins.
Generated cardinal propagation charges four traversal units plus one
generated-origin fixed overhead. Goal G and matching fixed targets seed zero;
handoff, terminal, and grounded suffix costs remain part of the field.
_Avoid_: Terrain-extrema heuristic, physical-tile F/4 smear

**Component-conditioned V commitment potential**:
A proposed replacement route potential for a V label launched from one
non-goal G component. It inherits complete global-potential values at that
component's useful V merge fringe and flood-fills backward through its sparse V
shadow at one cardinal-origin traversal plus fixed overhead per step. The
source component is carried as potential ownership; the field replaces rather
than augments the global route potential while that commitment remains active.
_Avoid_: Additive V surcharge, component-independent V potential

**Potential-owner state**:
The proposed identity selecting either the global mixed route potential or the
component-conditioned field belonging to the G component from which the
current uninterrupted V route launched. One physical origin may have values
for several component owners; reaching the owner's useful V merge fringe
returns ownership to the global field.
_Avoid_: Physical-origin partition, generated history

**Resolved V2 move**:
A generated-band movement whose newly encountered origins are resolved as
newly planned V-profile work or transitions into projected fixed ground.
Generated expansion and projected-ground navigation remain parts of one route
rather than separate search phases.
_Avoid_: Fringe handoff, fixed-body preprocessing

**Goal-connected projected-ground component**:
A clearance-exact Mega navigation component containing tower-reachable physical
ground and any fixed target surfaces connected to it. Reaching any node in the
component supplies an exact downstream route to tower ground.
_Avoid_: Fixed-frontage terminal, optimistic provider component

**Fixed-to-ground transition**:
A clearance-exact edge between a fixed target surface and adjacent physical
ground. It belongs to the projected fixed-ground graph and adds physical travel
but no generated designation or generated-work cost.
_Avoid_: Generated V/G seam, fixed frontage fee

**Projected fixed provider chain**:
A complete clearance-exact route from an origin cluster to tower ground through
existing but possibly unfinished terrain work. It requires no new accessway
designations and remains waiting until its fixed work becomes live-pathable
from the ground side.
_Avoid_: Zero-work failure, already-accessible provider

**Greedy outward access provisioning**:
The policy of connecting inaccessible origin clusters from near to far, with
each cluster minimizing its complete grounded route against infrastructure
established by earlier clusters. It deliberately does not jointly optimize the
provider network across all clusters.
_Avoid_: Global access-network optimization, remote-first provisioning

**Accessway route cost**:
An additive comparison of complete grounded driving distance and the work,
cleanup, and origin overhead needed to establish the route. Driving distance
is not a hard priority; the player controls its exchange rate with landscaping
through the landscaping-cost distance scale.
_Avoid_: Shortest driving path, lexicographic travel priority

**Cheapest-label history ownership**:
The bounded-search policy in which the cheapest arrival at a V2 band,
transition continuation, or projected-ground center owns the generated
geometry, ray, and cleanup history used for later expansion. More-expensive
arrivals with different histories are discarded, accepting a small
completeness risk to avoid multi-label state growth; equal-cost ownership uses
a canonical geometry-based tie-break.
_Avoid_: History-complete search, Pareto-label search

**Useful-material rebate**:
The reduction in excavation cost for material that the mining operation values
as useful product rather than waste. It uses the access-planning definition of
useful material, independent of tower scan filters or mining priority.
_Avoid_: Useful-ore rebate, mining-priority discount

**Projected fixed terrain**:
The terrain surface obtained by overlaying reusable fixed designation targets
onto captured physical terrain. Navigation and heuristics use already-scheduled
fixed work at its finished target height, where it contributes no new
landscaping charge.
_Avoid_: Raw terrain under fixed work, zero-height fixed work

**Terrain-extrema landscaping heuristic**:
An admissible V2 A* estimate of unpaid future landscaping work derived from
favorable terrain extrema and a proven charge horizon. Possible V2 transition
bands provisionally count as ground and end any landscaping charge that is not
proven unavoidable before reaching them.
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
