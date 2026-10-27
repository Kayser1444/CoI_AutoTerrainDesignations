# Controls layout regression

Reads `ControlsTab.buildOwnerBlock` from the installed game's actual IL, applies
the production AutoHelpers transpiler from the built ATD DLL, and executes the
native threshold arithmetic and column-creation branch as a small dynamic
method. It needs neither Unity UI objects nor a running game.

```powershell
dotnet run --project tools/ControlsLayoutContracts -- 'C:\Program Files (x86)\Steam\steamapps\common\Captain of Industry\Captain of Industry_Data\Managed' ./AutoTerrainDesignations.dll
```

The single-shortcut case must keep its first section in the first column.
Native decisions for 2..50 shortcuts must remain unchanged. The fixture also
rejects unexpected native branch shapes. Replacing the DLL argument with
`--native` runs without the fix and must fail on 0.8.8 build 622, reproducing the
empty left column shown in the original screenshot.

This checks the actual layout decision, not rendered pixels or shortcut input.
Full native Controls method installation cannot be checked under the external
.NET hosts here: loading its Unity UI type fails on `FontAsset` before Harmony
can run. Final patch installation and visual confirmation require the game's
Mono runtime after a restart.
