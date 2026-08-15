# Active soil import for farmland filling

Status: ready-for-agent

## Problem Statement

When ATD is filling farmland-preparation origins, it currently relies on
ordinary terrain-dumping logistics to bring soil material to the origins.
Vanilla terrain balancing deliberately does not pair two low-priority output
and input buffers. A storage configured to neither import nor export therefore
cannot be selected as a soil source for dumping, even when it contains exactly
the material the farmland needs. The result is farmland that remains pending
without an obvious reason, while the player must temporarily change unrelated
storage logistics settings or create another exporter.

The behavior is especially surprising because farmland filling is already an
ATD-managed demand, and because the player may have deliberately chosen not to
export the material from a neutral storage. Tower trucks are not the solution:
they escort excavators and are not the trucks that perform dumping. The feature
must therefore use ordinary global trucks, including trucks that vanilla would
consider available after soft release.

## Solution

Add automatic active soil import to the existing ATD farmland filling pass. A
pending, materialized farmland filling origin becomes a localized demand that
can dispatch a vanilla balancing pickup-and-dump chain. The chain collects one
eligible farmable material from a registered output and targets that specific
farmland origin. Neutral storage contents are accepted as a low-priority
fallback, while all normal vanilla source, route, product, quantity, truck,
and reachability rules remain in force.

The dispatcher runs from the existing farming-session filling tick. It uses a
read-only, version-guarded adapter over vanilla's product-indexed output
registry instead of scanning every world entity. It filters candidates using
the live storage-to-tower route graph, the live tower dumpable-material list,
and vanilla per-truck reachability. It then applies a greedy, vanilla-shaped
selection policy and delegates job construction to the default vanilla truck
job provider. The behavior is runtime-only and requires no new player toggle,
saved logistics object, temporary route, or storage-slider mutation.

## User Stories

1. As a player preparing farmland, I want soil already present in a neutral storage to be usable for filling, so that I do not have to change that storage's export setting merely to finish a farm.
2. As a player, I want active import to happen automatically during final farmland filling, so that I do not need to micromanage a separate logistics job.
3. As a player, I want ordinary global trucks to perform active soil import, so that the feature uses the same vehicle pool that vanilla terrain dumping uses.
4. As a player using soft release, I want a truck with only vanilla parking or navigation work to remain eligible, so that released trucks behave as vanilla-available trucks.
5. As a player, I want trucks with true work or non-default providers excluded, so that active import does not interrupt committed work or special vehicle workflows.
6. As a player, I want tower trucks to remain escort vehicles only, so that active import does not incorrectly assign dumping work to them.
7. As a player, I want the tower's final-filling material list to initially include every farmable product, so that normal preparation does not unexpectedly exclude a usable soil material.
8. As a player, I want removing a material from the tower's live dumpable list to prevent new active-import chains for that material, so that I retain control over what may be dumped.
9. As a player, I want a chain already reserved before I remove a material to finish or cancel under vanilla rules, so that changing a live rule does not corrupt an in-flight job.
10. As a player, I want neutral-storage material to be lower priority than normally exported material, so that active import follows vanilla logistics preferences whenever an ordinary source is available.
11. As a player, I want an import reserve to remain protected, so that active import cannot consume material reserved for a storage's configured imports.
12. As a player, I want disabling logistics output on a source to remove it from active import, so that the feature respects an explicit player shutdown.
13. As a player, I want the export-from slider to retain vanilla priority semantics rather than becoming an absolute ban, so that below-threshold material can still serve as the final low-priority fallback when appropriate.
14. As a player, I want explicit storage-to-tower routes to constrain active import, so that a tower supplied by assigned storages does not silently draw from unrelated storages.
15. As a player, I want no implicit allow-non-assigned-input escape for a tower, so that an unassigned storage cannot bypass an explicit tower route.
16. As a player, I want a source accepted when at least one of several servicing towers permits its route, so that overlapping tower management does not reject a valid source unnecessarily.
17. As a player, I want route edits to affect future dispatches only, so that an already reserved vanilla chain is not rewritten mid-trip.
18. As a player, I want active import to read current live tower management rather than a stale session owner, so that reassigned or overlapping towers are honored.
19. As a player, I want a filling origin with no servicing tower to wait for ordinary global dumping rules, so that active import does not invent a tower route.
20. As a player, I want source and product choice to follow vanilla combined priority, including queued-job pressure, so that active import does not starve higher-priority logistics.
21. As a player, I want route eligibility evaluated before priority and proximity, so that an unrouted source cannot win merely because it is closer or has a better priority.
22. As a player, I want the closest reachable filling origin selected for a chosen source, so that a nearby reachable demand is served before a farther one.
23. As a player, I want an origin excluded when no candidate truck can reach both its source and target, so that an unreachable origin does not consume an active-import slot.
24. As a player, I want the closest truck to the selected source chosen among trucks that can reach both endpoints, so that blocked trucks do not prevent a farther valid truck from working.
25. As a player, I want one active-import truck per target origin, so that a small fill does not attract a flock of trucks.
26. As a player, I want separate origins to import in parallel, so that a large farm can use as many valid trucks as its reachable origins and source quantity allow.
27. As a player, I want no additional ATD-wide truck cap, so that 100 reachable origins can involve 100 trucks when the world has enough eligible supply.
28. As a player, I want the dispatcher to respect vanilla partial-load behavior, so that active import does not impose a new full-load or minimum-load policy.
29. As a player, I want the requested load to be bounded by source quantity and truck capacity rather than an ATD-estimated exact origin remainder, so that vanilla cargo and continuation behavior remains authoritative.
30. As a player, I want leftover soil after an origin fills to follow ordinary vanilla dumping continuation, so that a partially used load can still find the next valid dumping destination.
31. As a player, I want the active-import chain to target only its selected farmland origin initially, so that ATD does not precompute or reorder unrelated nearby dumping destinations.
32. As a player, I want ordinary dumping jobs and reservations to take precedence over active import, so that ATD does not duplicate, preempt, or second-guess vanilla work.
33. As a player, I want an origin to receive active-import fallback only after one complete farming tick without an ordinary claim, so that event ordering cannot cause duplicate assignments.
34. As a player, I want a cancelled ordinary claim during that grace period to reset the wait, so that active import still yields fairly to newly appearing vanilla work.
35. As a player, I want actual designation assignment to remain governed by vanilla `CanBeAssigned` and `DumpingJob` reservation behavior, so that assignment races resolve through the game's normal authority.
36. As a player, I want a source reservation failure to let the same pass try another valid match, so that one race does not stall every other filling origin.
37. As a player, I want a landslide or other terrain change that makes an origin unfulfilled again to re-enable active import during stabilization, so that filling cannot become stale.
38. As a player, I want no new alert when soil is unavailable, so that active import has the same silent-pending behavior as vanilla's no-soil case.
39. As a player, I want the existing farming status or debug surface to distinguish route-blocked sources, unavailable sources, no eligible trucks, and reachability-blocked truck sets, so that I can diagnose waiting without a new notification system.
40. As a player, I want active import to leave storage settings untouched, so that using a neutral storage for farmland does not change my broader logistics design.
41. As a player, I want active import to leave route assignments untouched, so that the feature cannot create hidden or persistent logistics connections.
42. As a player, I want active-import state to survive loading through vanilla jobs and reservations rather than ATD logistics records, so that saves remain compatible and removable.
43. As a mod maintainer, I want output discovery to use vanilla's product-indexed registry, so that active import does not perform an entity-wide scan on every farming tick.
44. As a mod maintainer, I want the registry adapter to be read-only and version-guarded, so that a vanilla internal change disables only active import instead of corrupting logistics state.
45. As a mod maintainer, I want the adapter failure path to degrade without dispatching, so that unsupported game versions remain safe and silent.
46. As a mod maintainer, I want dispatch to use vanilla's balancing job specification and default truck provider, so that cargo pickup, reservations, navigation, cancellation, and continuation remain vanilla-owned.
47. As a mod maintainer, I want the feature integrated into the existing farming tick, so that there is one authoritative orchestration point rather than a competing global loop.
48. As a mod maintainer, I want runtime slot bookkeeping rebuilt from live vanilla jobs after load, so that stale ATD state cannot block or duplicate imports.
49. As a tester, I want deterministic candidate-selection fixtures, so that priority, route, proximity, reachability, and tie-break behavior can be verified without relying on timing.
50. As a tester, I want regression coverage for neutral storage, soft-released trucks, route edits, partial loads, landslides, and source races, so that the original failure and its edge cases remain protected.

## Implementation Decisions

- Add active soil import to the existing farming-session filling orchestration;
  do not add a global simulation loop or modify the global vanilla balancing
  sweep.
- Keep the behavior automatic. There is no separate ATD toggle, saved
  logistics entity, or player-facing import-mode setting.
- Treat only materialized, currently pending farmland filling origins as
  demand targets. Re-evaluate filling state every farming tick, including
  during stabilization.
- Discover eligible source candidates through a narrow, read-only,
  version-guarded compatibility adapter over vanilla's product-indexed
  registered-output registry. Do not scan all world entities every tick. If
  the expected internal registry shape is unavailable, produce no active
  import dispatch and leave ordinary vanilla behavior untouched.
- Enumerate only currently eligible farmable products for the relevant live
  tower state. During final filling, initialize the tower's dumpable list to
  all farmable products and consult its live list before each new chain.
- Consider any registered output represented by the vanilla product index,
  including neutral storage output. A source with logistics output disabled
  is not eligible.
- Preserve vanilla source quantity semantics: available quantity excludes the
  source's protected import reserve. The export-from slider remains a
  priority preference; below-threshold material may be selected as the
  lowest-priority fallback.
- Preserve vanilla `RegisteredOutputBuffer.CombinedPriorityCached` ordering,
  including queued-job pressure. Route eligibility is a hard filter before
  material, source, and proximity ranking.
- Read the live storage-to-tower route graph without creating, changing, or
  broadening routes. If a servicing tower has one or more assigned output
  storages, only those storages may supply its farmland filling products.
  There is no tower-side allow-non-assigned-input escape.
- When multiple servicing towers manage an origin, accept a source if at
  least one servicing tower permits it. Reject it only when none permits it.
- Use the designation's current live managed-tower collection at dispatch
  time. If it is empty, active import waits and ordinary global dumping rules
  remain the only fallback.
- Apply the greedy matching order: eligible source/product by vanilla
  combined priority, closest eligible target to that source, then closest
  eligible truck to that source. Use stable origin/entity identity only for
  final equal-distance ties; never let collection order decide a match.
- Evaluate target eligibility per truck using vanilla's cached unreachable
  designation result. A target that is unreachable for a candidate truck is
  excluded for that candidate and does not consume the target's one-import
  slot. Do not force new ATD pathfinding or alter vanilla cache invalidation.
- Use vanilla truck availability. Default-provider trucks with only non-true
  parking/navigation jobs may be selected and vanilla may clear those jobs;
  trucks with true jobs or non-default providers are excluded. Preserve
  allowed truck groups, job filters, logistics zones, amphibious restrictions,
  assigned-building restrictions, and source/target reachability.
- Allow at most one active-import truck in flight per farmland origin. Permit
  distinct origins to proceed in parallel with no additional ATD-wide cap.
- Treat any live ordinary dumping job or designation reservation as claiming
  its origin. Require one complete farming tick without such a claim before
  active import dispatch; reset that grace period if an ordinary claim appears
  and later cancels.
- Require vanilla `TerrainDesignation.CanBeAssigned(false)` before dispatch.
  Let the normal `DumpingJob` create the real designation reservation; the ATD
  slot is only an anti-flocking guard.
- Request a load using vanilla terrain semantics: the selected product's
  available source quantity bounded by the selected truck's capacity. Do not
  estimate or impose an ATD-specific exact remaining target amount.
- Compose a vanilla terrain `BalancingJobSpec` with the farmland origin as
  the only initial dumping target and no ATD-precomputed extra targets or
  secondary output buffers.
- Delegate pickup and dumping to `DefaultTruckJobProvider.AssignBalancingJob`.
  After the selected origin is fulfilled, any remaining cargo follows vanilla
  continuation and disposal rules.
- If source reservation fails, discard that candidate and continue the same
  pass with remaining candidates. Do not mutate source settings or create a
  compensating export route.
- Keep route edits effective for future dispatches only. An already reserved
  vanilla chain finishes or cancels under its normal job rules.
- Keep active-import slot bookkeeping runtime-only. Re-derive it after load
  from live vanilla jobs and designation reservations; add no save schema.
- Leave no-source, no-truck, and adapter-unavailable cases pending silently.
  Extend existing farming status/debug reporting with diagnostic distinctions
  but do not add a new player-facing notification.
- Keep all player-facing copy localized if status/debug text is changed.
- Preserve existing farmland intent, farmland placement, accessway, and
  tower-ownership semantics. Active import supplies only the missing logistics
  fallback.

## Testing Decisions

- Test external behavior at the farming filling orchestration seam. Tests
  should assert which source, product, target, and truck are selected, which
  candidates are rejected, and whether a vanilla job dispatch is requested;
  they should not assert private helper call order or implementation-shaped
  collections.
- Add deterministic source/target/truck fixtures for priority ordering,
  source proximity, target proximity, stable ties, and one-origin slot
  behavior.
- Add a neutral-storage regression fixture proving that raw priority 15
  output can be selected by active import while ordinary balancing would not
  pair it.
- Add fixtures for import reserves, disabled logistics output, export-slider
  preference, and source queued-job pressure.
- Add route fixtures for one assigned storage, multiple assigned storages,
  multiple servicing towers with one permitted match, no permitted match, no
  explicit route constraint, and an empty managed-tower set.
- Add live-material-list fixtures proving that all farmable materials are
  initially allowed, a removed material blocks only new chains, and an
  already reserved chain is not cancelled by that removal.
- Add truck-eligibility fixtures for default-provider trucks, soft-released
  non-true jobs, true jobs, non-default providers, truck groups, job filters,
  zones, amphibious requirements, assigned buildings, and source/target
  reachability.
- Add a reachability fixture where the nearest truck or nearest origin is
  blocked but a farther truck/origin is valid.
- Add a scalability fixture with many origins proving one truck per reachable
  origin and no artificial global cap.
- Add partial-load fixtures proving the request is bounded by source quantity
  and truck capacity, does not use an exact target remainder, and leaves
  leftover cargo to vanilla continuation.
- Add reservation-race fixtures proving an ordinary job/reservation blocks an
  origin, grace-period cancellation resets the wait, and a failed source
  reservation allows another match in the same pass.
- Add a stabilization fixture where a landslide or equivalent change makes a
  previously fulfilled origin pending again and active import becomes
  eligible.
- Add silent-failure and diagnostics fixtures for route-blocked source,
  unavailable source, no eligible truck, reachability-blocked trucks, and
  adapter-unavailable behavior.
- Add save/load fixtures proving active-import slot state is reconstructed
  from live vanilla jobs and reservations without ATD-owned persisted
  logistics state.
- Add adapter compatibility tests for the expected vanilla registry shape,
  a changed or missing shape, duplicate output references, and read-only
  behavior.
- Reuse the repository's deterministic fixture-gate style used by access and
  farming diagnostics, and add focused unit seams for candidate selection
  rather than requiring a full interactive world for every ordering case.
- Verify the build and run repository validation appropriate to the affected
  modules after implementation; no decompile gameplay verification is part of
  this spec.

## Out of Scope

- Changing vanilla global terrain balancing or its raw-priority-15 behavior.
- Temporarily enabling export on neutral storages or changing any storage
  import/export slider.
- Creating, editing, or persisting storage-to-tower routes.
- Assigning dumping work to tower trucks; tower trucks remain excavator
  escorts.
- Adding a player-facing toggle, manual “import soil” command, or separate
  logistics panel.
- Adding a new alert or notification when no soil or no eligible truck is
  available.
- Calculating exact target remainder or inventing a new partial-load policy.
- Precomputing nearby extra dumping designations for the active-import job.
- Adding secondary output buffers or unrelated cargo to an active-import load.
- Forcing pathfinding retries, replacing vanilla unreachable caches, or
  implementing a new truck route planner.
- Persisting ATD-owned active-import jobs, slots, or route records in saves.
- Generalizing active import to non-farmable products or non-farmland demands.
- Replacing existing farmland intent, placement, stabilization, accessway, or
  tower-ownership models.

## Further Notes

The feature is deliberately a localized demand-side fallback. It should make
neutral soil usable without changing the player's logistics design, while
remaining recognizable as vanilla truck logistics from the player's point of
view. The adapter is the only compatibility-sensitive seam; all resource
reservation, designation assignment, job cancellation, navigation, and
post-target cargo behavior remain vanilla-owned.

If the adapter cannot safely expose the product-indexed registry on a future
game version, active import should simply stop offering new chains and leave
the existing silent pending behavior intact. That failure must not mutate
storage settings, routes, tower material lists, or saved state.
