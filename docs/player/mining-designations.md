# Mining Designations

*Current as of: v0.8.0*

## What it does

Auto Terrain Designations scans a mine tower's work area, finds the ore or material you want to target, and places mining designations automatically. Instead of drawing a large set of manual dig orders tile by tile, you let the mod build a connected designation region that follows the deposit.

The core workflow has five parts:

1. **Scan** — Sample the terrain under the tower and decide which 4×4 designation cells are worth mining.
2. **Filter** — Exclude poor or isolated tiles according to the selected ore filter and purity settings.
3. **Connect** — Fill holes and add corridors when needed so excavators and vehicles can reach the work.
4. **Ramp** — Try to add an access ramp from the tower area to the dig.
5. **Place** — Write the final mining designations into the world.

## How to use it

1. Select a mine tower.
2. Open the **Mining designations** panel in the inspector.
3. Adjust the per-tower settings if needed.
4. Choose a **Scanning filter** if you want to force one product, or leave it on Auto.
5. Click **Create Designations**.
6. If needed, click **Clear** to remove the ATD mining designations and try again with different settings.

The panel also includes a **Remove Debris** action. This designates debris in the tower area for cleanup without running a full ore scan. This is useful to clean large areas of debris without spending Unity.

## Development build: queued creation

In the current unreleased build, requests for different towers wait in order.
Clicking Create Designations again for the same tower replaces its pending
request. Capture and calculation can be stopped before submission. Once the
mining batch is submitted to the game, it finishes without rollback; Clear can
remove the resulting ATD designations afterwards. Mining and access generation
remain separate phases.

## Inspector settings

### `Max height diff`

Controls how much vertical difference ATD allows between neighboring designation cells.

- Lower values produce smoother, safer dig shapes.
- Higher values allow steeper excavation and can follow rougher deposits more aggressively.

### `Ramp width`

Controls the width of generated access ramps.

- `0` disables ramp generation.
- `1` (`AUTO`) generates a routed accessway using the largest suitable excavator.
- `2` (`T3`) generates a two-lane routed accessway for T3/Mega excavators.
- `3`–`5` use the legacy straight-ramp generator and reserve more width for vehicles.
- Routed accessways are enabled automatically for eligible modes; the old experimental toggle is no longer needed.

If ramp generation fails or produces a questionable result, ATD shows a warning notification on the tower.

### `Max layers to excavate`

Limits how many terrain layers ATD will dig down from the current surface.

- `∞` means no limit.
- Lower values are useful when you want a shallow surface pass instead of a full deep excavation.

### `Elevation limit`

Sets an absolute minimum elevation that ATD is allowed to dig to.

- `-∞` means no limit.
- Use this when you want to avoid digging below a known floor level.

### `Ore quality`

Controls how aggressively the scan rejects poor or contaminated ore.

- `Off` includes all matching tiles and digs to full depth.
- `Low` rejects only very weak ore.
- `Med` is a balanced setting for mixed deposits.
- `High` focuses on rich ore columns.
- `Max` is very selective and targets near-pure ore only.

### `Corridor clearance`

Controls whether ATD connects separated ore regions with passable corridors.

- `Off` leaves components separate.
- `1` allows narrow corridors for small and medium vehicles.
- `2` uses wider corridors suitable for mega vehicles.

### `Scanning filter`

Forces the scan to target one specific product.

When left on AUTO, ATD scans for useful products only if the tower area has no terrain designations. If terrain designations already exist, ATD creates no new mining field and instead treats the existing work as pathfinding goals. Debris cleanup and dirt scanning are manual-only selections.

### `Dumping priority`

Controls ATD's active input demand for dumping designations managed by this tower. The request uses ordinary vanilla logistics; tower-assigned trucks remain excavator escorts.

- **1–15** — actively request products accepted by the tower for every eligible managed dumping designation. The control uses vanilla import-priority labels (**1 - high** through **15 - low**) and competes with all other vanilla importers and dumping jobs according to the normal priority rules.
- **Passive** — use vanilla passive dumping. Only active exporters supply the tower's dumping work; neutral/passive storages are not pulled into it. During farmland automation's restricted fill window, ATD temporarily uses active priority 14 so farmable materials can be sourced without enabling tower-wide active dumping.

Neutral storage contents can be used as active sources without changing storage sliders. Explicit storage-to-tower routes, disabled outputs, protected quantities, reachability, truck filters, and vanilla partial-load/continuation rules still apply. A designation outside all tower areas remains governed by global dumping rules. Farm workflow products and accessway ownership determine which designations are eligible; ATD does not apply a separate soil filter.

The per-tower priority is in the Mining designations panel. The default is **Passive**. The world default is in Mod Settings → Tower defaults; **Save to config** writes that world default to `ATDsettings.json` for future worlds. Existing towers keep their concrete priority when the world default changes. The console command `atd_set_dumping_priority 1..15|Passive` changes the current world's default (omitting the argument reports it).

## Ore composition panel

The **Ore composition** panel analyzes the tower's current managed designations and estimates how much material is inside them.

Use it to:

- check what products are inside the current designation area
- compare mixed deposits before committing to a scan
- set excavator priority for a chosen product on vanilla mine towers

The panel reads the current designations directly, so it also works with designations that were not created by ATD.

## Ore Sorting Plant exports

ATD adds export controls to **Ore Sorting Plant** inspectors. Use the export
route panel to assign a destination building for all sorted products or for a
specific output, then set the plant's export priority alongside the normal
import controls. Routes are saved per plant.

The optional **assigned trucks only** control limits cargo collection and
delivery at that plant to its assigned trucks (and trucks assigned to
participating mine towers). Leave it disabled to keep vanilla truck selection.

## Excavator priority

When you set a mining priority from the ore composition cards, ATD stores it per tower and reapplies it to newly assigned excavators.

This means:

- you do not need to re-set the same priority every time a new excavator is assigned
- resetting the tower priority to none lets excavators use their normal behavior again

## Corner designations

ATD also adds manual **Corner Designations** for terrain tools, but they are useful well beyond the automatic mining workflow.

See [Corner Designations](corner-designations.md) for the standalone guide.

## Global settings and console commands

The per-tower settings in the inspector are separate from the global defaults stored in `ATDsettings.json`.

### Vanilla fixes

The per-world **Filter ore spikes** option is enabled by default. It detects
isolated, ultra-thin ore columns that extend far below the surrounding vanilla
deposit and prevents those anomalous tails from pulling the mine floor down.
The correction changes only how ATD interprets selected ore while planning;
it does not alter terrain or existing designations.

Bedrock is mineable and produces 2.5 times as much Rock as ordinary rock for
the same excavated volume. In captured spike-affected mines, this correction
reduced planned bedrock excavation by up to 99% locally and 36% across entire
designation plans.

Spike filtering runs before **Ore quality**, connectivity, bottom flattening,
and corner smoothing. Turning it off restores the unfiltered ore interpretation
for newly created Mining Designations.

### Terrain safety

The per-world **Avoid ocean** and **Avoid buildings** options also apply to regular mining plans. ATD removes mine cells that directly overlap ocean or a building safety perimeter before evaluating the remaining mine edge. It then avoids exterior terrain work that would reach protected ocean or buildings. **Safety policy** controls the combined landslide prediction and protected-area buffer from `MIN` through `MAX`; `MED` is the default. **Harvest disrupted trees** marks trees in the final mine body and its disturbance zone only when enabled; disabling it creates no ATD tree harvest orders.

### PERFORMANCE

The per-world **Use worker thread** option is enabled by default. It runs
pure access and mining planning on ATD's dedicated worker thread to reduce
game-thread stalls. Disable it for compatibility troubleshooting. Planning
then runs on the game thread and may cause pauses, especially in large mines.
The execution-mode change applies to new planning requests; an active request
finishes using its selected backend.

The per-world **Reduce oversized areas** option is also enabled by default.
If the normal access snapshot would exceed its configured memory ceiling, ATD
tries one smaller, geometry-only corridor around the current access sources
and goals. Its purpose is to find some usable route in very large modded tower
areas; it may choose a different route than a full-area search. A failed
reduced search is inconclusive and does not prove that the full area has no
route.
**Save as config** makes both current world values the defaults for new worlds.

Useful console commands:

| Command | What it does |
|---|---|
| `atd_get_settings` | Prints the current global defaults and purity arrays. |
| `atd_set_max_height_diff n` | Sets the global default maximum height difference between adjacent designation cells. |
| `atd_set_ramp_width n` | Sets the global default ramp width. |
| `atd_set_max_layers_to_excavate n` | Sets the global default surface-depth limit. `0` means no limit. |
| `atd_set_max_depth_to_dig_to n` | Sets the global default minimum elevation. Use `-` for no limit. |
| `atd_set_ore_purity_level n` | Sets the global default ore purity preset. |
| `atd_set_bottom_flattening on\|off` | Toggles the extra bottom-flattening pass. |
| `atd_set_bottom_flattening_strength n` | Sets how aggressively the bottom-flattening pass levels the designation floor (1–10). 1 = mildest (few tiles affected), 5 = median target (default), 10 = strongest (everything pulled to the deepest tile). |
| `atd_set_safety_policy MIN\|LOW\|MED\|HIGH\|MAX` | Applies a World safety preset. |
| `atd_set_landslide_predictor_slope_factor n` | Sets the expert slope factor behind **Safety policy**. |
| `atd_set_landslide_buffer n` | Sets the expert protected-area buffer behind **Safety policy**. |
| `atd_set_min_corridor_clearance n` | Sets the global default corridor clearance. |
| `atd_set_ramp_notifications on\|off` | Enables or disables ramp access warning notifications on mine towers (Failed, Truncated, NotAccessible icons). |
| `atd_set_auto_release_when_idle on\|off` | Sets the global default for the **Auto-release when idle** feature; individual towers can override this via the inspector toggle. |
| `atd_set_dumping_priority 1..15\|Passive` | Sets the current world's default dumping priority. Lower numbers are more urgent; `Passive` uses vanilla dumping and only imports from active exporters. |
| `atd_set_access_astar on\|off` | Switches the access-search algorithm for the current session. A* is enabled by default and this choice is not saved. |
| `atd_save_settings` | Writes the current in-memory defaults to `ATDsettings.json`. |
| `atd_reset_to_defaults` | Resets the in-memory defaults to the built-in values. |

For setter commands, omit the value (or values) to print the currently set value to the console and ATD log without changing it.

## Things to know

- ATD only clears mining designations that it recognizes as mining work. It does not bulk-remove unrelated designation types when using the clear action.
- Placing new mining designations can still overwrite other designation types if they occupy the same origin tile.
- Ramp generation tries to avoid buildings, but steep terrain or poor access can still require manual adjustment.
- Ore composition is an estimate of material inside the current designations and does not account for landslides. Landslides usually cause ore quality to degrade, as more rock and dirt are mixed in.
