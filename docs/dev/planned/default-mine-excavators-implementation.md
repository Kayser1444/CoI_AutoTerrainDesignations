# Default Mine implementation preparation

Reviewed 2026-10-02 against the current ATD working tree, KPIE reservation code,
and local CoI 0.8.7d build 619 source. This is preparation, not implemented
behavior. The [product design](default-mine-excavators.md) remains authoritative.
Implementation subsequently proceeded with player approval; see the
[current implementation and remaining validation](../in-progress/default-mine-excavators.md).

## Assessment

The selection rules and player-facing scope are implementable using existing
game facilities. This is more than a new idle-job selector: mining execution,
unloading, truck ownership, delivery policy, and saving require coordinated
changes. Start with a feasibility implementation that proves unassigned mining
and removable saves before building the complete settings and logistics flow.

No feature source was changed during this review. Existing ATD, KPIE, and AH
working changes were present and must be preserved during implementation.

## Confirmed contracts and gaps

Game references below are relative to
`%APPDATA%/Captain of Industry/Mafi/Mafi.Core/Mafi/Core/`.
The product note's dotted namespace paths identify an older parallel source
layout. Use the nested assembly output for future contract checks.

| Concern | Evidence | Implementation consequence |
| --- | --- | --- |
| Global candidates | `Terrain/Designation/TerrainMiningManager.cs`, `MiningDesignations`, `TryFindClosestReadyToMine` | Enumerate mining candidates globally, then apply native readiness, capacity, and per-vehicle unreachable checks. The tower selector itself requires a tower. |
| Native scoring | `Terrain/Designation/TerrainDesignationsManager.cs`, `TryFindBestReadyToFulfill` | Supply the filtered set and excavator product preference; omit the assigned-tower positional preference. Do not rewrite the scorer or make product preference a hard filter. |
| Tower coverage | `Buildings/Mine/MineTowersManager.cs`, `Towers`; `Buildings/Mine/MineTower.cs`, `isDesignationInsideArea` | Runtime `MineTower` subclasses count. Vanilla area membership checks the designation's center tile. Check every live tower's area independently of enabled status or assigned excavator count; test designation edges. |
| Reservations | `Terrain/Designation/TerrainDesignation.cs`, `CanBeAssigned`, `TryAssignTo`, `RemoveAssignment` | Preserve native capacity and penalty bookkeeping, with `tryIgnoreReservations: false`. Reserve on the simulation thread and handle a failed reservation normally. |
| KPIE coexistence | `KaysersPreIndustrialEra/src/KPIE.MinerRuntime.cs`, `MinerDesignationReservations` | KPIE synchronizes native assignment counts and rejects non-miner jobs. Native excavator reservations should already exclude KPIE miners in both directions; verify with KPIE loaded. No compile-time KPIE dependency is needed. |
| Mining creation and execution | `Vehicles/Jobs/MiningJob.cs`, `Factory.TryCreateAndEnqueueJob`, constructor, `DoJobInternal`, `isControlledByAssignedTower` | The factory rejects an excavator with no Mine Tower. Even `Factory.EnqueueJob` reaches an assignment assertion. Execution also asserts assignment and rejects unowned designations. A factory patch alone is insufficient. |
| Waiting with a scoop | `Vehicles/Excavators/Excavator.cs`, `handleWaitingForTruck` | With no compatible waiting truck, an unassigned excavator returns to idle. Preserve the scoop and active work state through a scoped integration; merely enabling its queue is insufficient. |
| Native queue | `Vehicles/Trucks/TruckQueue.cs`; `Vehicles/Jobs/VehicleQueueJob.cs`, `StartNavigation` | Queue jobs can follow a moving vehicle owner. This is a useful reuse point for the pickup leg. Queue expiry, job cleanup, cancellation, and refueling can break the association and need explicit handling. |
| Dispatch eligibility | `Vehicles/Trucks/Truck.cs`, `IsAvailableToBalanceCargo`; `Vehicles/VehiclesManager.cs`, `GetFreeVehicle` | `GetFreeVehicle` selects assignable vehicles and merely penalizes paused/loaded ones. It is not an available global logistics-truck selector. Require empty, enabled, genuinely available trucks, supported by the excavator, and honor zones, reachability, and applicable truck job filters. |
| Zone lifetime | `Entities/Dynamic/Vehicle.cs`, `ZoneMask`; `Vehicles/LogisticsZonesManager.cs`, `PlayerZonesFast` | An unassigned truck's native mask comes from its assigned Logistics Zone, not the pickup position. Capture a separate position-derived request mask, including overlapping zones and Default Zone fallback. Native delivery helpers that read `truck.ZoneMask` require a scoped integration with this captured mask. |
| Delivery policy | `Vehicles/Trucks/JobProviders/MineTowerTruckJobProvider.cs`, `tryGetExcavatorDeliveryJob`; `TruckJobProviderBase.cs`, `TryGetRidOfCargo` | The generic cargo fallback differs from Mine Tower delivery: it calls sorting with `isMineTruck: false`, has different fallback ordering, and can use unexpected-cargo fallback. Letting the default provider dispose of every released load does not prove the requested policy. |
| Sorting without a tower | `Buildings/OreSorting/OreSortingPlantsManager.cs`, `TryGetMixedDeliveryJobFor` | The tower argument is optional. Use `Option.None` and the mine-truck sorting classification, retaining native plant eligibility. A virtual Mine Tower is unnecessary. |
| Serialized dependencies | `Vehicles/Trucks/Truck.cs`, `SerializeData`; `Vehicles/Jobs/MiningJob.cs`, `SerializeData` | Both truck job providers and vehicle jobs are serialized. A mod-owned provider, job, queue-tip job, or event target must not remain reachable in the serialized game graph. |
| Existing save hooks | `src/ATD.Mod.cs`, `beforeSave`, `onSaveDone`; `src/ATD.TowerSettingsConfigPersistence.cs` | ATD already has a vanilla config JSON store and transient cleanup hooks. Store world settings and harmless identity records there; prove runtime state sanitation before save. |
| Idle release | `src/ATD.IdleVehicleRelease.cs`, `RestoreIdleReleasedVehiclesForSave`, `ReReleaseIdleVehiclesAfterSave` | ATD's released excavators are genuinely unassigned during play and are eligible under this design. Before-save tower reassignment can cancel their jobs and pairing unless Default Mine is suspended first. |

## Review findings requiring action

1. **Save/load pairing and mod removal are the first feasibility gate.**
   A JSON pair record is harmless when ATD is absent, but does not make a
   custom serialized job/provider harmless. Even a native `MiningJob` left
   on an unassigned excavator depends on ATD patches to run correctly. Do not
   leave that job in the saved graph. Prove a suspend/resume path that retains
   bucket cargo, truck cargo, pairing, and accounting without custom serialized
   types or orphaned native mining work.

2. **Separate pairing from queue membership and delivery state.**
   A full truck may leave the pickup queue but still block replacement if it
   cannot start delivery. Removing a queue entry must not erase this blocker.
   Keep identity and delivery state until the contract permits another truck.
   Recheck destination availability at vanilla job cadence rather than polling
   every simulation tick.

3. **A coverage handoff must retain the finished scoop.**
   Native mining cleanup clears excavator cargo. On passive coverage expansion,
   stop starting new scoops, finish the active scoop, release the designation,
   and allow unloading. Calling ordinary cancellation at that point would
   discard the finished scoop. Manual orders, explicit assignments, toggle-off,
   and excavator pause still use the design's discard behavior.

4. **Captured zones require deliberate delivery integration.**
   Keep the request mask with the pairing and with a released truck's delivery
   record. Do not change the player's truck zone assignment merely to make
   helpers read that mask. Zone deletion/recreation must be reconciled using
   zone identities; do not let a reused bit silently authorize a different
   zone. Exact behavior after zone deletion remains to specify and validate.

5. **There is a native cargo compatibility limit.**
   `Vehicles/VehicleCargo.cs`, `CanAdd`, limits cargo to eight product kinds
   and requires mixability. `TruckQueue.TryGetFirstTruckFor` releases trucks
   unable to accept any held product even when underfilled. The product design
   needs an explicit exception or waiting behavior for this case. Recommended:
   deliver the incompatible partial load, retain the scoop, and request a
   replacement subject to the existing cannot-deliver blocker. This recommendation
   is provisional pending the player's answer.

## Proposed implementation shape

Keep one world-owned Default Mine module. Its interface should expose enabling,
idle-job acquisition, player interruption, save suspension/resumption, and world
disposal. Keep reservations, pairs, pending delivery blockers, and captured zones
inside that module; avoid distributing independent ownership dictionaries among
Harmony patches.

Use ATD's existing partial class as the wiring layer, with focused implementation
files such as `ATD.DefaultMine.cs`, `ATD.DefaultMinePatches.cs`, and
`ATD.DefaultMinePersistence.cs`. These names are proposed, not existing files.

The preferred feasibility route is to reuse native `MiningJob` execution with
narrow patches that recognize only module-owned excavators/jobs. Inspect the
constructor and execution assignment assertions, ownership checks, unloading
wait behavior, and cleanup together. Preserve genuine `AssignedTo.IsNone`; do
not fake assignments or remove assertions for all excavators. Validate every
required patch target and disable this feature safely if a target is unavailable.

If that route requires fragile broad rewrites, reassess an original transient
mining job using native navigation, terrain extraction, reservations, animations,
and refueling primitives. Such a job must be completely removed from the graph
before save. This alternative is not permission to copy a decompiled state
machine into the repository. Choose the route after the feasibility gate, not by
assuming that `EnqueueJob` supports unassigned excavators.

Keep native `VehicleQueueJob<Truck>` for pickup when possible. Initially evaluate
a hook at the default provider's idle-job acquisition point for available global
trucks; retain vanilla refueling precedence and prevent a bound truck from
reentering global balancing. The existing ore-sorter export hook also patches
`DefaultTruckJobProvider.TryGetJobFor`; define precedence and verify patch
composition instead of adding an independent claim path that can double-dispatch.

Delivery needs a scoped adapter around the native factories: mine-truck sorting,
global mixed dumping, product input buffers, product dumping, and surface
placement in the product design's order. Native factories should own destination
reservations and navigation. Keep ATD's delivery identity until all cargo has
been handled; a factory accepting a job is not proof that the truck has started
moving. Use native cannot-deliver notifications, without tower parking fallback.

All job/reservation mutations run on the simulation thread. The UI toggle must
schedule enable/disable reconciliation through the existing input scheduler.
Enabling should wake eligible idle excavators promptly while leaving manual,
scrap, replacement, recovery, and refueling work under their current owner.

## State and transition contract

Track an excavator's selected designation separately from its truck association.
Track released deliveries separately so a cannot-deliver truck can block that
excavator after leaving the pickup queue.

| Event | Excavation/reservation | Truck handling |
| --- | --- | --- |
| Toggle enabled, idle and eligible | Acquire through vanilla idle-job path | Request at bucket-filling start, not at initial designation selection |
| No available truck | Finish at most one scoop and hold it | Retry at normal logistics cadence; never queue a second truck |
| Truck becomes full | Retain scoop remainder if any | Release for delivery; wait to replace if delivery is blocked |
| No eligible mining work remains | Finish unloading held cargo; release reservation | Send partial load for delivery and return empty truck to logistics |
| Passive tower coverage expansion | Finish current scoop, reserve no further work there, release reservation | Preserve unloading/pairing if there is more Default Mine work; otherwise deliver partial load |
| Manual excavator order, new entity assignment, toggle-off, excavator pause | Immediately cancel owned work, discard bucket, release reservation | Cancel empty pickup; loaded truck keeps normal delivery obligation |
| Bound truck manually cancelled/reassigned/paused | Retain eligible excavation and bucket | Break association and request replacement immediately when appropriate; paused loaded truck retains cargo for later delivery |
| Truck temporarily refuels | Do not treat vanilla refueling as a player cancellation | Preserve association if possible; verify how queue cancellation and refueling cooperate |
| Vehicle destroyed/despawned/replaced | Release owned reservation and clear invalid identities | Cancel empty pickup or preserve surviving cargo delivery as applicable; never restore pairing onto a new entity |
| World terminated/replaced | Dispose all runtime bookkeeping and non-saveable subscriptions | No references or pending callbacks may leak into the next world |

Pause/reassignment exceptions take precedence over a previous truck's delivery
blocker: the design explicitly calls for replacement on those player actions.

## Persistence plan and first validation gate

Use `worldSettings` in `atdTowerSettingsStateJson` for the world toggle, defaulting
to off when absent. Add a versioned Default Mine section for identity records.
Initial record contents: excavator ID, paired truck ID, designation origin where
needed, pickup-zone identities/mask, and pending delivery phase/blocker. Do not
serialize CLR type names or live objects into JSON.

Before save:

1. Suspend selection and dispatch and capture pair/delivery identities.
2. Sanitize all feature-dependent mining work and any transient provider/adapter
   registrations. Preserve native cargo and product accounting; ordinary job
   cancellation is insufficient because it clears the bucket.
3. Stage the JSON state after capture, then permit ATD's existing save-time
   idle-release tower reassignment. Specify and test ordering explicitly.

After save, including a reported failure, restore the in-memory session once
ATD has re-released temporary tower assignments. On load, reconcile identities
against live entities and native queue jobs before requesting anything. Reject
missing vehicles, new player assignments, stale designations, unsupported schema,
duplicate ownership, and invalid zone records safely. Presence of a saved truck
queue job and a pair record must produce one association, never two dispatches.

Do not assume that save removal requires preserving Default Mine work. With ATD
absent, the save must deserialize and return to playable vanilla behavior with
valid reservations and vehicles. The player clarified that transient cargo
loss on removal is acceptable. With ATD present, cargo and pairing must resume.
After loading and saving without ATD, reinstalling ATD starts from normal fresh
configuration; shared instructions do not promise continuity across mod absence.

The exact sanitation mechanics remain unproven. First gate: one unassigned
excavator, one truck, one designation; save with a held scoop and with an en-route
truck, reload with ATD, then reload the same saves without ATD. Inspect the saved
job/provider graph and compare material quantities. Do not advance to complete
logistics until both pairing continuity and removal work.

## Implementation sequence

1. Prove the native mining/excavator patch route and save gate above. Record the
   chosen approach and patch guards before expanding the feature.
2. Implement global candidate selection and atomic reservations, tower coverage
   checks, idle retry, cancellation ownership, and KPIE coexistence.
3. Implement one-truck pickup, captured zone handling, replacement, queue lifetime,
   and pending-delivery blockers; compose with ore-sorter/global logistics hooks.
4. Implement native mine-cargo delivery and refueling behavior with no owning
   tower. Validate mixed loads, partial loads, blocked destinations, and warning
   clearing before treating the logistics layer as complete.
5. Add the World Settings toggle, localized label/tooltip, persistence defaults,
   diagnostics, player documentation, and private changelog entry. Re-read shared
   translation instructions before changing localized text. Do not bump versions
   or edit the public changelog for this development task.
6. Run the regression matrix and ATD Debug build, then move implementation docs
   out of `planned` only when behavior has been demonstrated.

## Regression matrix

Use focused transition fixtures where they cover meaningful ownership decisions,
plus in-game checks for navigation, serialization, and Harmony composition.

- Default off, existing saves lacking the key, immediate enable/disable, world
  replacement, and enable during an active manual order.
- Mine Tower inspector still offers active Default Mine excavators for assignment.
- Unreachable, fulfilled, ocean-limited, reserved, and non-mining designations;
  priority as a soft score; new work appearing after idle.
- Paused/no-excavator towers, modded Mine Tower subclasses, Forestry Towers, area
  overlap, and designation-center behavior at area edges.
- KPIE miner first and excavator first, including four KPIE reservations and
  cancellation releasing capacity.
- No trucks, unsupported truck size, zone-ineligible trucks, ordinary busy trucks,
  and another ATD workflow attempting to claim the same idle truck.
- One truck across scoops, excavator movement, zone crossings, truck capacity,
  cargo-kind compatibility, and bucket remainder after a truck becomes full.
- Sorting enabled/disabled, one material/mixed material, dumping/surface rules,
  no destination, recovery after a destination opens, and partial-load delivery.
- Manual order, explicit assignment with either job-cancellation flag, toggle-off,
  excavator/truck pause, truck cancellation/reassignment, vehicle loss, and fuel
  depletion at each pickup/delivery phase.
- Passive coverage expansion while driving, preparing, scooping, waiting with a
  scoop, and unloading; no lost scoop and no additional work inside tower areas.
- Save/load while searching, moving, holding a scoop, pickup en route, paired
  partial load, full/released load, blocked delivery, and paused loaded truck.
- Save failure and repeated autosaves; removal of ATD; idle-released tower
  excavators during save; no duplicate requests, orphan reservations, stale
  zone authorization, product loss/duplication, or mod-only serialized types.

## Verification performed during preparation

- Verified ILSpy CLI update to `11.1.0.9782`; ran `tools/decompile-coi.ps1`.
  All four assembly outputs passed the script's timestamp freshness check.
  The asset catalog was regenerated: CoI `0.8.7d`, build `619`, 4050 assets.
  The reference repository received the script's `Update asset catalog` commit.
  Maintained manifests/changelogs already named that game version and were not
  changed by the script. Freshness here is the script's timestamp check, not a
  byte-for-byte decompilation comparison.
- The decompile skill's static scanner produced 223 compatible lookups, 141
  unresolved lookups, and one reported incompatibility. The latter names KPIE's
  reflection lookup of `Proto.<IsObsolete>k__BackingField`; the nested source
  shows an auto-property, so the source scanner does not establish an actual
  missing backing field. No source fix was applied, and runtime reflection
  compatibility is not claimed. The unresolved lookups likewise prevent a
  blanket compatibility conclusion.
- Scanner report:
  `%TEMP%/coi-decompile-reports/default-mine-preparation-20261002.md`.
- Debug builds passed for ATD, AFD, DesignerToolkit, KPIE, and AH, each with
  zero warnings/errors. These validate the existing working trees; they do not
  validate the proposed feature or runtime save behavior. Normal project build
  targets may refresh deployed local DLLs.
- No Default Mine in-game experiment or save-removal test was performed.
