# Default Mine runtime

Status: source implemented on 2026-10-02 against CoI 0.8.7d build 619.
Debug build and native integration fixtures pass. In-game behavior and save
removal are not yet verified. The [product design](../planned/default-mine-excavators.md)
records the approved behavior; the [regression plan](../../test/default-mine-excavators.md)
records the remaining checks.

## Implementation

`src/ATD.DefaultMine.cs` owns world-local excavator sessions, one pickup or
blocked delivery per excavator, and released deliveries until their cargo is
gone. Excavators retain `AssignedTo.IsNone`. Selection supplies native scoring
with ready, reachable, reservable Mining designations outside every live
`MineTower` area. Native `MiningJob` execution owns mining, navigation, and
designation reservations.

`src/ATD.DefaultMinePatches.cs` scopes integrations to module-owned vehicles and
jobs. Two guarded IL substitutions adapt the native assignment assertion and
cargo cleanup; unexpected instruction sequences prevent enabling the feature.
Other hooks handle global acquisition, scoop waiting, interruption, truck
availability, captured zone masks, and delivery acquisition. No custom job,
provider, proto, event subscription, or visible assignment is installed on a
vehicle. The ore-sorter provider postfix remains compatible because its trucks
are excluded from Default Mine dispatch.

The first scoop requests an empty available global truck. A native
`VehicleQueueJob<Truck>` follows the moving excavator across later scoops.
Truck candidates use the excavator prototype's native truck-compatibility
predicate as the authoritative eligibility map, together with each truck's
zone, group, availability, and reachability. KPIE Miners are Truck entities;
their actual eligibility therefore follows this native predicate.
Released full or cargo-incompatible trucks use native sorting, dumping,
delivery, and surface factories. A released loaded truck blocks replacement
until it starts driving. The native cannot-deliver warning handles unavailable
destinations. Player interruption releases this blocker immediately; native
excavator refueling retains the pairing and reenqueues its pickup.

`src/DefaultMine/DefaultMineZoneSnapshot.cs` resolves captured zone identities
against current live masks. Deleted identities lose permission; remaining
identities retain permission. Default Zone applies when none remain. Mask-bit
reuse cannot grant permission to a new zone.

## Persistence and removal

The existing `atdTowerSettingsStateJson` stores `worldSettings.defaultMineEnabled`
and a versioned `defaultMine` section containing numeric vehicle and zone IDs
and delivery flags, plus the exact originating tower ID for idle-released
vehicles. Missing or unsupported state leaves the feature off.
Restoration rejects invalid entities, assignments, pickup ownership, and zone
records. JSON contains no live objects or CLR type names.

Before save, suspend dispatch; cancel native mining, unloading, and pickup work;
release designation reservations; reset excavators to ordinary idle state; clear
remembered unloading targets; disable pickup queues. The bucket remains native
cargo. Native delivery and refueling jobs on released trucks can remain because
they require no ATD types or unassigned-mining patches. JSON is staged before
ATD's existing idle-release subsystem temporarily restores tower assignments.
After the save result, that subsystem re-releases vehicles, then Default Mine
restores pickups and wakes eligible excavators through the native provider.
On load, pairing restoration runs after tower settings are loaded. It undoes a
save-time idle-release assignment only when the saved tower identity matches,
the current tower policy allows release, and the tower still has no pending
work. Released vehicles return to the existing idle-release tracking list.

The removal requirement is loadability and continued play without ATD.
Transient cargo loss on removal is acceptable, as clarified by the player.
Ordinary saves with ATD still aim to preserve cargo and pairing. Static native
type reuse and patch-installation checks do not prove either runtime contract;
the save/load and removal trials remain required.

## Wiring and diagnostics

The default-off **Unassigned excavators excavate** toggle appears in World
Settings. UI changes enqueue intent; `UpdateAfterCmdProc` applies it on the
simulation thread, including while paused. Native idle acquisition performs
mining retries; the once-per-second ATD callback reconciles pairs and retries
truck requests. World reset disposes the module.

`atd_default_mine_status` reports enabled state, patch readiness, save suspension,
session and truck counts, and each excavator's job, cargo-kind count, and truck.
English defaults are declared with `Loc.Str`; translation work remains deferred
until pre-release.

## Verification

The fixture runner exercises zone deletion, overlap, bit reuse, Default Zone,
and identity-to-mask changes. It invokes the production IL transformations
against the installed native constructor, execution, and cleanup bodies and
checks that missing or duplicate expected sequences are rejected. It also
installs all production Harmony hooks against the actual game assemblies,
without constructing a game world.

Navigation, cargo transfer, actual serialization, runtime patch composition,
and KPIE coexistence require the in-game regression plan. No in-game pass is
claimed by the fixture runner.

The fixture project reports NU1900 warnings when NuGet vulnerability metadata
is unreachable. Its build and executable checks still pass; vulnerability
metadata retrieval is not verified by those results.
