---
status: accepted
---

# Active soil import for farmland filling

ATD farmland filling currently relies on ordinary terrain-dumping logistics.
Vanilla terrain balancing ignores output buffers whose effective priority is
15, so a neutral storage (neither importing nor exporting) cannot supply soil
to a farmland dumping designation even when material is present.

Define active soil import as a fallback demand for still-pending,
ATD-managed farmland filling. ATD will compose the existing vanilla pickup and
dumping job machinery through a localized dispatcher rather than patching the
global balancing sweep. The dispatcher will use any registered output with an
eligible farmable material, preserve vanilla output/product ordering and
protected import quantities, and allow neutral storage contents as the
low-priority fallback. It will target only the closest valid farmland
designation, choose the closest eligible free global truck to the source, and
use the vanilla partial-load policy.

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

An origin may have at most one active-import truck in flight at a time; other
origins may proceed in parallel. The slot lasts from dispatch through normal
completion or cancellation. The tower's live dumpable-material list controls
new imports: final filling may initialize that list to all farmable materials,
but removing a material later prevents new chains for it. Already-reserved
vanilla chains are allowed to finish.

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

The active-import target is only the initial target. If the truck still carries
soil after fulfilling it, the normal vanilla dumping job may continue to the
highest-priority, closest valid designation or fall back to ordinary cargo
disposal. ATD does not attempt to calculate a target's exact remaining volume.
The active-import `DumpingJob` receives only its selected farmland origin as
the primary target; ATD supplies no precomputed nearby extra designations.

Target ranking first discards origins for which no eligible truck can reach
both endpoints, then prefers the closest remaining target to the source; a
nearer universally unreachable origin cannot block a farther reachable one.

Truck selection filters for reachability to both source and target before
choosing the closest eligible truck to the source; a blocked nearest truck is
skipped in favor of a farther truck that can reach both.

This seam keeps source reservations, cargo handling, designation reservations,
navigation, and cancellation inside vanilla job implementations. It avoids a
Harmony patch into the private global balancing sweep, while accepting a
narrow, read-only compatibility adapter for vanilla's private product-indexed
output registry and explicitly mirroring the relevant selection rules in ATD.

The dispatcher is runtime-only. It does not change storage sliders, create
ATD-owned saved logistics entities, or add persistent farmland intent.
Origin-slot bookkeeping is also runtime-only and is rebuilt after load from
saved vanilla truck jobs and designation reservations, so no ATD logistics
record is required for save compatibility or removal.

If no eligible source or truck can complete an import, the farmland remains
pending indefinitely without a new player-facing notification, matching
vanilla's silent no-soil behavior. The existing farming status/debug surface
distinguishes a route-blocked source, a genuinely unavailable soil source, no
eligible truck, and an otherwise eligible truck set blocked by source/target
reachability.

Active soil import is automatic during final filling and has no separate
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

The dispatcher preserves ordinary truck and source eligibility, including
allowed truck groups, job filters, logistics zones, reachability, amphibious
requirements, and assigned-building restrictions. Vanilla route assignments
also constrain the target tower: if one or more storages are assigned as
outputs to that tower, only those storages may supply the tower's farmland
filling products. There is no analogous "allow non-assigned input" switch on
the tower, so an unassigned storage cannot bypass that constraint. If no
explicit tower route constrains the target, normal output-side assignment
rules still apply, including the source's own allow-non-assigned-output setting.
When an origin is managed by multiple servicing towers, a source is eligible
when at least one of those towers is an allowed route match; it is rejected
only when no servicing tower permits that source.
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
