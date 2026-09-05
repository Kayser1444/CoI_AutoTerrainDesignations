# Dynamic accessway excavator groups

**Status:** Planned; not implemented.

## Purpose

Replace the static `T1`–`T3` accessway choices with dynamically discovered
excavator capabilities. The player chooses the search/pathability profile that
an accessway should support, rather than choosing a fuel variant of a vehicle.

The list must include every registered `ExcavatorProto`, regardless of research
or vehicle unlock state. This lets players plan wide accessways before the
corresponding excavator is available.

## Group identity

ATD groups registered excavator prototypes when both of these are equal:

1. The complete `VehiclePathFindingParams` value.
2. The derived mining-approach radius currently used by access planning.

Fuel, capacity, build cost, visuals, and other fields not consumed by access
planning do not split a group. Consequently, vanilla diesel and hydrogen
variants collapse into one entry when their search-relevant behavior is the
same.

Each group has one canonical member for presentation. Select the member with
the lowest game `UIOrder`, then localized name, then prototype ID. Display the
canonical member's localized name and icon. If two distinct groups have the
same localized name, append a short prototype-ID suffix only to disambiguate
the colliding labels. If a prototype has no usable icon path, use the standard
vehicle icon.

Sort groups by path width, then height clearance, then localized name, then
prototype ID.

For the vanilla prototype set, the seven excavators become four entries:

- `Small excavator` (T1)
- `Excavator` (T2 diesel/hydrogen)
- `Mega excavator` (T3 diesel/hydrogen)
- `Amphibious excavator` (diesel/hydrogen)

The amphibious group remains distinct because amphibious behavior is part of
the search-relevant pathability parameters.

## User-facing choices

Both the global default and each tower inspector use the same dynamically
generated list and retain these static choices:

- `OFF`
- `AUTO`
- dynamic excavator groups
- `Turning 2`, when eligible
- `Legacy 3`, `Legacy 4`, and `Legacy 5`

`Turning 2` is shown when at least one excavator prototype is registered and no
registered group resolves to V2/two-lane pathfinding. If it is already selected,
keep it visible even after a later mod-set change introduces a V2-capable group.
Its tooltip is: “Generate two-lane turn-capable accessways.”

`Turning 2` uses the pathability resolved by `AUTO` but forces two-lane,
turn-capable routing. If no live, released, or queued excavator is available,
resolve its profile from the widest/highest-clearance registered group. It is
not shown when no excavator prototypes are loaded.

Explicit dynamic groups and `Turning 2` are planning commitments: they may
generate accessways using registered prototype data even when no matching
vehicle is currently built or assigned. `AUTO` remains inactive when no live,
released, or queued excavator can resolve it.

## `AUTO` presentation

In a tower inspector, `AUTO` keeps the `AUTO` label and shows a small icon for
the currently resolved group. Resolution follows the existing tower-assigned,
released, queued, and fleet fallback rules. The tooltip may identify the
resolved group's localized name.

The global default `AUTO` has no hint icon because it has no tower context. A
tower with no runtime excavator resolution also keeps plain `AUTO` with no
hint icon. Hint updates use the existing panel refresh/activation and
tower-switch paths, not a per-tick UI refresh.

## Persistence and migration

Current state uses a new group-selection representation; the old
`AccessVehicleClearanceMode.T1`–`T3` values remain readable only for migration
and compatibility. They are not emitted as new dropdown choices.

Persist a selected dynamic group with:

- its canonical prototype ID, for readable/debuggable saves; and
- a deterministic fingerprint of the grouped search properties, for recovery
  if the canonical member disappears.

When loading:

1. Resolve the canonical ID first. If it exists, its current properties win,
   even if they changed since the save.
2. If the ID is absent, resolve an equivalent group by fingerprint and adopt
   that group's canonical ID in memory; write the normalized identity on the
   next ordinary save.
3. If neither resolves, silently clear the stale identity and use `AUTO`;
   persist that cleanup on the next ordinary save. Do not warn the player.

Existing saved `T1` and `T2` values migrate to the exact prototype selected by
the old deterministic tier-token lookup, or silently to `AUTO` if none exists.
Existing `T3` values migrate to that exact prototype when it resolves to V2;
otherwise they migrate to `Turning 2` to preserve the old two-lane behavior.
Legacy width modes remain unchanged.

Keep the existing numeric compatibility API:

- `GetRampWidth`: `0` = `OFF`; `2` = any V2 group or `Turning 2`; `3`–`5` =
  legacy widths; `1` = `AUTO` or a V1 dynamic group.
- `SetRampWidth(2)` selects `Turning 2`; it does not guess a prototype.

Do not add a public exact-prototype selection API as part of this feature.
Keep the existing numeric `vehicleClearance` config field and add optional
identity/fingerprint fields for dynamic groups. Reserve a new numeric value
for `Turning 2`; old ATD versions may safely interpret it as `AUTO`.

## Rebuild and verification

Cache the grouped list from the current `ProtosDb` after initialization. Rebuild
it on world-generation changes and refresh dropdowns when they open or when an
inspector switches towers.

Implementation must add coverage for:

- grouping equivalent fuel variants;
- distinguishing amphibious and other pathability differences;
- deterministic canonical selection and ordering;
- conditional `Turning 2` visibility;
- `AUTO` hint resolution and no-resolution behavior;
- ID-first/fingerprint-second persistence recovery;
- old `T1`–`T3` migration; and
- numeric ramp-width API compatibility.

