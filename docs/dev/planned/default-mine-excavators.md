# Default Mine for unassigned excavators

Status: implementation authorized and source implemented; in-game validation pending.
Current implementation: [runtime and persistence](../in-progress/default-mine-excavators.md).

Implementation review: [findings, integration plan, and validation gates](default-mine-excavators-implementation.md)
(2026-10-02, CoI 0.8.7d build 619). Runtime lifecycle and save removability
remain validation gates before considering the feature verified.

## Summary

Let excavators with no entity assignment automatically mine eligible terrain
designations that are outside every Mine Tower's area. This internal work scope
is the **Default Mine**. To the player, its excavators continue to appear
unassigned, just as they do in vanilla. See the [context glossary](../../../CONTEXT.md#excavator-operations).

The main logistics change is that a Default Mine excavator requests a global
truck when it starts filling a bucket. The truck remains associated with that
excavator across later scoops and excavator movement until the truck is full or
the player cancels it. Existing vanilla truck destination rules continue to
decide where its cargo can go.

## Goals

- Give unassigned excavators useful work on Mining designations not covered by
  any Mine Tower.
- Preserve the player's vanilla mental model: Default Mine is internal state,
  not a new building or visible assignment.
- Keep Default Mine's truck throughput lower than a Mine Tower's. A Mine Tower
  can have multiple trucks serving each excavator, while Default Mine uses one
  truck per excavator and accepts some waiting as the tradeoff for less
  critical excavation.
- Reuse vanilla designation selection, reservation, priority scoring, truck
  dispatch, cargo delivery, and refueling behavior wherever possible.
- Let manual orders and explicit entity assignments take control immediately.

## Player control

Expose a global player-facing toggle:

- **Location:** Automatic Terrain Designation mod settings, World Settings tab.
- **Label:** `Unassigned excavators excavate`
- **Tooltip:** `Unassigned excavators excavate terrain designations outside
  Mine Tower areas.`
- **Default:** Off.
- Save the toggle value per world.
- Turning the toggle on immediately scans for eligible unassigned excavators.
- An active manual vehicle order keeps control when the toggle is turned on;
  Default Mine can take over after the order finishes if the excavator is
  still unassigned.
- Do not add an excavator warning while it waits for an available truck with
  a scoop; this is expected logistics waiting. Use the native truck warning
  when cargo cannot be delivered.

## Terms and boundaries

- **Default Mine** is an internal work scope derived from having no entity
  assignment. It is not a Mine Tower, a player-visible zone, or a new
  assignment option.
- **Unassigned excavator** means an excavator with no entity assignment. It may
  have an active Default Mine job or a Logistics Zone assignment; “free” does
  not mean idle.
- An excavator assigned to a Logistics Zone remains eligible. That zone
  affects truck logistics at the excavator's pickup position, not which Mining
  designations it may reserve.
- Only Mining designations are candidates. The Default Mine does not create
  terrain designations.
- Any Mine Tower coverage excludes a designation from Default Mine selection,
  including coverage managed by a modded Mine Tower, regardless of whether
  that tower currently has excavators or is paused.
- A modded Mine Tower based on the vanilla `MineTower` type counts as a Mine
  Tower. Independent modded area managers that do not derive from that type do
  not block Default Mine work for now.

## Agreed behavior

### Excavator and designation selection

- Consider excavators with no entity assignment at all. An assignment by
  another mod's entity also disqualifies the excavator.
- Find ready Mining designations outside all Mine Tower areas, using vanilla
  candidate eligibility and selection rules: reservation capacity, readiness,
  reachability, and vanilla material-priority scoring. Material priority is a
  soft score, based on exposed material; it is not a hard filter.
- Respect reservations by other excavators. Default Mine and KPIE miners are
  mutually exclusive on a designation. KPIE's four-miner capacity therefore
  does not grant four additional Default Mine excavators; vanilla excavators
  retain their native capacity.
- Mine Tower inspectors continue to show these excavators among the unassigned
  pool available for assignment. Internally being managed by Default Mine does
  not change their player-facing unassigned state.
- Pausing follows vanilla behavior. A paused excavator cancels its current job
  and releases its designation reservation; it clears bucket cargo, remains
  unassigned, and becomes eligible again after it resumes. There is no
  separate Default Mine pause filter. Pausing also breaks its truck
  association: cancel an empty truck request back to general logistics, let a
  loaded truck finish delivery, and request no replacement while the excavator
  is paused.
- If no eligible designation is available, keep checking at the normal
  vanilla idle-job cadence so newly placed or newly eligible designations can
  restart work automatically.
- Use vanilla refueling behavior.

### Orders, assignments, and Mine Tower coverage

- A manual vehicle order or new entity assignment interrupts Default Mine work
  immediately. Cancel the current scoop/job, discard bucket contents as vanilla
  does for a manual order, and release the designation.
- Turning the global toggle off has the same immediate effect on all
  Default-Mine excavators: discard any held scoop, release designations, cancel
  empty truck requests, and let trucks already carrying cargo finish delivery.
- Explicit assignment to a Mine Tower or another entity breaks the excavator's
  truck association. Cancel an empty truck request/en-route pickup so that
  truck returns to general logistics. A truck already carrying cargo completes
  its delivery.
- Passive expansion of Mine Tower coverage is different from explicit
  assignment: allow an active scoop to finish, then release the designation so
  the tower can claim remaining work.
- When an excavator has no eligible designation, any associated underfilled
  truck delivers its partial load and returns to general logistics.

### Truck requests and delivery

- Request any available global truck when the excavator starts filling its
  bucket; do not create a dedicated Default Mine truck pool.
- Keep at most one truck associated with an excavator. Request another when the
  current truck has left and the excavator starts filling its next bucket; do
  not pre-assign or queue additional trucks as a Mine Tower can.
- If no truck is available, the excavator may complete one scoop and hold it
  while waiting. It does not abandon the scoop merely because logistics are
  temporarily unavailable.
- Trucks are the only unloading destination in this design. Do not transfer
  directly to conveyors, loaders, or stockpiles.
- Determine the request's Logistics Zone eligibility from the excavator's
  position when the truck is requested. If that position is outside all
  player Logistics Zones, use vanilla's Default Zone mask. Keep this original
  zone mask for the truck's pickup and delivery eligibility for the lifetime
  of the association, even if the excavator moves into another zone. Use the
  excavator's then-current position when requesting its next truck. The truck
  remains associated with the excavator as it moves between designations or
  zones.
- The truck remains associated until full or manually cancelled. Cargo may
  include products from later scoops that could not be predicted when the
  initial truck request was made.
- If native cargo compatibility prevents an underfilled truck from accepting
  a later scoop, deliver the partial load and request a replacement after the
  delivery blocker clears. Retain the scoop.
- Capture zone identities rather than reusable mask bits. If a captured zone
  is deleted, continue under the remaining captured zones, falling back to
  Default Zone only when none remain. A newly created zone cannot inherit the
  deleted zone's permission.
- If the player cancels or reassigns the bound truck while the excavator still
  has work or a scoop to unload, request a replacement truck immediately.
- Pausing the bound truck breaks its association with the excavator. Return an
  empty truck to general logistics; a loaded truck keeps its cargo for normal
  delivery after it is unpaused. Request a replacement immediately if the
  excavator still has work.
- Preserve the excavator-to-truck association across save/load so the same
  pairing resumes without dispatching a duplicate truck.
- Choose cargo destinations using the same vanilla rules as a truck loaded by
  an excavator assigned to a Mine Tower with no export routes. This includes
  vanilla mixed-material, ore-sorting, and dumping eligibility; Default Mine
  should not invent a separate destination policy.
- Default Mine has no virtual Mine Tower or tower-local dump area. Trucks use
  eligible global destinations under their current Logistics Zone and vanilla
  product, sorting, dumping, and surface-placement rules.
- If a truck carrying cargo cannot find an eligible delivery destination, it
  waits at its current location, shows the existing native truck warning, and
  blocks further truck assignments to that excavator until it resumes moving.
  This also applies to an underfilled truck dispatched because no eligible
  excavation work remains.
  Do not add a duplicate Default Mine warning. Vehicles do not block each
  other, so the waiting truck stays in place without obstructing traffic.

## Vanilla behavior checked

- `TerrainMiningManager.TryFindClosestReadyToMine` searches the tower's managed
  designations and checks readiness, reservations, and reachability. It does
  not constrain mining candidates by the excavator's Logistics Zone. Default
  Mine should reuse the applicable designation checks while supplying its own
  global candidate scope and excluding Mine Tower coverage.
- Storage truck requests and Mine Tower truck queues use different vanilla
  dispatch paths. Default Mine needs the global available-truck path while
  retaining the existing zone and cargo eligibility rules.
- Vanilla builds logistics-zone masks from zones containing an entity's
  position. If the resulting mask contains no player zone, it resolves to the
  Default Zone mask.
- With ore sorting enabled, the Mine Tower truck provider can try the source
  tower's and its assigned towers' dumping areas, a mixed-load sorting-plant
  job, and then a global mixed-load dump constrained by the truck's zone mask.
  If none works, it retains the cargo and activates the native mixed-cargo
  warning.
- The source tower/assigned-tower dump step is intentionally omitted for
  Default Mine because it has no owning tower or assigned routes.
- The game's `MineTowersManager` recognizes runtime entities that are
  `MineTower` instances, including subclasses. The more general
  `IAreaManagingTower` interface is also used by Forestry Towers, so it does
  not identify mining coverage by itself.
- In the ordinary no-export fallback, the provider checks global input
  buffers, zone-eligible dumping destinations, then surface-placement jobs.
- Vanilla pause handling cancels vehicle jobs; mining-job cleanup releases
  designation reservations. No special paused-state reservation behavior is
  needed.
- Vanilla exposes a native truck cannot-deliver warning. The design relies on
  that warning for Default Mine cargo rather than adding a second excavator
  warning.

## Open decisions

No open decisions remain from the current grilling session.

On 2026-10-02 the player approved keeping excavators truly unassigned, reusing
native mining and truck jobs through scoped patches, and suspending dependent
jobs during saving. The partial-load compatibility exception and deleted-zone
policy above were also approved.

Removal must be technically possible: a save created with ATD must load without
ATD and allow continued play. Transient effects, including cargo loss in trucks
or excavator buckets, are acceptable on removal. Cargo and pairing continuity
remain ordinary save/load goals while ATD is installed; they are not removal
requirements.

## Source references

- `Mafi/Mafi.Core.Terrain.Designation/TerrainMiningManager.cs:99` — tower
  mining candidate enumeration and readiness checks.
- `Mafi/Mafi.Core.Vehicles/RegisteredInputBuffer.cs:272` and
  `Mafi/Mafi.Core.Vehicles.Trucks.JobProviders/MineTowerTruckJobProvider.cs:260`
  — storage-zone truck requests and Mine Tower truck selection.
- `Mafi/Mafi.Core.Vehicles/LogisticsZonesManager.cs:236` and
  `Mafi/Mafi.Core.Vehicles/LogisticsZone.cs:152` — position-derived zone masks
  and fallback to the Default Zone.
- `Mafi/Mafi.Core.Vehicles.Jobs/MiningJob.cs:389` and
  `Mafi/Mafi.Core.Entities.Dynamic/Vehicle.cs:233,440` — mining reservation
  cleanup and pause/job cancellation behavior.
- `Mafi/Mafi.Core.Vehicles.Excavators/Excavator.cs:733` — clearing excavator
  bucket cargo during mining-job cleanup.
- `Mafi/Mafi.Core.Vehicles.Trucks.JobProviders/MineTowerTruckJobProvider.cs:307`
  — Mine Tower sorting, dumping, and delivery fallback.
- `Mafi/Mafi.Core.Buildings.Mine/MineTower.cs:290` — Mine Tower
  cannot-deliver warning.
- `Mafi/Mafi.Core.Buildings.Mine/MineTowersManager.cs:59` and
  `Mafi/Mafi.Core.Buildings.Towers/IAreaManagingTower.cs:10` — Mine Tower type
  recognition and the generic area-managing interface.
