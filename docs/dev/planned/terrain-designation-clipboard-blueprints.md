# Terrain Designation Clipboard and Blueprint Research

This note captures the implementation research for player-facing copy, cut, paste, flip, rotate, translate up/down, and blueprint-book storage of terrain designations. The motivating player use case is reusing complex terrain-designation patterns: repeatable mine cuts, decorative mountain carving, farm-preparation pads, access ramps, and other hand-authored terrain structures.

## Current ATD and vanilla-facing primitives

ATD already has the low-level pieces needed to serialize and recreate terrain designations without inventing a new terrain format.

- A terrain designation is addressed by its 4×4-grid origin tile. ATD consistently snaps arbitrary tiles with `TerrainDesignation.GetOrigin(...)` before selecting or placing designations.
- `DesignationData` can represent both flat and shaped designations. Flat cells can be created from an origin plus one target height, while sloped/corner/custom cells can be created from the origin plus four absolute corner heights in NW, NE, SE, SW order.
- `TerrainDesignationsManager.AddOrReplaceDesignation(proto, data)` is the placement primitive used by ATD's scanner, corner mode, and farming/access helpers.
- `TerrainDesignationsManager.RemoveDesignation(origin)` is already used for temporary hiding/restoration and cleanup, so cut/move/delete can be implemented with the same world mutation primitive.
- Preview support exists through `TerrainDesignationsRenderer` in corner mode; clipboard paste should reuse that renderer path for ghost designations before committing.

The important consequence is that a copied designation can be stored as `{ relativeOrigin, protoId, fourCornerHeightsRelativeToAnchor }` and later rehydrated into a normal vanilla `DesignationData` with the chosen target `TerrainDesignationProto`.

## Coordinate and height model

A clipboard item should separate horizontal shape from vertical placement.

Recommended clipboard representation:

```text
TerrainDesignationClipboard
  anchorOrigin: Tile2i
  cells: TerrainDesignationClipboardCell[]

TerrainDesignationClipboardCell
  relativeOrigin: Tile2i        // source origin - anchor origin, in tile coordinates
  protoId: string               // MiningDesignator, DumpingDesignator, LevelDesignator, etc.
  nwDelta: int                  // source NW height - anchorHeight
  neDelta: int                  // source NE height - anchorHeight
  seDelta: int                  // source SE height - anchorHeight
  swDelta: int                  // source SW height - anchorHeight
```

`anchorHeight` should default to the minimum or NW-corner height at the anchor cell. Storing deltas instead of absolute heights lets the same pattern be pasted at a new elevation by adding the paste target height plus the current designation-tool height bias.

For paste, compute:

```text
worldOrigin = pasteAnchor + transformed(relativeOrigin)
worldCornerHeight = pasteBaseHeight + transformedCornerDelta
```

The paste base height should be selectable with a small set of modes and adjustable while the paste preview is active:

1. **Surface-relative default**: use the surface height at the paste anchor plus the active terrain tool's height bias. This matches current corner-mode behavior and feels consistent with vanilla terrain-designation controls.
2. **Absolute elevation**: preserve exact copied elevations. Useful for blueprints intended for a known mine/farm floor level.
3. **Snap to neighbor**: if the pasted pattern touches existing designations, offset the whole pattern so the best shared edge matches neighboring corner heights. This is the most ergonomic option for extending a repeated slope/ramp pattern.

While previewing, the player must be able to translate the whole pasted pattern vertically with the vanilla terrain up/down controls (`Q`/`E` by default). This should add or subtract the same integer height delta to every transformed corner height, not change the horizontal footprint or source DTO. The preview should show the current vertical offset so players can paste the same copied terrain shape at a floor, ramp, or bench elevation that differs from the initial paste-base calculation.

## Copy and cut selection

There are two practical selection sources.

### Rectangular designation-grid selection

The first implementation should provide a rectangular selection tool because it matches current drag workflows and is straightforward to preview. It should:

1. require an active terrain-designation tool, or expose its own toolbar mode under the terrain tool;
2. snap drag start/end to 4×4 designation origins;
3. iterate origins inside the rectangle;
4. call `GetDesignationAt(origin)` and include only occupied designations;
5. store mixed proto IDs so a copied pattern may contain mining, dumping, and leveling cells.

Cut is then copy followed by `RemoveDesignation(origin)` for each copied source origin, preferably after successful clipboard creation so failed copies do not delete work.

### Area-tool or blueprint-style selection

A later implementation could reuse the game's area editing or blueprint selection UI if accessible. This would provide non-rectangular selections and better consistency with players' existing copy/paste muscle memory. It is not required for the first working version because the terrain designation grid is sparse: empty cells in a rectangular selection do not need to be stored or pasted.

## Transforming terrain designations

Transforms must affect both the cell origin and the corner-height order.

Corner index convention used by ATD:

```text
0 = NW / origin
1 = NE / plus X
2 = SE / plus X+Y
3 = SW / plus Y
```

The existing `TileTransform` used for farm placement shows that the game already models rotation plus reflection for entity footprints. For terrain designations, ATD can implement a smaller transform helper over `Tile2i` offsets and four-corner arrays.

Required transforms:

| Transform | Relative origin transform | Corner permutation |
| --- | --- | --- |
| Rotate 90° clockwise | `(x, y) -> (height - y - 4, x)` after normalizing to selection bounds | NW←SW, NE←NW, SE←NE, SW←SE |
| Rotate 180° | `(x, y) -> (width - x - 4, height - y - 4)` | NW←SE, NE←SW, SE←NW, SW←NE |
| Rotate 270° clockwise | `(x, y) -> (y, width - x - 4)` | NW←NE, NE←SE, SE←SW, SW←NW |
| Flip horizontal | `(x, y) -> (width - x - 4, y)` | NW←NE, NE←NW, SE←SW, SW←SE |
| Flip vertical | `(x, y) -> (x, height - y - 4)` | NW←SW, NE←SE, SE←NE, SW←NW |

The formulas assume `relativeOrigin` values are measured in tile units and every designation cell is 4×4. Normalize transformed output so the minimum transformed x/y becomes zero before applying it to the paste anchor. That prevents rotation around a source-world origin from introducing negative offsets that surprise players.

## Paste controls, behavior, and conflict policy

Paste should be conservative by default but allow explicit replacement.

Recommended first-pass controls and policy:

- Preview all transformed cells at the target location.
- Rotate clockwise with the vanilla rotate action (`R` by default).
- Flip with the vanilla flip action (`F` by default).
- Translate the preview up/down with the vanilla terrain height actions (`Q`/`E` by default).
- Prefer `ShortcutsManager` or the same native input action sources used by the vanilla terrain designation controller, so remapped rotate/flip/up/down keys work instead of hard-coding `R`, `F`, `Q`, and `E`.
- Color cells by outcome: placeable, would replace existing designation, out of bounds/invalid, or unsupported proto.
- Plain paste places only into empty cells and skips conflicts.
- Shift-paste or an explicit toolbar toggle allows `AddOrReplaceDesignation` to replace existing designations.
- Right-click cancels paste preview.
- Cut-paste should not remove source cells until the destination commit succeeds unless the action is explicitly a normal cut-to-clipboard operation.

The manager's existing `AddOrReplaceDesignation` semantics make overwrite implementation simple, but the UI should make replacements obvious because replacing a mix of mining/dumping/leveling cells can affect active tower jobs immediately.

## Blueprint book feasibility

Blueprint-book support is possible, but it is higher risk than a standalone clipboard because terrain designations are not static entities.

Likely implementation options:

1. **ATD-owned terrain-blueprint library**: store serialized designation patterns in ATD config/state or export/import JSON. This is the safest first milestone because it does not patch vanilla blueprint internals and can be designed to be save-removable.
2. **ATD terrain blueprint book embedded in the vanilla blueprint UI**: patch the normal blueprint book so terrain-designation blueprints appear alongside vanilla blueprints, but store the actual terrain payload in a separate ATD-owned file such as `TerrainDesignationBlueprints.json` in the mod folder. This matches the player's mental model without pretending vanilla blueprints can carry terrain designations.
3. **Vanilla blueprint attachment / hybrid**: store ATD terrain patterns separately and reference them from patched vanilla blueprint-book UI metadata when possible. Mixed entity+terrain blueprints are not a requirement; avoid coupling terrain payloads to vanilla entity blueprints unless a future use case explicitly needs it.

The local repository does not contain Captain of Industry assemblies, so exact blueprint-book class names and patch points still need confirmation against the installed game binaries. Before implementing vanilla blueprint integration, decompile/search the game's managed assemblies for blueprint classes, commands, serializers, and placement previews.


## Blueprint Designers Toolkit clues

Kayser's Blueprint Designers Toolkit (`https://github.com/Kayser1444/CoI_DesignerToolkit`) is public and does contain useful blueprint-book clues. One important difference from BDT is that terrain-designation blueprints cannot be consumer-free: they require ATD (or equivalent code) to interpret terrain-designation payloads and place them into the world. BDT is still useful for learning how to patch the blueprint UI, preserve metadata, duplicate records, and keep mod-owned state outside vanilla save-critical objects.

Concrete BDT clues found so far:

- **Blueprint update flow:** `BDT.BlueprintUpdate.cs` patches `Mafi.Unity.Ui.Blueprints.BlueprintsWindow`, injects an Update button into `m_placementPanel`, reads the selected `IBlueprint`, preserves `Name`, `Desc`, `OverlapDeltaX`, `OverlapDeltaY`, and folder index, then calls `BlueprintCreationController.ActivateForSelection(...)`. The selection callback receives `ImmutableArray items`, `ImmutableArray surfaces`, and `ImmutableArray decals`, deletes the old item with `BlueprintsLibrary.DeleteItem`, creates a replacement with `BlueprintsLibrary.AddBlueprint(folder, items, surfaces, decals)`, restores metadata with `RenameItem`, `SetDescription`, `SetOverlapDeltas`, and reorders with `TryReorderItem`.
- **Recycle-bin flow:** `BDT.BlueprintRecycleBin.cs` patches `BlueprintsLibrary.DeleteItem(...)` and `BlueprintsWindow.deleteConfirm`. Copies are made through `BlueprintsLibrary.AddBlueprint(targetFolder, bp.Items, bp.Surfaces, bp.Decals)` plus metadata restoration. This shows a safe way to duplicate vanilla blueprint records without custom serialized payloads.
- **Folder persistence:** `BDT.FolderPersistence.cs` stores only the last-open blueprint folder path in `config.json`, patches private `BlueprintsWindow.setFolder(...)`, and gracefully resolves the deepest still-existing folder. This is a useful sidecar-persistence pattern for ATD metadata that should not make saves depend on ATD.
- **Blueprint export and batch placement:** `BDT.BlueprintExport.cs` patches `BlueprintDetail` and `BlueprintFolderDetail`, reads `IBlueprint.Items`, `IBlueprint.Surfaces`, `IBlueprint.Decals`, computes blueprint extents from entity transforms/trajectories/pillars and surface/decal rectangles, offsets copies, and activates the vanilla entity placer with `SetEntitiesToClone(copiedConfigs, copiedSurfaces, copiedDecals, ..., overlapDeltaX, overlapDeltaY)`. This suggests surfaces and decals are first-class blueprint payloads, but terrain designations are not represented in the vanilla `AddBlueprint` signature BDT uses.
- **Undo flow:** `BDT.UndoPatches.cs` records `BatchCreateStaticEntitiesCmd`, `PasteSurfaceDesignationsCmd`, `BatchAddSurfaceDecalCmd`, and `ReplaceEntityCmd` around `InputScheduler.processCmd(...)`; `BDT.UndoManager.cs` stores undo records in transient runtime memory, records pasted surface data from `PasteSurfaceDesignationsCmd.Data`, and reverts with `BatchRemoveSurfacePlacingDesignationsCmd`. This confirms there is a vanilla command path for pasted surface designations/decals, and it gives ATD a model for transient, save-removable undo of pasted terrain-designation operations.
- **Shortcut and settings pattern:** BDT has dedicated hotkey registry/settings code and a `ShortcutsManager` patch file. ATD should inspect those implementations when available locally for the exact way BDT exposes remappable mod hotkeys and integrates with vanilla shortcut UI.

Implications for ATD:

1. Do not assume the vanilla blueprint book can store terrain-designation cells: BDT's concrete `AddBlueprint(folder, items, surfaces, decals)` usage exposes entities, surfaces, and decals only. Terrain designations should use ATD storage unless further decompilation finds a terrain-payload extension point.
2. Terrain designation blueprints should be **player global**, not save-local or tower-local. Store the library in ATD-owned storage, preferably a human-readable file such as `TerrainDesignationBlueprints.json` in the mod folder or another player-profile-level ATD data location.
3. The library must support string copy/export and import-from-string so players can share terrain-designation blueprints outside the save and outside the vanilla blueprint book.
4. Mixed entity+terrain blueprints are not required. Keep the first implementation terrain-only even if the terrain blueprint UI is patched into the vanilla blueprint book.
5. If terrain patterns appear next to vanilla blueprints, use a patched UI entry that points to the ATD library record rather than modifying the vanilla blueprint payload. BDT's folder-path config pattern and recycle-bin copy behavior are useful models, but ATD should not rely on vanilla blueprints remaining loadable without ATD for terrain records.
6. For placement preview UX, BDT's batch placement code is a useful reference for aggregating payloads, computing bounds, applying offsets, respecting filters, and delegating to vanilla placement controllers.
7. For undo, mirror BDT's transient stack: record the exact terrain designation cells added/replaced/removed during paste and revert them via normal terrain designation manager calls or scheduled commands, without serializing the undo history.

## Save-removability constraints

Do not persist mod-owned live world objects solely to support this feature. Terrain designations themselves are vanilla objects and are safe once placed, but any ATD-owned clipboard/book metadata must be optional and removable.

Recommended persistence boundaries:

- In-memory clipboard: no save data required.
- ATD terrain-blueprint library: store player-global records in ATD-owned storage such as `TerrainDesignationBlueprints.json`, not in vanilla save-critical runtime managers.
- String import/export: support a compact serialized blueprint string for sharing and clipboard workflows independent of the player-global library file.
- Vanilla blueprint UI integration: if terrain blueprints appear in the normal blueprint book, those entries should be patched views over ATD-owned records. They may require ATD and do not need to be consumer-free.
- Notifications are unnecessary for the clipboard workflow; if later added, they must follow ATD's transient notification pattern.

## Proposed implementation milestones

### Milestone 1 — internal transform library and tests

Create a pure helper that can:

- capture `TerrainDesignation` instances into DTO cells;
- rotate/flip relative origins;
- permute corner heights;
- rebase heights at a target elevation;
- produce `DesignationData` instances for paste.

This can be unit-tested without game UI by using DTOs and expected corner arrays.

### Milestone 2 — clipboard copy/cut/paste UI

Add a terrain clipboard mode layered onto terrain designation tools, similar to corner mode:

- copy selection rectangle;
- cut selection rectangle;
- paste preview at cursor;
- rotate, flip, and translate up/down while previewing;
- honor the player's remapped vanilla rotate, flip, and terrain-height keybinds when those shortcut bindings are accessible;
- commit using `AddOrReplaceDesignation`;
- remove cut sources with `RemoveDesignation` after copy or after successful move semantics.

Reuse current input patterns where possible: mapped rotate key for rotation, vanilla flip key for reflection, vanilla terrain up/down keys for vertical translation, a visible toolbar item, and clear/cancel behavior consistent with vanilla terrain tools. If a native shortcut binding cannot be resolved, fall back to the vanilla defaults (`R`, `F`, `Q`, `E`) and log the fallback in debug output.

### Milestone 3 — stored pattern library

Add an ATD-specific, player-global terrain blueprint library before patching vanilla blueprint books:

- save named patterns as JSON/config records in player-global ATD storage, e.g. `TerrainDesignationBlueprints.json`;
- list/load/delete patterns in an ATD UI panel or patched blueprint-book view;
- export/import a compact pattern string and optionally a file;
- keep records versioned so future proto/height metadata can migrate.

### Milestone 4 — blueprint-book integration spike

Using the Blueprint Designers Toolkit patterns above, then decompiling game assemblies as needed, identify how ATD terrain blueprint records can appear alongside normal blueprint-book entries while remaining stored separately. The spike should answer:

- whether BDT's `AddBlueprint(folder, items, surfaces, decals)` path is the complete vanilla blueprint payload surface or only one overload;
- where and how to inject ATD terrain-blueprint entries into the vanilla blueprint book without serializing terrain payloads into vanilla blueprint records;
- how to key player-global `TerrainDesignationBlueprints.json` records to patched UI folders/categories, names, previews, and reorder operations;
- where terrain blueprint copy/cut/paste commands should be built;
- how placement preview and validation can include terrain designation overlays;
- what versioning/migration format the string export/import and JSON library should use.

Only proceed if the mod-removal behavior is safe.

## Research Answers to Open Questions

- **Where should player-global `TerrainDesignationBlueprints.json` live on each supported platform, and how should it be backed up/migrated?**
  - **Location**: It should reside in the game's global User Data directory under the `Blueprints` folder, which resolves to `C:\Users\<User>\AppData\Roaming\Captain of Industry\Blueprints\TerrainDesignationBlueprints.json` on Windows (resolved at runtime via `IFileSystemHelper.GetDirPath(FileType.Blueprints, ensureExists: true)`). This ensures blueprints are kept with the player's profile and survive mod updates.
  - **Backups**: Implement a rolling backup mechanism mirroring the game's native backup logic, preserving up to 5 historical copies (`TerrainDesignationBlueprints.json.bak[0-4]`).
  - **Migration**: Include a `"version"` field in the JSON structure (e.g., starting at `1`) to support future schema migrations during loading.

- **What compact, versioned string format should terrain blueprint export/import use?**
  - **Format**: A compressed JSON string prefixed with a format version, e.g., `ATD1:<base64-deflated-json>`. Base64-encoded GZip or Deflate compression ensures the text payload remains short, copy-pasteable, and easy to share on Discord or forums, while being straightforward to deserialize.

- **Can ATD key terrain-pattern records robustly enough to survive BDT-style blueprint-book reorder, rename, recycle-bin copy, and folder restore workflows?**
  - **Identity**: Since vanilla `IBlueprint` does not possess a stable unique GUID (only string `Name` and `Desc` are exposed on `IBlueprintItem`), ATD must map its external records using metadata embedded inside the vanilla blueprint records.
  - **Approach**: Inject a hidden identifier tag (e.g., `[ATD-ID: <guid>]`) into the vanilla blueprint's description field. Since description fields travel with the blueprint during reorders, copies, and folder restores, ATD can read this tag to find the corresponding terrain pattern in its external library.

- **Does vanilla expose a stable copy/paste command layer that can be patched without duplicating large UI flows?**
  - **Commands**: Yes! Vanilla uses input commands for designation mutations:
    - `AddTerrainDesignationsCmd` (takes a single prototype ID and an array of `DesignationData`)
    - `RemoveDesignationsCmd` (takes an array of tile coordinates)
  - Mod actions should schedule these through `IInputScheduler.ScheduleInputCmd` to ensure compatibility with multiplayer sync, command processing, and game replay records.

- **Are terrain designations managed globally only, or does each tower maintain additional ownership indices that need refresh after paste?**
  - **Ownership**: They are managed globally by the `TerrainDesignationsManager`. Individual towers (`MineTower` and `ForestryTower`) observe the manager's global events (`DesignationAdded`, `DesignationRemoved`, and `DesignationFulfilledChanged`) to dynamically link/unlink designations inside their boundaries. No manual tower indexing refresh is required.

- **Can mixed-proto terrain designation cells exist in one terrain-only blueprint without confusing construction or tower assignment logic?**
  - **Mixed Protos**: Yes. The game engine handles overlapping or mixed designations fine (towers selectively retrieve only the types they manage). However, because `AddTerrainDesignationsCmd` is prototype-specific, pasting a mixed blueprint requires partitioning the paste cells by prototype ID and scheduling one command per prototype.

- **Should pasted patterns preserve source proto IDs, force the currently active designation proto, or offer both modes?**
  - **UX Recommendation**: Offer both. By default, preserve the source prototype IDs to reconstruct complex multi-tool layouts. If a modifier key (like holding `Ctrl`) is pressed, or a toolbar toggle is selected, force all cells to use the currently active designation tool's prototype.

- **Should height rebasing use anchor NW height, minimum copied height, average surface height, or a player-selected origin cell?**
  - **Rebasing**: Use the NW height of the anchor cell at copy time as the reference zero baseline. Relative height deltas for all cells are calculated from this level. During paste, the pattern height initially aligns to the target surface height under the cursor, which the player can shift up or down.

- **Which vanilla shortcut identifiers expose terrain-designation up/down and flip on every supported game version?**
  - **Shortcuts**: They are defined in `ShortcutsMap` / `ShortcutsManager` as:
    - `RaiseUp` (default keybind: `E`)
    - `LowerDown` (default keybind: `Q`)
    - `Rotate` (default keybind: `R`)
    - `Flip` (default keybind: `F`)
  - Key bindings should be checked via `m_shortcutsManager.IsDown(m_shortcutsManager.<Name>)`.

- **How should queued/assigned excavator or truck work react when a cut removes active designations?**
  - **Reactivity**: No special handling is required. Deleting a designation calls `SetDestroyed()`, marking it as `IsDestroyed = true`. Active truck/excavator jobs continuously check for this flag and safely abort or re-evaluate assignments automatically.
