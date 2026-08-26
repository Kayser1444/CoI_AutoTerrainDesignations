# Active dumping modes for tower-managed dumping designations

Status: ready-for-agent
Issue: #24

## Problem Statement

ATD's active soil import currently exists only as a fallback for final
farmland filling. That leaves two gaps. First, farmland automation can create
dumping work during the preparation phase and can create dumping accessways as
part of the same workflow; those designations can stall when a neutral storage
is the only practical source. Second, the existing active-import machinery is
the right foundation for a broader player-controlled behavior, but it has no
way to opt into active dumping for ordinary tower-managed dumping designations
or to disable the fallback on a particular tower.

The distinction is not primarily a material distinction. A mode named “Soil
only” would misleadingly suggest that dirt may be actively imported to any
dumping designation. The important boundary is which workflow owns the target
designation. Farmland automation must be able to source whatever products its
live tower dump rules permit, while unrelated mining and interactive
accessway work must remain outside the farmland-only scope.

The behavior also needs to remain compatible with vanilla logistics. Tower
trucks escort excavators and must not be repurposed for dumping. Ordinary
global trucks, including trucks temporarily soft-released by ATD, must use the
same source, route, reachability, reservation, partial-load, and continuation
rules already established for active soil import.

## Solution

Generalize active soil import into a runtime-only active-dumping dispatcher
with a per-tower **Active dumping** mode:

- **Always** actively imports any eligible terrain material to any eligible
  dumping designation managed by that tower, including ordinary dumping and
  dumping/accessway work created by mining or interactive workflows.
- **Farmland automation only** actively imports to dumping designations that
  are currently owned by the tower's live farmland automation workflow. This
  includes primary farmland fill, preparation-stage dumping, final filling,
  and accessways created by that workflow. Product eligibility is delegated to
  the workflow's live tower dump rules; ATD does not add a separate hard-coded
  product filter.
- **None** disables ATD active dispatch for that tower. Farmland automation's
  existing temporary dump-rule changes remain unchanged.

The dispatcher runs once per running game-second from ATD's existing
simulation-update seam. It collects all eligible targets globally, evaluates
each target against its live servicing towers, applies the existing source and
truck selection policy, and delegates job creation to vanilla's balancing job
provider. The dispatcher is paused with the simulation.

The setting has three layers. The current world's tower default is exposed in
Mod Settings → Tower defaults and persisted in the existing config-backed world
state. That view's **Save to config** action writes the selected world default
to `ATDsettings.json` as the global default for future worlds. Each tower
stores its own concrete mode and copied/blueprinted towers retain it. Changing
the world default does not rewrite existing tower modes. Saves created before
this feature, or tower records without the new field, migrate to
**Farmland automation only**.

Each mode option has a localized tooltip. Use the same localized option copy in
the per-tower dropdown and the Tower defaults control:

- **Always** — “Actively request eligible terrain materials for every dumping
  designation managed by this tower, including ordinary dumping, mining
  accessways, and interactive accessways. Products follow the tower's current
  dumpable-product list.”
- **Farmland automation only** — “Actively request materials only for dumping
  work owned by this tower's farmland automation, including preparation, final
  filling, and accessways created by that workflow. Products follow the live
  workflow/tower dump rules.”
- **None** — “Do not create ATD active-dumping requests for this tower.
  Farmland automation still uses its normal temporary dump-rule behavior.”

The tooltip text is registered through ATD localization keys and translated in
every supported language; it must not be embedded as an untranslated UI string.

## User Stories

1. As a player, I want to choose an active-dumping mode per mine tower, so that
   I can control whether ATD augments dumping for that tower.
2. As a player, I want **Always** to actively import material to ordinary
   dumping designations in a tower's managed area, so that neutral storages can
   supply general terrain work without temporary export changes.
3. As a player, I want **Always** to include mining-generated accessway dumps,
   so that access routes can be supplied from otherwise neutral storage.
4. As a player, I want **Always** to include interactive pathfinding accessway
   dumps, so that active dumping is genuinely general for that tower.
5. As a player, I want **Farmland automation only** to include the complete
   farmland workflow, so that preparation and final filling share one reliable
   material-sourcing behavior.
6. As a player, I want farmland preparation-stage dumping designations to be
   eligible, so that a temporary preparation fill cannot stall merely because
   its source storage is neutral.
7. As a player, I want final farmland filling designations to be eligible, so
   that the existing active soil-import benefit remains available.
8. As a player, I want farmland-workflow accessways to be eligible, so that a
   small dumping ramp required to reach the fill area cannot deadlock the
   workflow.
9. As a player, I want farmland automation to decide which products are
   permitted, so that the active importer follows the workflow's live tower
   dump rules rather than applying a second ATD product filter.
10. As a player, I want a material removed from the tower's live dumpable list
    to stop new active chains for that material, so that the tower remains the
    product-control surface.
11. As a player, I want an already reserved chain to finish or cancel under
    vanilla rules after a product or mode change, so that ATD does not interrupt
    vanilla-owned work.
12. As a player, I want **None** to disable only ATD active dispatch, so that
    farmland automation's existing temporary dump-rule changes still work.
13. As a player, I want designations outside all tower management areas to
    remain governed by global dumping rules, so that ATD does not invent route
    authority outside a tower.
14. As a player, I want a farmland-owned accessway outside all tower areas to
    follow the same global-only boundary, so that out-of-area fallback does not
    silently create an ATD route exception.
15. As a player, I want an **Always** tower to work even when farmland
    automation is disabled, so that the mode is useful for general dumping.
16. As a player, I want an **Always** tower to use every terrain product
    accepted by its live dumpable-product list, so that I can intentionally opt
    into general active dumping.
17. As a player, I want no ATD mode to change the tower's dumpable-product list
    by itself, so that product selection remains explicit and inspectable.
18. As a player, I want neutral storage contents to be usable as a low-priority
    active source, so that I do not have to change storage import/export
    sliders.
19. As a player, I want a source with logistics output disabled to be excluded,
    so that an explicit source shutdown remains authoritative.
20. As a player, I want protected import quantities to remain unavailable, so
    that active dumping cannot consume material reserved for inbound logistics.
21. As a player, I want export-from thresholds to retain vanilla priority
    semantics, so that below-threshold output remains only a low-priority
    fallback rather than becoming an unexpected hard ban.
22. As a player, I want explicit storage-to-tower routes to constrain the
    source storages for active dumping, so that assigned tower logistics remain
    authoritative.
23. As a player, I want no implicit allow-non-assigned-input escape for a tower,
    so that unrelated storages cannot bypass an explicit tower route.
24. As a player, I want a source accepted when one of several servicing towers
    permits its route, so that overlapping tower management does not reject a
    valid source unnecessarily.
25. As a player, I want source route eligibility evaluated against the specific
    tower candidate, so that a source assigned to one tower is not accidentally
    treated as assigned to another.
26. As a player, I want the live managed-tower collection to decide target
    servicing, so that reassigned and overlapping towers are honored.
27. As a player, I want a designation with no live servicing tower to receive no
    ATD active dispatch, so that global dumping rules remain the only fallback.
28. As a player, I want a shared farmland/non-farmland designation to remain
    eligible for farmland-only dispatch while farmland ownership is live, so
    that another workflow does not suppress a pending farmland obligation.
29. As a player, I want farmland-only dispatch to stop when farmland ownership
    is revoked or the designation is replaced, so that stale workflow ownership
    cannot attract trucks.
30. As a player, I want overlapping towers to combine by eligible servicing
    pair, so that an **Always** tower may serve any designation it manages while
    a farmland-only tower serves only its workflow-owned targets.
31. As a player, I want a designation to receive at most one active-import truck
    at a time, so that a small dump does not attract a flock of trucks.
32. As a player, I want distinct eligible origins to proceed in parallel, so
    that a large set of dumping work can use the available fleet.
33. As a player, I want no additional ATD-wide truck cap, so that 100 eligible
    reachable origins may involve 100 trucks when source quantity allows it.
34. As a player, I want ordinary dumping claims and reservations to win over
    active fallback, so that ATD does not duplicate or preempt vanilla work.
35. As a player, I want one complete no-claim grace tick before fallback
    dispatch, so that simulation-event ordering cannot cause duplicate claims.
36. As a player, I want a cancelled ordinary claim to reset that grace period,
    so that active fallback yields fairly to newly appearing vanilla work.
37. As a player, I want the closest reachable target selected for a chosen
    source, so that source priority and target proximity remain vanilla-shaped.
38. As a player, I want the closest eligible truck to the chosen source, so that
    a blocked nearest truck does not prevent a farther valid truck from working.
39. As a player, I want source and target reachability checked for the same truck,
    so that a truck cannot be selected when it can reach only one endpoint.
40. As a player, I want vanilla truck groups, job filters, logistics zones,
    amphibious restrictions, and assigned-building restrictions preserved, so
    that active dumping does not broaden ordinary truck eligibility.
41. As a player using ATD soft release, I want a default-provider truck with
    only parking or navigation work to remain vanilla-available, so that soft
    release improves fleet utilization.
42. As a player, I want tower-assigned trucks excluded from active dumping, so
    that tower trucks continue escorting excavators.
43. As a player, I want true jobs and non-default truck providers excluded, so
    that active dumping does not disrupt specialized vehicle workflows.
44. As a player, I want partial-load behavior delegated to vanilla, so that
    active dumping does not introduce a competing full-load policy.
45. As a player, I want the requested load bounded by source availability and
    truck capacity, so that ATD does not guess an exact target remainder.
46. As a player, I want leftover cargo after the selected target to follow
    vanilla continuation and disposal, so that partial fulfillment remains
    useful instead of becoming stranded cargo.
47. As a player, I want mode changes to prevent new dispatches immediately while
    allowing existing vanilla chains to finish or cancel normally.
48. As a player, I want a landslide or other terrain change that makes a
    farmland designation pending again to re-enable active import, so that
    stabilization cannot leave stale unfulfilled work.
49. As a player, I want no new notification when active dumping cannot find a
    source or truck, so that waiting retains vanilla's silent behavior.
50. As a player, I want existing farming status/debug output to distinguish
    route blocks, unavailable sources, no eligible trucks, and reachability
    failures, so that beta testing can diagnose waiting behavior.
51. As a player, I want the per-tower mode in the general Mining designations
    panel, so that a setting affecting ordinary dumping is not hidden in the
    Farmland panel.
52. As a player, I want the world-level tower default in Mod Settings → Tower
    defaults, so that each world can choose its normal tower behavior.
53. As a player, I want the Tower defaults view's **Save to config** action to
    write the selected world default to `ATDsettings.json`, so that future
    worlds start with my preferred behavior.
54. As a player, I want a console command for the current world default, so that
    beta testing and scripted setup can change it without opening the UI.
55. As a player, I want changing the world default not to rewrite existing
    towers, so that a world-wide preference change does not silently alter
    active logistics.
56. As a player, I want new or previously unconfigured towers to use the world
    default, so that defaults reduce repetitive setup.
57. As a player, I want copied and blueprinted towers to retain their concrete
    active-dumping mode, so that tower configuration remains portable.
58. As a player loading an older save, I want missing active-dumping state to
    migrate to **Farmland automation only**, so that existing behavior is
    preserved without unexpectedly enabling general active dumping.
59. As a player, I want all mode labels and behavior tooltips localized, so
    that the setting is understandable in every supported language.
60. As a player, I want the same behavior tooltip shown for an option in both
    the per-tower dropdown and the Tower defaults control, so that the setting
    is explained consistently wherever it is changed.
61. As a player, I want active dumping to run once per running game-second, so
    that response time is sufficient without an expensive per-simulation-step
    scan.
62. As a player, I want active dumping to pause with the simulation, so that
    trucks are not queued into work that cannot progress.
63. As a player, I want active dumping to leave storage settings, routes, and
    tower assignments unchanged, so that the feature remains a demand-side
    fallback rather than a logistics reconfiguration.
64. As a player, I want active-import runtime state rebuilt from live vanilla
    jobs and designations after loading, so that stale slots cannot block or
    duplicate work.
65. As a player, I want the feature safe to remove from a save, so that ATD-owned
    runtime bookkeeping cannot make the save dependent on the mod.
66. As a mod maintainer, I want one global dispatch orchestration seam, so that
    source discovery and target matching are not duplicated per tower or per
    farming session.
67. As a mod maintainer, I want the existing product-indexed vanilla output
    registry adapter reused, so that candidate discovery does not scan every
    world entity on every pass.
68. As a mod maintainer, I want the adapter read-only and version-guarded, so
    that a vanilla internal change disables only active dispatch.
69. As a tester, I want deterministic candidate-selection fixtures, so that
    priority, route, proximity, reachability, and stable tie behavior can be
    checked without timing-sensitive worlds.

## Implementation Decisions

- Replace the farmland-only active-import orchestration with one global
  active-dumping pass on ATD's existing simulation update. Run it once per
  running game-second and do not run it while the simulation is paused.
- Keep the highest behavioral seam at target collection and candidate
  dispatch. Reuse the existing read-only output-registry compatibility adapter,
  source route checks, reachability checks, truck eligibility filters, grace
  period, one-origin slot, load sizing, and vanilla balancing-job assignment.
  Do not patch vanilla's global balancing sweep or create a parallel pickup,
  dumping, or route planner.
- Represent the tower mode as three concrete values: `Always`, `Farmland
  automation only`, and `None`. The default is `Farmland automation only`.
  Missing serialized values migrate to that default.
- Evaluate a target through eligible `(target designation, servicing tower)`
  pairs. `Always` permits any eligible dumping designation managed by that
  tower. `Farmland automation only` permits only dumping designations with
  live farmland-workflow ownership associated with the selected tower.
  `None` contributes no candidate.
- If several towers manage one designation, evaluate each eligible servicing
  tower separately. Use existing source/product/target/truck ordering and a
  stable servicing-tower identity only as the final tie-break.
- Require a live vanilla managed-tower relationship. A designation outside all
  towers is not an active-dumping target, even if a farmland workflow created
  an out-of-area accessway for it.
- Define farmland-workflow ownership through a general predicate over live
  farmland-owned dumping roles. Include primary fill, preparation-stage fill,
  final fill, workflow-created accessways, and future auxiliary farmland
  dumping roles. Ownership is additive when another workflow also uses the
  designation; revocation or replacement removes it from the farmland-only
  scope.
- Do not use a hard-coded product filter for farmland-only mode. Product
  eligibility is delegated to the selected tower's live dumpable-product list,
  including temporary changes made by the farmland workflow. Always mode uses
  the same live tower list but has no farmland-workflow target restriction.
- Preserve the existing product-indexed output discovery adapter. In Always
  mode it may expose any eligible terrain product represented by the registry;
  in farmland-only mode the workflow's live target product rules decide which
  source products can match.
- Preserve source semantics: disabled logistics output excludes a source,
  protected import quantity remains a hard limit, export thresholds retain
  vanilla priority meaning, neutral output may be selected as fallback, and
  assigned storage-to-tower routes remain hard source filters.
- Preserve target and truck semantics: vanilla designation reservations and
  `CanBeAssigned(false)` remain authoritative; ordinary claims receive the
  existing no-claim grace period; source/target reachability, truck groups,
  job filters, zones, amphibious requirements, assigned-building restrictions,
  default-provider availability, and soft-released non-true jobs remain in
  force. Tower-assigned trucks remain excluded.
- Keep the global one-origin anti-flocking slot and permit unlimited parallel
  origins subject only to vanilla source quantity and eligible trucks.
- Compose the same vanilla terrain balancing job with one selected primary
  designation and no ATD-precomputed secondary targets or output buffers.
  Vanilla owns source reservation, partial-load behavior, cargo continuation,
  designation reservation, cancellation, navigation, and disposal.
- A source-reservation race rejects only that candidate; the same global pass
  continues matching other sources, trucks, targets, and servicing towers.
- Mode changes, route changes, and product-list changes affect future
  dispatches only. Existing reserved vanilla chains finish or cancel normally.
- Keep active-import slots, no-claim grace state, and dispatcher diagnostics
  runtime-only. Rebuild live slot state after load from vanilla jobs and
  designation reservations. Do not add ATD-owned logistics records to saves.
- Add the mode to the existing tower configuration persistence and
  copy/blueprint/clone configuration. Seed new or previously unconfigured
  towers from the current world default; changing the world default does not
  rewrite existing concrete tower modes. During migration, snapshot the
  legacy default into existing towers so later world-default changes cannot
  silently rewrite them.
- Add a world-level default to the existing config-backed world state and
  expose it in the Tower defaults settings view. Initialize a new world's
  value from the global `ATDsettings.json` default. Expose a console command
  for the current world value.
- Make the Tower defaults view's **Save to config** action write the current
  world default to `ATDsettings.json`. Keep `config.json` as the registration
  mechanism for save-backed state, not as the editable user-defaults file.
- Put the per-tower dropdown in the general Mining designations panel. Keep
  the three labels and behavior tooltips localized across supported
  translations. Reuse the same tooltip registrations in the Tower defaults
  control; do not embed untranslated mode descriptions in either UI.
- Keep **None** scoped to active dispatch. Do not change the existing farmland
  workflow's temporary dump-rule switching, stabilization, or cleanup logic.
- Extend the existing farming status/debug surface with global active-dumping
  counts and mode/scope diagnostics without adding a new player-facing alert.
- Treat the existing active-soil-import ADR as the source of truth for vanilla
  source/job delegation, but supersede its final-filling-only and no-toggle
  scope with this mode-based generalization.

## Testing Decisions

- Test external behavior at the global active-dumping orchestration seam.
  Assert eligible target scope, selected source/product/tower/truck, rejected
  candidates, and dispatch requests; do not assert private collection order or
  helper-call structure.
- Add deterministic mode fixtures covering all three modes, default migration,
  world-default seeding, concrete tower overrides, and mode changes while a
  chain is in flight.
- Add target-scope fixtures for ordinary dumping, farmland preparation fill,
  final farmland fill, farmland workflow accessways, mining accessways,
  interactive accessways, shared workflow ownership, replaced ownership, and
  out-of-area targets with no managed tower.
- Add overlapping-tower fixtures covering Always + farmland-only, Always +
  None, farmland-only + None, multiple route-permitted towers, route-blocked
  towers, and stable servicing-tower tie-breaks.
- Add product-rule fixtures proving farmland-only delegates product eligibility
  to the live tower/workflow dump list and does not apply a separate
  hard-coded farmable filter; Always accepts every eligible terrain product in
  the target tower list.
- Reuse the existing neutral-storage, protected-reserve, disabled-output,
  export-threshold, source-priority, queued-pressure, and route-assignment
  fixtures from active soil import, expanding them to ordinary dumping targets.
- Add truck fixtures for free default-provider trucks, ATD soft-released
  trucks with non-true jobs, true jobs, tower-assigned trucks, non-default
  providers, groups, filters, zones, amphibious restrictions, assigned
  buildings, and source/target reachability.
- Add ordering fixtures for source combined priority, target proximity, truck
  proximity, servicing-tower identity tie-breaks, blocked-nearest candidates,
  one-origin slots, and unlimited parallel origins.
- Add vanilla delegation fixtures for source quantity/capacity load sizing,
  partial loads, leftover-cargo continuation, ordinary claim grace, claim
  cancellation, designation reservation races, and source-reservation failure.
- Add workflow lifecycle fixtures for preparation-to-filling transitions,
  stabilization reactivation, landslide re-entry, accessway creation/removal,
  ownership sharing, ownership revocation, and replacement designations.
- Add cadence fixtures proving one pass per running game-second, no dispatch
  while paused, no duplicate pass per tower, and no entity-wide source scan.
- Add settings fixtures for `ATDsettings.json` load/save/migration, Tower
  defaults view updates, **Save to config**, world-state persistence, console
  updates, copy/blueprint/clone transfer, old-save migration, and concrete
  tower modes surviving world-default changes.
- Add localization/UI fixtures proving each mode option has a translated
  behavior tooltip in both the per-tower dropdown and Tower defaults control,
  with the copy explaining scope, product authority, and the None-mode
  farmland exception.
- Add save/removability fixtures proving active slots and diagnostics are
  runtime-only and that no ATD-owned logistics state is required after the mod
  is removed.
- Reuse the repository's deterministic fixture-gate style used by active soil
  import, farming transitions, accessway ownership, and settings persistence.
- Run the Debug build, diff validation, focused fixtures, and package
  verification appropriate to the implementation. Do not treat decompile
  gameplay verification as part of this spec.

## Out of Scope

- Changing vanilla's global terrain-balancing algorithm or its low-priority
  output behavior.
- Temporarily enabling export on storages, changing storage sliders, creating
  routes, or changing tower assignments.
- Importing to a dumping designation outside all tower management areas.
- Using tower trucks for dumping; tower trucks remain excavator escorts.
- Creating a new pickup/dumping job implementation, truck route planner, or
  pathfinding system.
- Replacing vanilla source reservation, designation reservation, partial-load,
  continuation, cancellation, or unreachable-cache behavior.
- Adding a product-specific ATD filter to farmland-only mode.
- Treating farmland-only as a generic soil-to-any-designation mode.
- Making farmland automation's temporary dump-rule switching conditional on
  the active-dumping mode.
- Adding a new player-facing notification for unavailable material, trucks,
  routes, or reachability.
- Persisting active-import jobs, slots, routes, source reservations, or
  diagnostics as ATD-owned save data.
- Extending farmland-only scope to mining or interactive accessways that have
  no live farmland-workflow ownership.
- Adding a separate per-product active-dumping policy UI.
- Rewriting existing tower modes when the world default changes.
- Adding an exception that grants route authority to farmland-owned targets
  outside tower areas.

## Further Notes

“Farmland automation only” is deliberately a workflow-scope name. It includes
all dumping work that the farmland automation workflow owns, including
accessway dumping needed to reach that work, but it does not itself prescribe
which products may be used. The live workflow/tower dump rules remain the
product authority.

The existing active soil-import implementation already contains the most
compatibility-sensitive part: a read-only, version-guarded view of vanilla's
product-indexed output registry plus vanilla balancing-job delegation. The new
work should generalize that seam rather than duplicate it or move it into the
global vanilla balancing loop.

The accepted active-soil-import ADR remains applicable for source discovery,
route preservation, truck eligibility, partial-load behavior, and silent
failure. Its “final filling only” and “no toggle” statements are superseded by
this spec's workflow-scoped target policy and three-mode tower setting.
