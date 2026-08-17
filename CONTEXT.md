# AutoTerrainDesignations

AutoTerrainDesignations plans and evaluates terrain work needed to keep
Captain of Industry structures accessible.

## Farmland preparation

**Farmland intent**:
A standing player-authored desired state for flat, farmable ground at a target
height. It remains after fulfillment until the player removes it or successful
farm placement consumes it. Materialized terrain work may temporarily suppress
its visualization without removing the intent.
_Avoid_: One-shot preparation job, leveling proxy

**Farmland intent cancellation**:
Explicit removal of farmland intent and all structurally owned materialized
work justified only by it. Shared support work remains while another intent
still justifies it.
_Avoid_: Hide intent, clear overlay only

**Active soil import**:
A transient logistics behavior that treats pending farmland filling as a
material demand: a free global truck may collect an eligible soil material
from any registered output and deliver it to a specific materialized farmland
filling designation owned by the active farmland workflow. Normal output
priority and protected import quantity still apply; neutral storage contents are
therefore a low-priority fallback. It does not preempt ordinary dumping; it
intervenes only for still-pending farmland filling. Final filling may initially
allow all farmable materials, but the tower's live dumpable-material list is
consulted for each new import, so the player can remove a material during
filling. It does not create persistent farmland intent, claim tower ownership,
or change a storage's player-configured import/export settings.
The source's import reserve remains a hard quantity limit, and disabling
logistics output removes the source entirely. The export-from slider retains
vanilla meaning as a priority preference: material below its threshold may
still be used as the lowest-priority fallback rather than becoming absolutely
unavailable.
Active soil import only reads the live storage-to-tower route graph; it never
creates temporary routes, changes assignments, or broadens a player-authored
route.
Route edits affect only future active-import dispatches. An already reserved
vanilla pickup/dump chain is allowed to finish or cancel under its normal job
rules.
Removing a material from the live tower list does not cancel an already
reserved import chain; it only prevents new chains for that material.
Truck load sizing follows the world's vanilla partial-load policy rather than
an ATD-specific full-or-partial threshold.
At most one active-import truck may be dispatched for a target origin at a
time; distinct farmland origins may import soil in parallel.
Within one filling pass, the dispatcher may greedily issue one eligible import
per origin until no source/truck match remains. There is no additional ATD-wide
truck cap: 100 eligible origins may involve 100 trucks when vanilla source
quantity and truck eligibility permit it.
Each greedy iteration is globally priority-driven by the eligible source and
product, then selects the closest reachable farmland target and closest
eligible truck; origin enumeration order does not override vanilla priority.
Equal-priority candidates resolve by proximity, with a stable origin/entity-ID
tie-break only for final deterministic ties.
Source ranking uses vanilla `RegisteredOutputBuffer.CombinedPriorityCached`,
including queued-job pressure, rather than raw output priority alone.
Candidate discovery follows vanilla's product-indexed output registry rather
than scanning every world entity each tick. ATD uses a small, read-only,
version-guarded compatibility adapter over that registry; if the expected
internal shape is unavailable, active import degrades without dispatching
rather than changing the player's logistics state.
When several free trucks could serve an import, the closest eligible truck to
the selected soil source is preferred, matching vanilla logistics. Eligibility
is checked against both source and target first, so a blocked nearest truck is
skipped in favor of a farther truck that can reach both.
For multiple eligible farmland origins, the closest valid target to the selected
source is preferred, also matching vanilla dumping. Target ranking first
discards origins for which no eligible truck can reach both endpoints, so a
nearer universally unreachable origin cannot block a farther reachable one.
When several allowed farmable materials are available, their normal logistics
priority and source proximity decide which material is imported; active soil
import adds no material preference.
After the initially targeted farmland origin is fulfilled, any remaining cargo
is ordinary truck cargo and follows vanilla continuation and disposal rules,
including priority and proximity to the next valid dumping designation.
The active-import `DumpingJob` receives only its selected farmland origin as
the primary target; ATD supplies no precomputed nearby extra designations.
Active-import slot bookkeeping is runtime-only and is rebuilt from live vanilla
truck jobs and designation reservations after loading a save.
If no eligible source or truck can complete an import, filling remains pending
indefinitely with no new player-facing notification, matching vanilla's silent
no-soil behavior; diagnostics may still expose the waiting reason. The existing
farming status/debug surface distinguishes a route-blocked source, a genuinely
unavailable soil source, no eligible truck, and an otherwise eligible truck set
blocked by source/target reachability.
Active soil import is automatic during final filling and has no separate user
toggle; existing farming automation and live tower material controls remain the
control surfaces. Dispatch attempts run from the existing farming-session
filling pass once per farming automation tick; no separate global logistics or
simulation loop is introduced.
Any live vanilla dumping job or designation reservation claims an origin, so
active import does not preempt, duplicate, or second-guess ordinary work even
when that job is delayed or unreachable.
An origin must remain without an ordinary vanilla job or reservation for one
complete farming tick before active import dispatches, so fallback behavior does
not depend on simulation-event ordering.
If an ordinary claim appears during that grace period and then cancels, the
grace period resets and another complete no-claim tick is required.
Dispatch also requires vanilla `TerrainDesignation.CanBeAssigned(false)`;
the normal `DumpingJob` creates the actual designation reservation and remains
the authority for assignment races. ATD's one-origin slot is an additional
anti-flocking guard only.
If source reservation fails while a candidate is being dispatched, that
candidate is discarded and the same pass continues matching remaining eligible
sources, trucks, and origins.
Dispatch eligibility follows the current filling analysis rather than the
stabilization phase label: each pass rechecks origins first, and a landslide or
other change that makes an origin unfulfilled can re-enable active import even
during stabilization. A pass with no pending origins creates no new chains.
Target eligibility uses vanilla's per-truck reachability result: an origin may
be eligible for one candidate truck and unreachable for another, and an origin
that the selected truck cannot reach does not consume a slot. Normal zone,
amphibious, and path restrictions remain part of that check. Active import
reuses vanilla's current per-truck unreachable cache and leaves retry timing
and invalidation to vanilla; ATD does not force fresh pathfinding attempts.
It preserves ordinary truck and source eligibility, including allowed truck
groups, job filters, logistics zones, reachability, amphibious requirements, and
assigned-building restrictions. Route assignments are authoritative for the
target tower: when one or more storages are assigned as outputs to that tower,
only those storages may supply the tower's farmland filling products. Vanilla
has no corresponding "allow non-assigned input" escape for a tower, so an
unassigned storage is excluded in that case. When no explicit tower route
constrains the target, the source still follows vanilla output-side assignment
rules, including its own non-assigned-output setting. If an origin is managed by
multiple servicing towers, a source is route-eligible when at least one of
those towers is an allowed match; it is rejected only when no servicing tower
permits that source.
The route endpoint set is the designation's current live `ManagedByTowers`
collection at dispatch time, not a stale farming-session tower assumption.
If that collection is empty, active import waits; the designation is then
governed only by ordinary global dumping rules, which may or may not allow the
soil materials.
Route eligibility is evaluated before normal material, source, and proximity
priority, so an unrouted source cannot compete merely because it has a better
logistics priority.
Dispatch composes a vanilla `BalancingJobSpec` and uses
`DefaultTruckJobProvider.AssignBalancingJob`; it does not construct pickup or
dumping jobs through a parallel ATD path. A default-provider truck with only
non-true parking/navigation work remains vanilla-available; the provider may
clear those jobs when assigning the import. Trucks with true jobs remain
ineligible.
Each active-import load contains only its selected farmable product; no
secondary output buffers are added.
_Avoid_: Farmland fill demand, tower truck import, temporary storage export

**Pending farm placement**:
A committed farm placement blocked only by terrain preparation and retained
while its generated farmland intent is fulfilled. It replays when that intent
is satisfied. Explicitly cancelling any required farmland intent cancels the
pending placement. If pending placements overlap, the first successful
placement consumes its footprint intent and cancels every competing placement
that requires any consumed origin. If replay becomes impossible for a new
non-terrain reason, the placement is cancelled but its farmland intent remains.
Repainting any required origin to a different target height also cancels the
placement while retaining the new intent.
_Avoid_: Placement preview, failed build command

**Unmanaged farmland intent**:
Farmland intent whose center tile is not covered by a suitable servicing tower.
It remains valid and visible but produces no materialized work.
_Avoid_: Invalid farmland designation, orphaned work

**Servicing tower**:
A capable area-managing tower whose area contains a farmland intent origin's
center tile. Its job system can service excavation and filling work, and ATD
can control its dumpable-product rules. A temporary lack of assigned vehicles
blocks progress but does not make the tower unsuitable. Several towers may
service the same origin.
_Avoid_: Intent owner, coordinating tower, primary tower

**Materialized farmland work**:
Vanilla terrain designations created or adopted to advance farmland intent.
They remain globally ATD-owned even when no tower currently services them.
_Avoid_: Tower-owned work, ephemeral save work

**Structural work ownership**:
ATD's authority over vanilla work it created or adopted because the live
origin, proto, and complete designation geometry exactly matched a currently
required materialized-work role. The resulting owned-work record must continue
to match before mutation. Fulfillment does not end ownership while associated
farmland intent remains.
_Avoid_: Historical provenance, persistent object identity

**Owned-work record**:
A persisted structural ownership claim for one materialized farmland-work role,
including primary work, shoulders, rims, accessways, and debris cleanup.
_Avoid_: Primary-origin cache, runtime request state

**Farmland preparation cohort**:
Farmland intent and servicing towers connected through shared intent origins.
All preparation in the cohort must complete before any of it enters filling.
_Avoid_: Overlapping tower areas, geographic farmland region

## Accessway pathfinding

**Access obligation**:
A workflow-owned need to connect current terrain work to reachable ground. It
remains the same obligation while world state changes and successive route
attempts are superseded, cancelled, blocked, or completed.
_Avoid_: Access request, pathfinding attempt, work key

**Access request**:
One transient attempt to satisfy an access obligation against a captured view
of the world. Ending a request does not by itself end its access obligation.
_Avoid_: Access obligation, permanent access job

**Access request owner**:
The workflow responsible for an access obligation and for interpreting its
progress, result, and user cancellation.
_Avoid_: Tower owner, pathfinder owner

**Access request logical cancellation**:
The immediate withdrawal of an access request's authority to produce a live
designation commit. Work already executing may still need to reach a safe stop
boundary, but its eventual route or plan cannot be accepted.
_Avoid_: Worker termination, completed cancellation, thread abort

**Search cancellation acknowledgment**:
Confirmation that logically cancelled search work has reached a safe stop
boundary and returned its partial progress and diagnostics without a candidate
plan.
_Avoid_: Logical cancellation, successful search result, silent disposal

**Hard access-request invalidation**:
A change that removes the identity or governing preconditions of an access
request, immediately withdrawing its authority and requesting worker
cancellation. World replacement, owner or obligation removal, and incompatible
access-mode changes are hard invalidations.
_Avoid_: Environmental snapshot dirtiness, ordinary stale validation

**Environmental snapshot dirtiness**:
A relevant live-world change after snapshot capture that may affect the
captured answer without removing the access request itself. Search may finish,
but success requires authoritative live validation and failure cannot establish
an authoritative negative result.
_Avoid_: Hard invalidation, automatically valid stale result

**Captured access snapshot**:
A sealed, data-only view of primitive world facts used by one access request.
It contains no live world references, callbacks, or pure search structures that
can instead be derived by the execution backend, and does not change after
publication.
_Avoid_: Live terrain view, mutable search cache, serialized world state

**Access search workspace**:
The request-local mutable state used to evaluate one captured access snapshot,
including derived indexes and graphs, frontier, history, lazy caches, and
temporary diagnostics. It is owned by exactly one execution context and
discarded after terminal output.
_Avoid_: Captured access snapshot, shared pathfinder cache, persisted request

**Access search policy snapshot**:
The immutable captured values of every configurable rule, cost, limit, and
feature flag that can affect one access search's feasibility or result. It is
part of request identity and is the sole configuration authority during that
attempt.
_Avoid_: Live global settings, diagnostic presentation options, mutable policy

**Access search evaluator**:
A pure request-local service reconstructed inside the access search workspace
from the captured access snapshot and policy. It answers graph, feasibility,
scoring, and handoff questions without callbacks to live game objects.
_Avoid_: Snapshot delegate, live-world callback, precomputed all-candidates table

**Search worker**:
A runtime execution component that consumes one immutable captured access
snapshot and request description, evaluates the pure route search away from
the game thread, and returns route diagnostics and a candidate plan for
game-thread validation and commit. It does not capture live world state,
resolve workflow ownership, or mutate terrain designations.
_Avoid_: Worker-owned access request, background game simulation, parallel
access obligations

**Access plan materialization**:
The pure derivation and captured-snapshot validation of a candidate terrain-
designation plan from an access route. It may run with the route search and
does not read or mutate the live world. Its result remains provisional until
designation commit.
_Avoid_: Designation placement, live validation, search result mutation

**Access designation commit**:
The game-thread operation that revalidates a materialized access plan against
the live world and applies its terrain-designation mutations transactionally.
Search completion alone never authorizes commit.
_Avoid_: Plan materialization, worker placement, search completion

**Modeled-rule preservation**:
The promise that making accessway search faster does not exclude a route that
the active physical accessway model permits. Deliberate player-relevance
pruning is a separate optional policy, not an implicit feasibility rule.
_Avoid_: Absolute search completeness, silent futile-route pruning

**Active ray envelope**:
The combined terrain effect currently imposed by a candidate accessway's side
rays. Overlapping contributions with no distinct physical effect collapse;
their chronological generation is not part of the modeled state.
_Avoid_: Ray event history, ray audit trail

**Path-local projected-terrain overlay**:
The approximate terrain effects planned by one candidate accessway, layered
over the shared captured terrain without changing it. A disrupted overlay tile
is unavailable to physical-ground navigation, while its projected cut or fill
surface constrains later generated work.
_Avoid_: Mutated search snapshot, chronological ray constraints

**Immediate-successor clearance waiver**:
The one-step permission for an immediately connected continuation to pass through the
predecessor fringe's projected terrain effect. The effect remains persistent,
new work is still costed, and no later continuation inherits the waiver.
_Avoid_: Deleted predecessor rays, replaceable fringe terrain

**Height-aware same-sort contact**:
A cut ray resolves when its incoming slope reaches or rises above an earlier
projected cut surface; a deeper cut pays only the remaining gap and continues.
A fill ray mirrors this rule, resolving at or below an earlier fill surface and
continuing above it. Static blockers remain authoritative until resolution.
_Avoid_: Presence-only ray termination, same-sort blocking

**Projected-contact safety boundary**:
The ordinary post-termination safety margin applied after a ray resolves
against projected terrain. It adds no landscaping work but must remain clear
of hazards, including non-exempt designation footprints that emitted earlier
rays.
_Avoid_: Immediate projected-ray stop, duplicate work charge

**Projected-work span**:
The active portion of a ray where positive cut or fill work remains. Its
projected heights participate in later ray resolution and projected-work
credit.
_Avoid_: Complete disturbed span, safety buffer

**Ray safety-only span**:
The precautionary area after ray termination. It is disrupted and unavailable
to G but supplies neither projected height nor landscaping credit. A later
same-sort ray may cross it, while opposing ray work and any non-exempt V
footprint are fatal.
_Avoid_: Projected ground, free work span

**Connected-predecessor disturbance guarantee**:
The game-mechanical protection enjoyed by the immediately connected
predecessor across the guaranteed single lateral-band gap. Ray clearance and
safety-buffer checks exempt that predecessor, but not older nearby origins.
_Avoid_: General history exemption, predecessor safety scan

**Connected-convex disturbance safety**:
The physical principle that a connected convex terrain-work area is safe from
its own projected disruption because no terrain material runs farther than the
artificial slope joining it. It justifies simple local exemptions but does not
require the search to prove arbitrary convex regions.
_Avoid_: Convex-region search rule, universal connected-work exemption

**Two-direction self-disruption safety**:
A connected V path whose longitudinal travel uses no more than two cardinal
directions is safe from its own projected disruption. Lateral strafes preserve
the current travel direction and do not introduce another direction; exterior
rays in such a path cannot reach an earlier origin footprint.
_Avoid_: Two-axis rule, turn-count limit

**Uninterrupted V segment**:
One connected run of generated V work. Entering physical or projected fixed
ground ends the segment and a later V launch starts a new direction-safety
scope, while all projected terrain effects remain part of the complete route.
_Avoid_: Complete accessway route, global V direction set

**Incremental self-disruption validation**:
An extension validates its new profiles against the established projected
terrain and its new rays against established non-exempt origins. Previously
accepted profiles and rays remain established and require no retroactive audit.
_Avoid_: Whole-history revalidation, final-shape safety proof

**Direction-introducing turn safety**:
A turn that introduces a third travel direction emits its outer ray in the old
direction, so that ray retains the established prefix's self-disruption safety.
Stricter origin checks begin with later rays emitted in the new direction.
_Avoid_: Third-direction turn audit, retroactive turn rejection

**Opposing-work conflict**:
A later generated cut or fill that would fight the persistent projected effect
of the opposite operation. The conflict is fatal even when it occurs far from
the segment that created the earlier effect.
_Avoid_: Ray crossing, same-sort overlap

**Generated-profile ray-envelope contact**:
The comparison of a later generated profile corner with an earlier projected
ray surface. A corner that continues the same terrain operation or reaches
captured terrain without opposing work is compatible even when its target does
not extend the earlier projected height. A corner that actually performs the
opposite operation is fatal, and an accepted corner may emit a new ray of its
own.
_Avoid_: Leveling ray, automatic ray continuation

**Projected-work credit**:
The landscaping work already represented by a compatible projected terrain
effect. Later work pays only the additional cut or fill beyond that projected
surface rather than paying again from captured physical terrain, regardless of
whether existing designations or the current candidate projected the effect.
_Avoid_: Free additional work, duplicate landscaping charge

**Projected-envelope work charge**:
The marginal landscaping charge for the complete clearance-dilated terrain
effect contributed by a generated ray. It uses the same projected cut ceiling
or fill floor that later profiles can credit, collapses overlap with captured
designation work, earlier path-local rays, and other rays in the same
transition, and retains the configured per-ray cap and unresolved penalty.
_Avoid_: Centerline-only ray charge, duplicate envelope charge

**Ambiguous opposing projection**:
A tile onto which existing terrain work projects both cut and fill effects.
Because its eventual surface is execution-order dependent, it is disrupted for
G and a hard blocker for newly generated accessway work.
_Avoid_: Blended projected height, selectable projection order

**T3 accessway model**:
The current two-lane accessway model for T3/Mega vehicle clearance. It is the
immediate refinement target and may later supply rules shared with the
single-lane T1/T2 models without requiring premature unification.
_Avoid_: Universal accessway model, permanently T3-only model

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

**Sparse V-type route potential**:
A reverse shortest-path lower bound over reusable FV and actual 4x4 generated
V origins. Generated cardinal propagation charges four traversal units plus one
generated-origin fixed overhead; goal-connected physical G contributes exact
suffix values only at useful contacts, while non-goal G uses a separate
component escape field rather than becoming dense global potential nodes. A
lookup at a generated V origin assumes that origin's fixed overhead is already
paid; a forward edge charges it only when entering another generated V origin.
FV may use relaxed diagonal travel at four square-root-of-two, while generated
V propagation remains cardinal. A live width-two band initially queries the
minimum potential of its currently paid origins; a stronger paired-band bound
requires a separate lane-projection proof.
_Avoid_: Terrain-extrema heuristic, physical-tile F/4 smear, dense all-G P

**Minimum center-spoke cost**:
The shared V2 cost-model lower bound for the vehicle-center movement between a
V-type route and physical G. It is derived from the generated-origin cost model
and prevents a relaxed G-to-V-to-G route from understating the corresponding
ground travel; it is not a player policy setting.
_Avoid_: Handoff tuning knob, zero-cost G/V contact

**Component-local G escape field**:
A lazy reverse lower bound over one non-goal physical-G component. It reaches
only canonical G-to-V launch positions, then inherits the sparse V-type route
potential after the minimum center spoke; a first generated-origin overhead is
added only when that contact actually enters generated V rather than reusable
FV.
It guides G labels only and neither owns V labels nor changes exact search
feasibility. It and the global sparse potential are cached for one immutable
request snapshot; incremental cross-cluster repair is a later refinement.
_Avoid_: Potential-owner state, eager all-component field

**Catalogued potential contact**:
A sparse route-potential edge admitted solely by immutable V-prime catalog or
fixed-navigation membership. It deliberately omits profile resolution, corner
compatibility, handoff geometry, and transition-band budget; exact search is
the sole feasibility authority.
_Avoid_: Resolved transition, heuristic construction work

**Component-conditioned V commitment potential**:
A replacement route potential for a V label launched from one non-goal G
component. It inherits complete global-potential values at that component's
useful V merge fringe and flood-fills backward through its sparse V shadow at
one cardinal-origin traversal plus fixed overhead per step. The source
component is carried as potential ownership; the field replaces rather than
augments the global route potential while that commitment remains active.
Returning to the source component remains legal after the uninterrupted V
segment has reached the useful merge fringe.
_Avoid_: Additive V surcharge, component-independent V potential

**Potential-owner state**:
The identity selecting either the global mixed route potential or the
component-conditioned field belonging to the G component from which the
current uninterrupted V route launched. One physical origin may have values
for several component owners; reaching the owner's useful V merge fringe
returns ownership to the global field.
_Avoid_: Physical-origin partition, generated history

**Useful V merge fringe**:
The first P-owned continuation at which an uninterrupted V segment launched
from a G component has paid for non-G-equivalent progress. Before this fringe,
returning to the source component is dominated by remaining on G and avoiding
the V overhead. After this fringe, returning to that same component is legal
because the V segment may represent a genuine shortcut through it.
_Avoid_: Any origin catalogued by P, permanent same-component-return ban

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
continuation must enter before reaching a useful projected-ground endpoint
(physical G or FV). Straight and
strafe transitions each consume one charge. Turns consume a traversal move but
own no generated slice, so they affect charge-bounded spatial reach without
advancing the charge index.
_Avoid_: Kfixed, contact distance, travel horizon

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

**Heuristic safety gate**:
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
