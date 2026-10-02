# Default Mine validation

Target: CoI 0.8.7d build 619. Source implemented 2026-10-02.
In-game checks below are pending, not passed.

## Automated checks

From the ATD repository:

```powershell
dotnet build AutoTerrainDesignations.sln -c Debug
dotnet build tools/DefaultMineFixtureRunner/DefaultMineFixtureRunner.csproj -c Debug
./tools/DefaultMineFixtureRunner/bin/Debug/net48/DefaultMineFixtureRunner.exe
```

The runner uses the standard Steam game installation by default; pass a Managed
directory as its only argument for another installation. It validates captured
zone identities, guarded native IL rewriting, and all Harmony hook installation.
It does not instantiate a game world.

2026-10-02: ATD Debug solution build passed with zero warnings/errors. The
fixture executable passed all zone, IL guard, and hook installation checks.
The fixture build emitted NU1900 because NuGet vulnerability metadata could
not be retrieved; this did not prevent its build or execution.

## Initial runtime trial

1. Restart the game and confirm the loaded ATD DLL timestamp using
   `tools/get-mod-log.ps1 -DllOnly`. Check `atd_default_mine_status` for patch readiness.
2. Place a small Mining designation outside all Mine Tower areas. With one
   unassigned excavator, enable the World Settings option. Confirm native
   excavation, unchanged assignment, and selection in a tower inspector.
3. Provide no available truck. Confirm one retained scoop, no further mining,
   and no unexpected idle-time cancellation. Add one available truck and confirm
   unloading and continued use across scoops and designation movement.
4. Fill the truck and confirm native delivery, retained bucket remainder, and
   exactly one replacement after the previous truck starts moving.
5. Remove all eligible mining work; confirm partial-load delivery and release
   of an empty pickup. Block all destinations and confirm the native warning,
   no tower parking, and no additional truck until delivery starts moving.

## Save/load and removal gate

Create separate saves while driving to mining work, holding a scoop, loading a
truck, awaiting an en-route pickup, retaining a partially loaded pair, and
waiting with a blocked full or partial delivery.

- Reload with ATD: verify the world toggle, cargo quantities, one restored
  pairing, delivery zone permissions, released reservations, and continued work.
- Repeat autosaves and exercise a failed save if practical: verify pickup and
  excavation resume, and no duplicate jobs or product accounting appear.
- Include excavators released by ATD's idle-release setting: save-time tower
  restoration must not destroy the restored Default Mine pairing.
- Load copies of these saves with ATD removed: the game must load and remain
  playable; vehicles must accept ordinary work. Cargo loss in buckets or trucks
  is acceptable in this removal trial. Inspect errors for missing types, protos,
  providers, notifications, or orphaned mining reservations.
- Save once without ATD and reinstall it: expect fresh configuration, not
  continuity of the old pairing or toggle.

## Transition regression

| Case | Expected result |
| --- | --- |
| Existing save without state; toggle off | Off, native behavior |
| Enable while a manual order is active | Order finishes before Default Mine acquisition |
| Manual order, assignment with either cancellation flag, toggle off, excavator pause | Owned work stops, bucket discarded, reservation released; loaded truck can deliver |
| Paired truck cancelled, assigned, or paused | Pair breaks; immediate replacement when work or scoop remains; paused cargo survives |
| Tower expanded during preparation, mining, or unloading | Finish active scoop, release reservation, retain scoop; no new work inside coverage |
| Paused tower, tower without excavators, subclassed Mine Tower | All exclude their covered designations |
| Forestry Tower | Does not exclude mining by itself |
| Unreachable, full reservation, fulfilled, non-mining designation | No invalid acquisition; retries when work becomes eligible |
| KPIE miner first, excavator first, four miner reservations | Mutually exclusive work and correct released capacity |
| Unsupported size, forbidden truck group, wrong zone, busy or sorter-assigned truck | Not dispatched |
| Overlapping zones, pickup crossing zones, zone deletion and bit reuse | Original identities govern; remaining zones then Default; new zone gets no inherited permission |
| Native cargo-kind or mixability limit | Partial delivery, retained scoop, replacement after delivery starts |
| Sorting on/off, single and mixed cargo, dumping and surface eligibility | Native destination rules and warnings; warning clears on recovery |
| Excavator or truck refueling during pickup and delivery | Native fuel handling; no duplicate pair or premature replacement |
| Vehicle loss, replacement, or world change | No restored pairing onto another entity; no leaked sessions or reservations |

Record actual outcomes and the loaded DLL timestamp here after the trials.
