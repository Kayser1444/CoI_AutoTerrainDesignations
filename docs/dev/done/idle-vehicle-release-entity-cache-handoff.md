# Idle vehicle release entity-cache handoff

**Status:** Implemented; in-game verification pending.

## Review and implementation (2026-10-04)

The review agrees with the diagnosis and targeted mitigation below. Decompiled
`EntitiesManager` and `LystMutableDuringIter` confirm the shared active-enumerator
contract and lifecycle-event ordering. `GameBuilder.initializeGame` calls mod
initialization before `GameRunner.Initialize`, supporting the initialization-only
seed scan. The identity of the competing enumerator remains unknown.

`ATD.IdleVehicleRelease.cs` now owns an ordered list and immutable array snapshots,
with lock-protected mutation/publication and ID plus reference checks on removal.
It subscribes to both entity lifecycle events using `AddNonSaveable`, seeds during
`AutoDepthDesignation.Initialize`, and unsubscribes/clears during
`ResetWorldRuntimeState`, including initialization failure cleanup. All four scans
listed below use the snapshot helper; existing guards and diagnostic sorting
remain in place. The cache adds no saved state.

`dotnet build AutoTerrainDesignations.sln -c Debug` passed with zero warnings and
zero errors. The runtime checks below remain pending; no claim is made that this
fix synchronizes other entity queries or entity fields.

## Background

The downloaded log `26-10-04_01-22-30_6055.log` records this assertion at
`02:27:03.964` on the simulation thread (`~Sim`):

```text
Outer enumerator finished first?
ASSERT: Option<Mafi.Collections.LystMutableDuringIter`1+EnumeratorClass[Mafi.Core.Entities.IEntity]> has value 'Mafi.Collections.LystMutableDuringIter`1+EnumeratorClass[Mafi.Core.Entities.IEntity]' but value 'Mafi.Collections.LystMutableDuringIter`1+EnumeratorClass[Mafi.Core.Entities.IEntity]' was expected.
  at Mafi.Collections.LystMutableDuringIter`1+EnumeratorClass[T].Dispose ()
  at Mafi.Core.Entities.EntitiesManager+<GetAllEntitiesOfType>d__68`1[T].<>m__Finally1 ()
  at Mafi.Core.Entities.EntitiesManager+<GetAllEntitiesOfType>d__68`1[T].MoveNext ()
  at AutoTerrainDesignations.AutoDepthDesignation.TickIdleVehicleRelease ()
  at AutoTerrainDesignations.AutoTerrainDesignationsMod.onSimUpdate ()
  ...
  at Mafi.Core.Simulation.SimulationBackgroundTask.PerformWork ()
```

ATD's [idle vehicle release tick](../../../src/ATD.IdleVehicleRelease.cs)
enumerates `s_entitiesManager.GetAllEntitiesOfType<MineTower>()`. The game's
`EntitiesManager.GetAllEntitiesOfType<T>()` in turn iterates the shared
`m_entitiesLinear`, a `LystMutableDuringIter<IEntity>`.

That collection supports mutations during iteration and nested enumerations
that dispose in last-in, first-out order. Its `m_activeEnumerator` is shared by
the collection instance and is not synchronized. `EnumeratorClass.Dispose()`
asserts that the disposing enumerator is the currently active one. In this log,
ATD's iterator was being disposed while another enumerator was recorded as
active.

The trace establishes that ATD's enumeration hit the assertion; it does not
identify the other enumerator or its owner. A simultaneous query from another
thread is a strong explanation: ATD's callback runs from the simulation
background task, while main-thread (`~Mai`) work appears at the same timestamp.
The log does not prove that the main-thread work was enumerating entities.
Out-of-order nested disposal is another possible cause. A lone, normally
disposed enumeration does not explain this assertion.

## Proposal

Maintain a runtime-only cache of live `MineTower` references and publish
immutable array snapshots. `TickIdleVehicleRelease()` and the other
MineTower-wide scans in `ATD.IdleVehicleRelease.cs` would iterate the latest
snapshot instead of creating iterators over the game's shared entity list.

### Cache lifecycle

1. During world initialization, subscribe to `EntityAdded` and `EntityRemoved`
   and seed the cache from one `GetAllEntitiesOfType<MineTower>()` scan. Keep
   this seed scan in initialization, before normal simulation updates and
   cross-thread world activity begin. Existing towers need seeding because
   they will not emit an add event merely because ATD subscribes.
2. On `EntityAdded`, add a `MineTower` to the private cache. On
   `EntityRemoved`, remove it. Use `EntityId` to locate an entry and check the
   cached reference when removing, so a delayed removal for an old object does
   not remove a replacement with the same ID.
3. Protect cache mutation and snapshot publication with an ATD-owned lock. On
   each lifecycle change, publish a new `MineTower[]`; never mutate a published
   array. Readers acquire the array reference under the same lock, release the
   lock, then iterate the stable array.
4. Unsubscribe both lifecycle handlers and clear the cache during world
   teardown/reset. Do not persist the cache; rebuild it from the loaded world.

An ordered `List<MineTower>` plus a published array is a simple way to retain
the entity-manager order. A dictionary keyed by ID can assist lookups, but
should not be the source of iteration order if order-sensitive side effects
need to stay aligned with the existing scan.

Illustrative shape (not a drop-in patch):

```csharp
private static readonly object s_mineTowerCacheLock = new object();
private static readonly List<MineTower> s_mineTowers = new List<MineTower>();
private static MineTower[] s_mineTowerSnapshot = Array.Empty<MineTower>();

private static MineTower[] GetMineTowerSnapshot()
{
    lock (s_mineTowerCacheLock)
        return s_mineTowerSnapshot;
}
```

The lifecycle handlers update `s_mineTowers` under the lock and replace
`s_mineTowerSnapshot` with `s_mineTowers.ToArray()`. Seed the list from the
initial entity scan and publish the initial array before registering normal
world updates.

### Call sites to migrate

Use the snapshot helper for the four MineTower scans in
[ATD.IdleVehicleRelease.cs](../../../src/ATD.IdleVehicleRelease.cs):

- `TickIdleVehicleRelease()` — the call implicated by the log.
- `RestoreIdleReleasedVehiclesForSave()`.
- `ReReleaseIdleVehiclesAfterSave()`.
- `FormatAssignedVehiclesDump()`.

Keep the existing destroyed, construction-state, and settings checks. A reader
may already hold the previous array when a tower is removed, so the existing
`IsDestroyed` guard remains useful. Do not wrap the current live
`GetAllEntitiesOfType()` result in `ToList()`; materializing it still starts an
enumeration over `m_entitiesLinear` and can collide in the same way.

## Scope and limits

This cache removes the MineTower enumeration from these ATD call sites. It
does not synchronize the game's entity collection, protect other mods, or
guarantee that other `GetAllEntitiesOfType<T>()` calls elsewhere in ATD cannot
participate in similar assertions. It also protects only enumeration of the
cached array; reading or mutating entity fields from a non-owning thread would
need separate analysis.

Entity add/remove events are raised after the game updates its entity
collections. The cache lock protects ATD's list and snapshot only. It cannot
make the initial seed scan safe if that scan runs concurrently with arbitrary
entity-manager enumerations, which is why initialization timing is part of the
design constraint.

## Verification handoff

After implementation:

1. Build `AutoTerrainDesignations.sln` in Debug.
2. Exercise idle release with multiple MineTowers, including release,
   restoration when work resumes, tower removal/deconstruction, and world
   load/unload.
3. Exercise before-save restoration and after-save re-release paths.
4. Inspect a full runtime log for `Outer enumerator finished first?` and
   compare the ATD DLL timestamp to the loaded-DLL row.

There is no repository test seam that reproduces the game callback and
cross-thread entity-manager access. A successful build or a run without the
assertion is useful verification but is not a deterministic regression test
for the original race. The cache should therefore be described as a targeted
mitigation, not a global fix for concurrent entity enumeration.
