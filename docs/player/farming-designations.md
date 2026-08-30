# Farmland preparation

*Current as of: v0.7.1*

## What it does

Flat level designations normally tell mine towers to move terrain to a target height, but the resulting top layer may contain rock, gravel, or other non-farmable material. Farmland preparation automates the full workflow so the final surface is ready for farming crops.

The mod works in two steps:

1. **Preparation** — Temporarily lowers the dig target one layer below the farm height where needed, so excavators remove non-farmable material from the future topsoil band. The original designation is restored once the band is clear.

2. **Filling** — Once preparation is complete, restricts the tower's dump rules to farmable products (dirt, compost, and similar) so trucks fill the topsoil band with farmable material only. With a dumping priority from **1** through **15**, ATD advertises active vanilla input demand for eligible dumping work, so a storage does not need to be configured as an exporter just to supply this fill. With **Passive** selected, ATD temporarily uses active priority **14** during this restricted fill window; outside it, Passive leaves the tower on vanilla passive dumping and only imports from active exporters. When all origins are done the dump rules are restored automatically.

Vehicle access ramps are added automatically when excavators or trucks cannot reach a work area.

While farming access is being prepared, ATD shows a progress toast with the current phase, visited and queued work, and processing time. Use **Stop automatic farming access** to cancel the active request, or **Hide** to suppress the toast for that request without cancelling it.

## How to use it

1. Draw one or more **flat level designations** inside a mine tower's area. All four corners of each 4×4 designation tile must be at the same target height.

2. Select the mine tower and open its inspector panel. Scroll down to **Farmland preparation**.

3. Enable the **Farmland preparation automation** toggle. The mod takes it from there.

4. Optionally enable **Auto-release when idle** if this tower should release assigned excavators and trucks while it has no pending mining or leveling work.

5. You can close the inspector — automation continues running in the background.

6. To pause, disable the toggle. All temporary modifications are restored immediately: original level designations reappear and dump rules return to their previous state.

## Farm Placement Assist

When **Farmland preparation automation** is enabled for a mine tower, farms placed fully inside that tower's area can be placed on uneven or non-farmable ground. ATD intercepts the placement, prepares the covered terrain cells, then places the farm automatically once the site is ready.

If the placement came from a blueprint, ATD holds the whole placement batch until the farm cells are ready, so related blueprint pieces are placed together instead of appearing early and blocking vehicle access.

## Status phases

| Phase | Meaning |
|---|---|
| `NeedsPreparation` | Non-farmable material is in the future topsoil band. Temporary dig being placed. |
| `NeedsLeveling` | Surface is at or above target but farmable. Normal leveling will finish it. |
| `Preparing` | Temporary dig designation is active; waiting for excavators to clear the band. |
| `ReadyForFilling` | Preparation is complete; waiting for tower-level filling to begin. |
| `Filling` | Farmable fill designation is active; waiting for trucks to complete the fill. |
| `Done` | Top layer is already farmable at the target height. No work needed. |
| `Blocked` | Something prevented normal progress (e.g. a designation was externally removed). |

## Things to know

- Only **flat** level designations participate. Designations with different corner heights are ignored.
- The tower's dump rules are modified only during filling, and only for that tower.
- The mod never changes global (cross-tower) dump rules.
- Active dumping uses ordinary available trucks, including trucks temporarily soft-released by ATD. Tower-assigned trucks continue to escort excavators; they are not used for dumping.
- Active dumping follows the tower's live dumping priority and the workflow's managed designations. It includes preparation, final filling, and accessways created by farmland automation; unrelated mining or interactive accessways are included when they are managed by the same tower. **Passive** disables ATD's active demand outside the farm-fill window; during restricted farm filling, ATD uses active priority 14 to avoid requiring a player priority override.
- If the tower has explicit storage-to-tower routes, only those storages can supply active dumping. Otherwise normal vanilla storage route and truck-filter rules still apply. A neutral storage with export disabled can therefore supply the farm only through the tower's active request, not ordinary global dumping.
- The initial filling product set is all farmable products. If you do not want a product such as compost dumped into this farm, disable that product on the tower before the relevant import is dispatched.
- If no eligible soil source or reachable truck exists, filling remains pending silently and retries on later farming ticks.
- Automation state is saved per tower. After reloading a save, towers restore their own farmland preparation automation setting.
- Pending Farm Placement Assist batches have limited save/load recovery. Pending farms restore crop schedules, fertility targets, rotation, and reflection; full blueprint configuration persistence is still planned.
- If you manually remove or replace a tracked designation, the mod drops that tile from the session. Place a new flat level designation and the next scan will pick it up.
- When extending a farming area adjacent to a previously completed area, the completed tiles are temporarily hidden to prevent dirt-spill conflicts with the new preparation work. They are restored together with the new tiles during the filling phase.
- When extending a farming area, make sure new designations use the correct target height. If they do not match the adjacent area's height, new tiles may be treated as a separate session at the wrong elevation.

## Settings

### Idle vehicle behavior

The tower panel has an **Auto-release excavators when idle** toggle and an **Idle truck behavior** dropdown.

When enabled, the selected vehicle class is automatically unassigned while none of the tower's managed mining or leveling designations have pending excavation work, while the tower is paused, or while the tower has no unpaused assigned excavator. The last condition also covers towers where every assigned excavator is paused.

- Released vehicles are tracked. When pending excavation work returns, ATD re-assigns those vehicles back to the tower.
- Useful when trucks can assist with general hauling and construction while this tower is idle. They can also be assigned to another tower, but may not be available when this tower's work resumes, especially if that tower is not using Soft release.
- Excavator auto-release defaults to off. The global excavator default is controlled by **autoReleaseExcavatorsWhenIdle** in `ATDsettings.json`.
- Truck behavior options are **Park at tower** (vanilla behavior), **Stay put** (the ATD default; keeps trucks assigned without sending them back), and **Soft release** (temporarily unassigns trucks and reassigns them when work returns).
- The global truck default is controlled by **truckIdlePolicy** in `ATDsettings.json`: `0` = Park at tower, `1` = Stay put, `2` = Soft release.

### `farmingPanelCollapsed` (ATDsettings.json)

Controls the default collapsed state of the **Farmland preparation** panel in the mine tower inspector.

Default: `true` (collapsed)

Set this to `false` if you prefer the panel to start expanded whenever you open a mine tower inspector.

Can also be changed at runtime — see console commands below.

## Console commands

Open the in-game developer console (default: **F8** or **~**) to run these.

| Command | What it does |
|---|---|
| `atd_set_farming_panel_collapsed true\|false` | Sets whether the **Farmland preparation** panel starts collapsed by default. Change is saved to `ATDsettings.json`. |
| `atd_farming_analyze_origin x y` | Prints the read-only farming analysis for the designation at tile (x, y). Coordinates snap to the 4×4 designation origin. Useful for checking why a tile is `Blocked` or `NeedsPreparation`. |
| `atd_farming_dump_all` | Prints full session state and terrain analysis for every mine tower. Useful for a broad overview of what all towers are doing. |

The following commands are debug tools for single-origin testing, not intended for normal play:

| Command | What it does |
|---|---|
| `atd_farming_prepare_origin x y` | Manually places the temporary preparation designation for one `NeedsPreparation` origin. |
| `atd_farming_restore_origin x y` | Restores the original level designation stored by the prepare command above. |

