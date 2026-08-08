# Corner Designations — Architecture Reference
Current as of release: 0.4.0k

## Feature summary

`ATD.CornerDesignation.cs` adds a manual corner-shaping mode to the vanilla terrain designation tools. The feature works for mining, dumping, and leveling designation controllers and is implemented as a live mode layered on top of the game's normal designation UI and preview flow.

The key design constraint is that corner mode must coexist with vanilla terrain designation tools without forking them completely. ATD achieves that by:

- tracking which terrain designation controller is active
- selecting the matching designation proto for mining, dumping, or leveling
- suppressing the game's preview/update flow while corner mode is active
- rendering and placing ATD-owned corner designations itself

## Source file

| File | Role |
|---|---|
| `ATD.CornerDesignation.cs` | Corner mode state, input handling, drag fill, preview handling, snapping, and Harmony patch handlers |

## Core model

A corner designation is still a normal 4×4 `DesignationData` object. Outer and inner shapes offset one corner by `+1` or `-1` relative to the other three. Saddle shapes keep one diagonal pair at the baseline and offset the other diagonal pair by `+1` and `-1`.

### `CornerVariant`

The active shape is encoded by `CornerVariant`:

```csharp
internal enum CornerVariant
{
    OriginHigh = 0,
    PlusXHigh = 1,
    PlusXyHigh = 2,
    PlusYHigh = 3,
    OriginLow = 4,
    PlusXLow = 5,
    PlusXyLow = 6,
    PlusYLow = 7,
    OriginHighPlusXyLow = 8,
    PlusXHighPlusYLow = 9,
    PlusXyHighOriginLow = 10,
    PlusYHighPlusXLow = 11,
}
```

Interpretation:

- variants `0..3` are **outer** shapes: one corner at `baseHeight + 1`
- variants `4..7` are **inner** shapes: one corner at `baseHeight - 1`
- variants `8..11` are **Saddle** shapes: one corner at `baseHeight + 1`, the opposite corner at `baseHeight - 1`, and the other two at `baseHeight`
- the orientation index is `variant % 4`
  - `0` = origin / NW
  - `1` = plusX / NE
  - `2` = plusXY / SE
  - `3` = plusY / SW

For Saddle variants, the orientation identifies the high corner and the opposite
corner is the low corner. The default profile is `[2 1; 1 0]` when the baseline
is `1`.

## Building the designation

The concrete `DesignationData` is created by:

```csharp
BuildCornerDesignationData(Tile2i origin, int baseHeight, CornerVariant variant)
```

Implementation logic:

1. Start all four corners at `baseHeight`.
2. Compute `delta = +1` for outer variants or `-1` for inner variants.
3. Apply that delta to exactly one of the four corners based on `variant % 4`.
4. Build `DesignationData` in constructor order:
   - NW / origin
   - NE / plusX
   - SE / plusXy
   - SW / plusY

This means corner mode does not require a custom designation type. It produces standard Mafi terrain designations using the active terrain designation proto.

## Active tool and proto switching

Corner mode is tool-aware.

When `TerrainDesignationController.Activate` runs, ATD inspects the concrete controller type and selects the matching designation proto:

- `MiningDesignationController` → `s_miningProto`
- `DumpingDesignationController` → `s_dumpingProto`
- `LevelTerrainDesignationController` → `s_levelingProto`

This is why the same corner tool can be used for excavation, dumping, or leveling work.

## Runtime state

Important runtime fields:

| Field | Purpose |
|---|---|
| `s_cornerModeActive` | Whether ATD corner mode is currently active |
| `s_activeCornerVariant` | Current outer/inner/Saddle family and orientation |
| `s_designationToolActive` | Whether any supported terrain designation tool is active |
| `s_activeCornerProto` | Current mining/dumping/leveling designation proto |
| `s_dragging` | Whether a corner drag operation is in progress |
| `s_dragOrigin` | Snap-aligned tile where drag started |
| `s_dragBaseHeight` | Base height captured at drag start |
| `s_dragEffectiveVariant` | Variant captured at drag start |
| `s_previewedTiles` | Tiles currently rendered by ATD preview |
| `s_protectedCornerTiles` | Recently placed corner origins protected from one async overwrite |

These fields are runtime-only scaffolding and are not persisted.

## Input flow

`HandleCornerModeInput()` runs every frame from the ticker.

### Activation

- `K` enters corner mode if a supported designation tool is active.
- Pressing `K` again while already in corner mode cycles outer → Saddle → inner → outer.
- `F` exits corner mode.
- `R` rotates the current corner orientation.

### Drag lifecycle

- left mouse down → `TryBeginDrag()`
- left mouse up → `TryCommitDrag()`

`TryBeginDrag()`:

1. Resolves the current cursor terrain position.
2. Snaps it to the 4×4 designation grid.
3. Tries to derive a snapped base height and variant from neighbors.
4. Falls back to surface height + current designation-tool height bias if no snap target exists.
5. Stores the drag origin, base height, and effective variant.

`TryCommitDrag()`:

1. Resolves the drag end tile.
2. Clears the preview.
3. Computes the final corner-fill set.
4. Places each tile with `PlaceCornerAt()`.
5. Plays the controller's apply sound once if anything was placed.

## Neighbor snapping and height bias

Corner mode tries to behave like a natural extension of the vanilla designation tools rather than a disconnected overlay.

Two mechanisms matter here:

### Height bias

ATD reflects `m_heightBias` from the live `TerrainDesignationController`.

When no neighbor snap is available, the drag base height is:

```csharp
GetSurfaceHeight(terrMgr, snappedOrigin) + GetHeightBias()
```

So the normal raise/lower controls of the designation tool still affect corner placement.

### Neighbor snap

Before using the raw surface height, ATD attempts to snap the current tile to neighboring designations. This lets corner mode continue existing terrain gradients instead of forcing the player to manually match heights at every edge.

## Drag fill pattern

A rectangular drag does not just repeat the same corner variant on every tile. ATD computes a checkerboard-compatible fill.

High-level idea:

- tiles with the same parity as the drag origin use the effective variant
- tiles with the opposite parity use `CheckerboardComplement(variant)`
- each grid step shifts the absolute height so adjacent designations keep compatible shared-edge heights

This is what allows broad drag placement of stepped ramps or sloped transitions without creating edge discontinuities between adjacent 4×4 cells. Saddle drags use the same orientation on every cell and change the baseline along the corresponding tilted plane.

## Placement and overwrite protection

`PlaceCornerAt()` uses the active terrain designation proto and calls:

```csharp
s_desigManager.AddOrReplaceDesignation(s_activeCornerProto, data)
```

If placement succeeds, the origin tile is added to `s_protectedCornerTiles`.

Reason:

- the base game may still have an async placement command in flight from the original tool
- without protection, that command can immediately overwrite the corner designation ATD just placed

ATD patches `TerrainDesignationsManager.AddOrReplaceDesignation` and consumes one protection hit for each protected origin so the vanilla overwrite is blocked exactly once.

## Preview pipeline

Corner previews are rendered by `UpdateCornerPreview()`.

Important behavior:

- if corner mode is inactive, previews are cleared
- if RMB is held, ATD hides its preview and lets vanilla removal behavior proceed
- if dragging, preview shows the full computed corner-fill rectangle
- otherwise preview shows the current hover tile

ATD tracks preview ownership with:

- `s_previewedTiles`
- `s_atdPreviewActive`

`ClearAllPreviews()` temporarily sets `s_atdPreviewActive = true` while removing the preview tiles, so ATD's own cleanup calls are not blocked by its Harmony prefixes.

## Harmony integration

Corner mode depends on Harmony patches to hook into the vanilla designation flow.

### Controller lifecycle

- `TerrainDesignationController.Activate` postfix:
  - marks the designation tool as active
  - reflects `m_heightBias`, apply sound, and switch sound
  - selects the correct toolbox and designation proto
- `TerrainDesignationController.Deactivate` postfix:
  - clears runtime references
  - exits corner mode if needed

### Preview and input suppression

- `TerrainDesignationController.previewInitialDesignationAt` prefix:
  - suppresses the vanilla "invalid position" flow while corner mode is active
- `TerrainDesignationController.handleInitialState` prefix:
  - suppresses the vanilla left-click branch so ATD can own corner placement while still allowing normal RMB behavior
- `TerrainDesignationsRenderer.AddOrUpdatePreviewDesignation` prefix:
  - blocks the game's preview calls while corner mode is active unless the call came from ATD itself
- `TerrainDesignationsRenderer.RemovePreviewDesignation` prefix:
  - same ownership rule for preview removal

### Toolbox integration

- `AreaToolbox` patching injects the ATD Saddle, outer, and inner corner buttons into the designation toolbox; the Saddle button uses `Assets/Unity/UserInterface/General/MapBound.svg`
- `AreaToolbox.SetMode` patching lets ATD track the current vanilla mode index so it can restore the selected mode button when corner mode exits

### Placement protection

- `TerrainDesignationsManager.AddOrReplaceDesignation` prefix enforces the one-shot protected-origin rule described above

## Enter / exit behavior

### `EnterCornerMode()`

- marks corner mode active
- selects the requested default variant (`OriginHigh`, a Saddle variant, or its inner counterpart)
- updates button selection
- plays the switch sound
- deselects the vanilla mode buttons so the UI does not show both K-mode and flat/ramp mode as active at once

### `ExitCornerMode()`

- clears ATD previews first while the mode is still logically active
- hides the custom cursor
- clears drag state and protected tiles
- updates button selection
- restores the previously active vanilla area mode in the toolbox

That ordering matters. Preview cleanup must happen before `s_cornerModeActive` is reset so the preview-blocking prefixes still behave correctly during teardown.

## Relationship to the rest of ATD

Corner mode is logically separate from ATD's automatic scan/filter/place pipeline.

It shares infrastructure with the rest of the mod:

- designation manager access
- designation renderer access
- terrain cursor access
- reflected terrain-designation-controller state
- ticker-driven per-frame update hooks

But it is not part of the automatic mining scan or farming automation lifecycle.

## Public API boundary

Corner mode has no public API.

It is intentionally internal because its implementation is tightly coupled to:

- Harmony patch layout
- vanilla controller private fields
- toolbox injection details
- preview suppression behavior
- drag-fill geometry and snapping heuristics

If a public integration point is ever needed, it should be exposed as a dedicated API instead of relying on the current internal runtime scaffolding.
