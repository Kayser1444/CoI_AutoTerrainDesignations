v0.4.6a [unreleased]
* Fixed: **Avoid buildings** now refreshes static-building occupancy for every explicit accessway request and captures nearby footprints beyond the tower boundary, including the outside portion of buildings that straddle it. Building snapshots now log entity/tile counts and capture bounds for field diagnostics.
* Changed: accessway pathfinding now treats trees as cost-free removable obstacles. Tree metadata is retained only to mark required or disrupted trees after the route is selected, reducing forest-driven route bias; the now-unused **Tree harvest cost** setting and JSON output were removed, while legacy JSON values are harmlessly ignored.
* Documented: reviewed V2 requirements against the current V1 implementation and added a separate staged implementation plan. The review identifies the under-specified four-origin brush/two-token profile model as a blocker, recommends a concrete two-origin band-slice state with 2x2 flat turn landings, and carries V1's categorical generated-origin revisit rejection into V2. Rare mining-body waists are tracked as a post-rollout refinement rather than a V2 release gate; core Mining Designations integration for Avoid ocean, Avoid buildings, and Harvest disrupted trees is retained in the refinement roadmap.
* Added: per-world **Harvest disrupted trees** pathfinder option, enabled by default. Enabled routes mark every tree in the finalized accessway footprint and projected disturbance rays for harvest; disabled routes retain minimum path-clearance harvesting. ATD persists primitive ownership coordinates and both Clear actions remove only harvest markers newly placed by that tower.
* Fixed: the experimental access snapshot self-test no longer rejects every search after exact-terrain V cells began materializing as prop cleanup instead of no-op terrain designations; its generated-path and flat-turn fixtures now contain real mining work.
* Fixed: exact-terrain V cells are no longer materialized as no-op leveling designations. Removable props on those elevation-validated cells become explicit cleanup designations even when the coarse G overlay marked the origin as durability-blocked; V cells with real cut/fill work continue to use terrain removal.
* Fixed: a generated cell connecting directly to an existing terrain designation no longer treats that connected predecessor's shared footprint as opposing side-ray work. Unrelated mining/dumping designations remain blockers, and building checks remain active on the same tiles.
* Fixed: ray end buffers no longer stack on top of the separate two-tile building-ray margin after terrain intersection. Buildings and pipes still block active terrain disturbance with their dedicated margin, while the post-termination tail remains active for ocean, designation conflicts, and generated-path history.
* Changed: ray slope conservatism now defaults to `1` and accepts values through `1.5` for an intentionally runnier safety envelope; the shared candidate/existing-work ray end buffer now defaults to three terrain tiles.
* Fixed: cutting rays now terminate only after passing both terrain and the dry `+1` elevation, then apply their configured end buffer. This prevents low shoreline intersections from hiding an ocean break immediately beyond the apparent terrain contact. Candidate rays that already meet terrain at their origin remain cost-free but still apply their safety buffer, and buffered ocean checks use the cut corner's elevation rather than an extrapolated post-termination height.
* Changed: candidate and existing-work landscaping rays now share one slope-conservatism setting and one end buffer. Cut rays interpolate across the intact material's collapse range (100% uses the runniest bound; lower values are more aggressive), while dumping rays use the corresponding loose/disrupted material range. Legacy JSON keys remain accepted as aliases.
* Fixed: existing-work cardinal rays no longer stop at an arbitrary configurable distance. Every relevant ray traces to terrain intersection, with unresolved map-edge projection treated as a snapshot failure; the candidate-ray distance cap and unresolved penalty remain available for bounded search work.
* Changed: experimental accessway AUTO vehicle clearance now uses the largest assigned or pre-assigned excavator, then the largest excavator actually present on the map; with no excavators present AUTO behaves as OFF instead of selecting an arbitrary registered prototype.
* Added: mine-tower vehicle construction ordering and pre-assignment, ported from AFD. Assignment controls can enqueue at the nearest eligible depot, persist pending assignments in a removable state blob, cancel queued orders, and assign completed vehicles automatically.
* Fixed while porting vehicle ordering: pre-assignment queue reconciliation now seeds depots that contain only vanilla or other-mod orders, and assignment controls refresh depot eligibility instead of retaining their inspector-open state.
* Changed: AUTO vehicle selection breaks equal horizontal-clearance ties by greater height clearance, then by ascending prototype ID.
* Fixed AFD coexistence for vehicle ordering: AFD now owns forestry-tower assignment controls, ATD owns mine-tower controls, and their depot queue observers clear only decorations they previously applied.
* Changed: nearest-depot selection for tower vehicle orders now uses immediate straight-line distance, avoiding perceptible confirmation delays from terrain path searches.
* Fixed: mine- and forestry-tower vehicle `+` controls remain visible and enabled for supported unlocked prototypes when no idle vehicle is available; depot eligibility is resolved when the order action is invoked.
* Fixed: AFD/ATD assignment-control ownership is scoped by vehicle proto family rather than the inspector entity-provider runtime type, restoring zero-availability order buttons while keeping forestry and mining handlers separate.
* Added concise nearest-depot selection diagnostics for vehicle orders, including eligible depot count, chosen depot, and squared straight-line distance.
* Added targeted tower-ground frontier diagnostics when an experimental access snapshot finds 16 or fewer tower-reachable ground goals, to expose the exclusions sealing the goal component.
* Fixed experimental accessway tower-ground seeding accepting isolated vanilla docking pockets with no traversable cardinal exit; seed selection now continues outward to a usable vehicle-reachable ground component.
* Changed experimental accessway tower goals from the entire reachable ground component to up to eight mask-aware radial goals: the first reachable tile along each cardinal/diagonal ray from the tower center, capped at 12 tiles, with per-direction diagnostics.
* Fixed: every generated V cell now checks the full durability envelope of existing terrain designations. The previous post-connection directional shortcut could ignore a future cut beside or behind the route and allow a long terrace whose support would later collapse.
* Improved: terminal V/G handoffs no longer require the entire outer edge of the designation to have crossed natural ground. Mining or dumping is inferred from the connected edge, while the existing mask-aware central-lane and post-work escape checks decide whether the actual exit corridor is usable.
* Fixed: the relaxed terminal handoff still requires at least one sample on the actual exit edge to reach or cross natural ground. An interior crossing can no longer escape sideways and validate a ramp whose leading edge remains completely buried (or suspended for dumping).
* Fixed: the exit-edge crest must occur in a vehicle-clearance-eligible central lane, rather than an unusable outer sample. Terrain comparison remains effectively exact at the game's fixed-point height resolution.
* Fixed: terminal handoff workability now uses the vanilla terrain-designator fulfillment predicate on clearance-eligible exit lanes, including terrain, props, stumps, occupancy, and upper-edge semantics, instead of approximating excavator/truck readiness from height alone.
* Fixed: north/east V-to-ground handoffs now compare outside terrain with the designation's shared boundary height rather than the adjacent inside sample, removing a directional false rejection that could make a ramp crest, dive, and turn before handing off.
* Added: bounded multi-cell terminal handoffs can reclassify an existing final straight V stretch or synthesize a terminal-only flat/rising extension in the travel direction, then validate escape through the combined post-work footprint. The maximum is `1 + ceil(vehicle width / 4)` designation cells (two for ordinary excavators and three for Mega/T3), avoiding awkward long mouths without adding general search branching.
* Fixed: terminal handoff escape validation now treats work performed by the terminal mining/dumping designation as completed. A profile that crosses ground inside a captured, vehicle-pathable tile can therefore hand off laterally through that new surface; projected work from the main mining body and other G exclusions remain blocked.
* Fixed: generated accessway history now retains elevation-aware side-ray envelopes. A later V cell is rejected above any prior cutting-ray ceiling or below any prior filling-ray floor, including profiles above natural ground; G traversal continues to use the full disturbed-tile exclusion.
* Added diagnostics around generated-only Clear actions and subsequent mining-plan placement to identify cleared accessway origins that remain live or are recreated before a failed access search.
* Extended clear/placement diagnostics to retain the cleared accessway origin set and report exactly which of those origins are recomputed as mining-plan cells before pathfinding.
* Added a before/after access-search mutation audit over every live designation prototype and corner profile, detecting additions, removals, or replacements during failed long-running searches.
* Fixed: projected designation rays now retain vertical envelopes for V traversal instead of boolean blocking. Cuts record the lowest future support ceiling and fills the highest future surface floor; every V profile is checked at its actual elevation, allowing multi-cell waist connections while rejecting unsupported or buried routes. G traversal retains the vehicle-expanded boolean disturbance mask.
* Fixed: Avoid ocean once again requires the vehicle-bearing V profile itself to finish at or above height 1 on ocean tiles. Dumping side rays may still spill into ocean, but submerged fill can no longer be selected as a drivable accessway.
* Changed: when the Ore composition refresh button is clicked, if no minable designations are found, clear the tower priority.
* Added: per-world **Avoid ocean** and **Avoid buildings** Pathfinder settings persisted in the removable state blob. `ATDsettings.json` supplies their enabled-by-default values for new games; Mining Designations generation does not use them yet.
* Changed: **Scanning filter: AUTO** scans only for useful products when the tower area has no terrain designations. When terrain designations already exist, AUTO creates no new mining field and treats the existing work as pathfinding goals. Debris and dirt remain manual-only selections.
* Changed: the designations Clear button now clears only that tower's ATD-generated designations on normal click and clears all terrain designations in the tower area on Shift-click.
* Added: ATD-generated designation ownership is persisted as primitive tile coordinates in the existing removable tower-state JSON, so generated-only clearing remains precise after save/load.
* Changed: the debris button now places prop-removal designations only on cells without existing terrain or forestry designations; Shift-click explicitly replaces existing designations.
* Added: per-tower vehicle-clearance selection with OFF, AUTO, T1, T2, and T3 modes; AUTO derives pathability from assigned excavators with an available excavator-prototype fallback, while legacy ramp-width settings migrate to OFF or AUTO.
* Fixed: generated disturbance rays account for the selected vehicle footprint: one tile around T1/T2 ground cells and two tiles around T3 ground cells. Ground traversal uses the material-aware projected disturbance of existing designations, while generated/fixed V feasibility uses the designation hourglass so accessways can still enter the waist and connect. Buildings are handled only as hard footprint-plus-vehicle-clearance obstacles and no longer contribute terrain-disturbance hourglasses.
* Fixed: V-to-G handoffs now revalidate the first ground cell after replacing the terminal generated node's provisional leveling rays with its finalized mining or dumping rays; the accumulated ray envelopes from every generated predecessor remain active throughout the ground route.
* Fixed: completed generated/fixed V networks no longer act as ground-flood conduits when rebuilding tower-reachable G goals for later clusters. They remain separate reusable V goals, preventing ramps from incorrectly making disconnected upper shelves valid ground goals.
* Improved: generated V interior work cost now samples all four profile corners with quarter-footprint weights and operation-aware cut/fill gaps, replacing the center-height approximation while preserving the established flat-cell normalization. Exterior disturbance rays continue from distance 1.
* Fixed: Create Designations is now globally single-flight. A newer request cancels the active operation at its next yield boundary, waits for cleanup, and starts from a fresh snapshot; superseded queued requests are discarded so shared pathfinder and materialization state cannot overlap.
* Fixed: disabling **Avoid ocean** now also disables the legacy `OceanBelowMinimum` V-profile exclusion, allowing risky shoreline landscaping as intended. Natural ocean remains excluded from drivable G nodes.
* Fixed: generated-only clearing now removes designations directly from the tower's persisted ATD origin registry instead of requiring them to appear in vanilla's `ManagedDesignations` collection. Shift-clear spatially removes every terrain designation fully inside the tower area, including generated leveling and specialized ramp terminals.
* Changed: **Avoid ocean** now blocks only projected cutting into ocean terrain. Dumping/fill rays may extend into ocean normally; natural ocean remains unavailable as drivable ground until filled.
* Fixed: ocean avoidance now combines vanilla's persistent ocean-tile flag with the projected surface-height threshold. Dry cuts at or above level 1 are allowed; only projected underwater cuts are rejected.
* Changed: corner designation mode is now configured in the vanilla **Controls** settings under **Kayser's Automatic Terrain Designations (Mod)** instead of the ATD Mod Settings tab.
* Migrated: existing `cornerDesignationKey` values from `ATDsettings.json` are used as a fallback on first startup and persisted into the vanilla controls store. Persisted vanilla controls values take precedence, followed by the legacy JSON value and then the code default (`K`).
* Fixed: changing the corner designation mode binding in vanilla Controls now persists across game restarts instead of being overwritten by the legacy JSON setting.
* Changed: Ore composition card priority button icon to the standard Priority icon
* Changed: Pathfinder settings tab icon to Connect128
* Refactored: experimental accessway generated-node entry costs now use one structured landscaping-cost calculation for search relaxation and result diagnostics, with baseline A*/Dijkstra cost-breakdown checks ahead of side-ray scoring.
* Fixed: experimental accessway cost diagnostics now reconstruct the actual predecessor-to-node traversal distance, including zero-length profile-to-ground handoffs, instead of assuming every ground node costs one tile.
* Added: experimental access snapshots now capture precise side-ray terrain heights, depth-specific cut-material layers, physical map bounds, ocean state, and a tower-wide conservative dumping-material slope for the planned landscaping scorer.
* Added: a bounded side-ray landscaping integrator with accelerating samples, finite caps and unresolved penalties, and operation-specific map-edge and ocean behavior.
* Added: experimental access search now charges direction-aware side-ray landscaping cost when entering generated cells, filters corner work by mining/dumping/leveling operation, rejects fatal boundary cases, and reports separate direct/ray cost diagnostics.
* Improved: side-ray tuning uses centralized internal weights and caps, and diagnostics report the selected route's center-only comparison cost alongside its ray components and bounded sample count.
* Fixed: an explicitly empty tower dumping-rule set now makes fill corners infeasible, including leveling fill, instead of silently assigning a fallback dumping material.
* Fixed: prospective V/G handoffs now require an operation-fulfilled, pathable or cleanup-eligible contact through the interior of the free edge with matching ground immediately outside; corner-only green contact no longer qualifies.
* Fixed: access routes can no longer U-turn through ground that their own generated designations or positive-work side-ray wedges will disturb; only the immediate exit through the current V footprint is exempted.
* Added: Mod setting and API methods (`GetAccessPropCleanupLandscapingCost`, `SetAccessPropCleanupLandscapingCost`) to configure prop cleanup landscaping cost.
  - Non-tree prop cleanup defaults to `8`, calibrated from observed excavator cleanup effort; tree cleanup remains separately configurable and defaults to `6`.
* Changed: Renamed setting `accessWorkDistanceScale` to `accessLandscapingCostDistanceScale` (and its UI row to **Landscaping cost vs. distance**) to better reflect that one unit of landscaping cost equals one unit of digging/dumping rock.
* Updated: Mod settings UI layout, descriptions, and translations for experimental access settings across German, Spanish, Italian, Portuguese, Russian, Swedish, and Chinese.
* Added: Extensive planned pathfinding documentation covering debris handling, side-ray cost functions, and implementation sequence.
* Improved: experimental accessways can now connect active mining, dumping, and leveling work endpoints into the tower access network without rewriting those endpoint designations
* Improved: experimental V-to-G handoff operation selection uses the connected cluster-origin predecessor for single-origin access paths instead of relying on a misleading center estimate, and final terminal placement checks walk backward to target the last generated V node before the ground suffix
* Improved: experimental access decisions are logged to debug console, and global `suppressLegacyAccessRamps` settings are now read from the migrated `ATDsettings.json` configuration file rather than save files
* Added: a **Shift-click Clear action** in the designations panel to clear ONLY automatic ATD-generated designations inside the tower's boundaries, keeping player-drawn designations intact; the panel tooltip is updated with the shortcut instruction
* Changed: removed the `[Kayser's AutoTerrainDesignations]` suffix tag from general standalone tooltips to clean up UI noise, keeping it focused on inspector panel headers
* Improved: generated accessways are tracked as runtime providers, so later clusters can reuse newly placed routes instead of creating duplicate ramps back to the tower
* Fixed: experimental routes can hand off from V-space to any valid ground node while still requiring the final route to connect to tower-reachable ground, reducing unnecessary V-designations over drivable terrain
* Fixed: endpoint starts can immediately hand off to ground when valid, avoiding needless flat accessway designations over already pathable terrain
* Fixed: fixed-profile/provider-join routes can end by reusing existing planned access profiles, including zero-new-designation paths that already provide access
* Added: **Suppress legacy ramps** experimental/debug setting and console command, allowing V1 accessway failures to be tested without the straight-ramp fallback taking over
* Improved: accessway diagnostics now distinguish generated mining clusters from external terrain-work endpoints, report fixed-network request shape, and include a `legacy-suppressed` decision reason when fallback is intentionally disabled
* Updated: access framework design notes and release defaults for the current V1 alpha behavior

v0.4.5a | 2026-06-21
* Improved: experimental turning accessways now hand off to ground using vanilla's prospective mining/dumping workability checks instead of requiring exact terrain-height contact; the final accessway tile uses the matching mining or dumping designation while mixed cut/fill route bodies continue to use leveling designations
* Fixed: V-to-G operation selection now uses the predecessor tile's relation to current terrain, allowing a route to crest uneven ground instead of continuing through unnecessary V tiles near the surface
* Fixed: tower-reachable ground is now flooded from actual vanilla-pathable terrain near the tower, including when the tower lies outside its managed area; the previous nearest-in-area seed could select a disconnected ground component
* Fixed: experimental paths can no longer revisit an earlier V origin or travel below the minimum ocean height
* Improved: active terrain designations and building foundations share the symmetric mining/dumping landslide exclusion model; durability checks use perimeter sources and direction-aware pruning with the public slope factor constrained to the validated `0.05..2` range
* Improved: A* is now the default experimental search and uses an admissible combined horizontal/height travel lower bound; work cost uses the documented quadratic center-height estimate
* Improved: difficult access searches are substantially faster through corrected goal flooding, durability-source pruning, and snapshot reuse; snapshot, search, and materialization runtimes are included in experimental diagnostics
* Fixed: large mine tower areas are scanned through non-overlapping chunks within vanilla's 192-tile designation-query limit, preventing clamping warnings and omitted existing designations
* Changed: accessway placement rematerializes against the unchanged search snapshot instead of rebuilding an identical snapshot immediately before placement; successful placement remains transactional and the next cluster receives a fresh snapshot
* Updated: access framework, pathfinding design, roadmap, English localization, and Chinese localization for the refined V1 behavior and settings limits

v0.4.4 | 2026-06-20 [packaged]
* Added: **Experimental turning & switchback accessways** (least-work corridor pathfinding) for mine towers, enabled by default (only active when the tower's **Ramp Width** setting is set to 1) 
  - Uses a new 2.5D pathfinding search (supports reference Dijkstra and optimized A*) over the terrain heightfield to evaluate and select the cheapest access route
  - Automatically plans and places multi-directional, turning, and switchback corridors using vanilla flat and slope designations (requires a tower ramp width of 1; corridor clearance remains independent)
  - Added configuration parameters under the new **Experimental accessways** Mod Settings section: **Use A* search**, **Work distance scale** (weight of terrain work vs driving distance), and **Landslide horizontal run** (adjusts safety/exclusion margin per level)
* Changed: changed default starting **Ramp Width** default setting for new mine towers from 2 to 1 so the new experimental turning and switchback pathfinding is active by default
* Changed: renamed the Mod Settings **Mining defaults** heading to **Mine control tower defaults** to match the vanilla tower name
* Changed: split **Auto-release when idle** into separate per-tower/global toggles for excavators and trucks; legacy `autoReleaseVehiclesWhenIdle` settings still migrate by setting both new defaults
* Fixed: auto-release now treats paused mine towers as idle, so enabled vehicle classes are released while the tower is paused and restored when excavation work resumes
* Added: the Farmland preparation panel auto-release tooltips now include a compact assigned/ATD-released vehicle list for the selected tower
* Fixed: ramp generation could incorrectly skip with "existing planned ramp designation(s) already provide surface access" when an unrelated reachable accessway existed in the tower area; the duplicate-access shortcut now requires every disconnected excavation cluster to have height-compatible access to tower-reachable ground or to a connected existing accessway
* Updated: translations for German, Spanish, Italian, Portuguese, Russian, Swedish, and Chinese to support the split auto-release toggles and assigned vehicles list
* Fixed: vehicle auto-release could leave vehicles stuck assigned to invisible or deconstructed mine towers after saving and restarting. Release tracking is now immediately pruned and save-time reassignments are skipped for any tower that is deconstructing or not fully constructed, keeping released vehicles unassigned and free.

v0.4.3 | 2026-06-06
* Added AutoHelpers shared **Mod Settings** tabs for ATD global defaults, game settings, and ore-quality thresholds, with localized labels/tooltips and shared-window tab icons
* Added: Chinese translation
* Changed: Farm Placement Assist now captures and replays the entire intercepted `BatchCreateStaticEntitiesCmd` when a farm in the batch needs terrain preparation, so mixed farm blueprints stay atomic and non-farm blueprint pieces are not dropped or placed early
* Added limited config-backed save/load recovery for pending Farm Placement Assist batches; pending farms retain proto, transform, reflection, crop schedule, and fertility target, but full blueprint/entity configuration persistence is not implemented yet
* Fixed: Farm Placement Assist replayed intercepted placements with crop assignments, recipe selections, port configurations, and reflection lost; the original `PlacementIntent` stored only proto, position, and rotation, discarding the rest of the engine's entity configuration; the intent now stores the full `EntityConfigData` from the intercepted command and replays it verbatim via `BatchCreateStaticEntitiesCmd` so all blueprint-configured state is preserved
* Added **bottomFlatteningStrength** global setting (1–10, default 5): controls which depth-percentile of each connected ore component is used as the flattening target during the bottom-flattening pass; 1 = mildest (90th-percentile depth, few tiles adjusted), 5 = median (default behavior), 10 = strongest (deepest tile, everything pulled down); configurable via `ATDsettings.json` or the new **atd_set_bottom_flattening_strength** console command
* Changed: farming access ramps to back-row farmland clusters now lay a flat 1-cell bridge at the neighbor cluster's target z instead of a wedge climbing back to surface; clusters are processed closest-to-tower first with a fixpoint outer loop so each cluster can become reachable through previously-placed ramps and bridges, and `TryPlaceRamp` now references the mouth-approach designation's target z when the approach lands inside another non-ramp designation, producing a flat body row that designates the gap to the shared target z (true bridge) rather than wedging up to the unmodified surface

v0.4.2c | 2026-05-30 [packaged]
* Added: **Farm Placement Assist** (experimental): when a farm building is placed inside a mine tower area, ATD intercepts the placement, automatically injects flat leveling designations for each tile the farm covers, and replays the placement once the site is fully prepared; farms placed on uneven or infertile terrain inside a tower area are handled without any manual designation step

v0.4.2b | 2026-05-29 [packaged]
* Fixed: farming preparation access ramp was not generated when terrain was entirely above the designation target level (all-red perimeter on a fresh excavation pad); a physical-fallback BFS collected perimeter target tiles for all designations regardless of readiness and concluded the cluster was accessible from surrounding ground-level terrain; removing the fallback causes an empty target-tile set to correctly mark the cluster as inaccessible and trigger ramp placement
* Fixed: farming filling access ramp was not generated for fill designations placed above current terrain; the vehicle-work readiness check used the mining fulfillment bitmap for both the preparation and filling passes; fresh fill designations have the mining bit set (terrain ≤ target) but the dumping bit unset, so the check incorrectly concluded the cluster was ready; the filling pass now gates on `IsReadyToDumpNonAmphibious()`, matching the vanilla truck dispatch condition, and correctly marks an unstarted fill cluster as inaccessible until a ramp is in place
* Changed: ore composition cards now use the game's speckled panel background (matching the AFD forestry information panel style) and increased card padding from 4 to 6 pt
* Changed: ore quality priority button tooltips reworded to reflect tower-level persistence — "Set tower mining priority to {0}" / "Tower mining priority set to {0}. Click to unset."

v0.4.2 | 2026-05-29
* Added: per-tower settings (max height diff, ramp width, max layers, max depth, ore quality, corridor clearance, auto-release, ore filter, mining priority, and farmland preparation automation state) are now persisted into the vanilla save file via a `config.json` state blob; settings survive save/load cycles without any custom save format
* Changed: ore filter selection is now keyed by entity ID instead of object reference, so it round-trips correctly through save/load
* Changed: `CoI.AutoHelpers` Runtime, VanillaAttachments, and Persistence source modules are now compiled into ATD (previously only Localization and Logging were included)
* Added: collapsed state of each tower's **Mining designations**, **Ore composition**, and **Farmland preparation** panels is now persisted per tower in the save blob and restored on load
* Fixed: all three ATD panels opened in the global default collapsed state after load instead of the saved per-tower state; the entity is null during inspector construction so the per-tower state is now applied on inspector activation when the entity is bound
* Added three debug console commands: **atd_dump_pending_save_json** (prints the JSON that would be written on save), **atd_dump_last_loaded_json** (prints the JSON from the last load), **atd_dump_panel_state** (prints per-tower panel collapsed state from memory)

v0.4.1 | 2026-05-22
* Fixed: corner designation rotation now responds to the player's mapped rotate key instead of always responding to R
* Added **cornerDesignationKey** global setting: the key used to enter and toggle corner designation mode; configurable via **ATDsettings.json** (default: `K`) or the new **atdSetCornerDesignationKey** console command (accepts any Unity KeyCode name, e.g. `Alpha1`, `F1`)
* Fixed: a farming preparation origin backed by a dumping designation was dropped from session tracking on the tick after it reached `ReadyForFilling`; the drop left any shoulder designations in `PreparationShoulderOrigins` permanently blocking `CanStartTowerLevelFilling`, so the session never entered the filling phase; this occurred consistently when shoulder designations were present
* Fixed: a farming preparation origin whose terrain was already at the final target height (`Done` analysis state) while still in the `Preparing` phase was not advanced to `ReadyForFilling` and remained stuck permanently, blocking the session from starting the filling phase
* Fixed: farming preparation corner shoulders are now only placed at true outside corners of the tracked farming cluster, after both adjacent cardinal shoulder ramps exist; this preserves proper corner closure without creating red-edge corner designations inside multi-tile preparation pads
* Fixed: farming filling access ramps now use the dumping fulfillment probe when planning a ramp mouth, so dump ramps continue until they connect to/cross existing terrain instead of stopping early as a floating lip above the ground
* Changed: visible UI wording now uses **Mining designations** for the mining panel, **Ore quality** for the ore filtering preset, and sentence-case panel headings such as **Ore composition** and **Farmland preparation**; technical settings keys and console command names remain unchanged for compatibility
* Changed: ramp warning notifications now use the same designation icon as the **Create Designations** button while retaining warning notification styling
* Changed: farming final filling and final rim alignment now use dumping designations instead of leveling designations, and filling completion checks use dump fulfillment so completed fill orders can transition to `Done`
* Changed: farming completion now removes completed final origin designations immediately after posting the completion notification, preventing old finished designations from disturbing adjacent follow-up farming patches
* Temporarily disabled the preparation-start future rim debris cleanup mining pass while leaving cleanup removal paths intact for already-owned cleanup designations
* Preparation pass now evaluates the access ramp before the future rim debris cleanup step; the ramp gets priority on tile positioning because a ramp designation already excavates any debris on those tiles, so the rim cleanup naturally defers by skipping tiles already occupied by the ramp
* Fixed: rim alignment placement height criteria was one-sided; far-corner height was only checked against a lower bound (`targetHeight − 0.1`); each far corner is now required to be within ±0.1 tiles of `targetHeight`, preventing rim designations from being placed where terrain is significantly above the target and would require unnecessary excavation at the boundary
* Fixed: filling access ramps were re-placed on every tick because `RemoveOwnedFarmingAccessRamps` cleared `LastAccessRampRequestKey` whenever the preparation ramp set was empty (always true during filling), preventing the deduplication guard from suppressing repeat placement
* Fixed: access ramps accumulated across ticks — when the inaccessible-designation set changed (some origins advanced phase), the request key changed and a new ramp was placed without removing the existing one; existing ramps are now reserved in the no-overlap set, and each cluster is skipped when a previous-tick owned ramp that is still present and snapped toward the cluster edge (`IsSnappedTowards`) is found
* Changed: inaccessible-designation detection now propagates reachability through adjacent farming designations that share a matching-height ("non-red") edge; once any designation in a cluster is confirmed reachable via pathfinding BFS, its snapped neighbours are also marked reachable without needing a direct line of sight from the tower, reducing the number of clusters that require a dedicated access ramp

v0.4.0n | 2026-05-18
* Fixed: trucks were unconditionally released from the tower at the start of every filling tick, so when a rim alignment designation contained debris (terrain above target height) the dispatched excavators had no trucks to haul the material away; trucks are now kept assigned (or restored) for any tick where at least one rim designation has pending excavation work (`IsMiningNotFulfilled`), and released again once all rim tiles are clear
* Changed: rim alignment placement criteria no longer examines a probe tile one step further out from the rim; instead the rim tile itself must pass: for cardinal rims the 2 far corners (the edge furthest from the farming area) must be above `targetHeight − 0.1`; for diagonal corner rims the 3 far corners must be above that threshold; height tolerance changed from 0.2 to 0.1

v0.4.0m | 2026-05-17 [unreleased]
* Fixed: rim alignment designations were removed and re-placed on every filling tick via `AddOrReplaceDesignation`, cancelling haul jobs for any trucks already en route to those tiles; the designation is now re-tracked without replacement when it already exists from a previous tick
* Fixed: `TickIdleVehicleRelease` released all tower vehicles when no mining or leveling work was pending, even during an active farming fill session; vehicles are now kept assigned while any filling origin is in the `ReadyForFilling` or `Filling` phase
* Fixed: trucks released during the filling phase (to prevent them competing with dump vehicles) were permanently unassigned after filling completed; `RestoreTowerTrucksReleasedForFilling` was called without `reassign: true` on all normal exit paths (filling complete, activation abort, and user-stop), so trucks were cleared from tracking without being re-assigned to the tower
* Fixed: `AddProductToDump` was called for every farmable product on each fill activation and for every snapshot product on restore, even when the product was already in the tower's dumpable set; the redundant calls produced a `Trying to make dumpable an already dumpable product` warning each time; both paths now skip products already present in the tower's dumpable set
* Fixed: ramp generation was not skipped when a previously-placed ramp (not yet physically excavated) already provides surface access from the tower; re-scanning the area could place a second redundant ramp in a new direction; ramp generation is now skipped if any existing non-ore designation in the area has a pathable surface tile reachable from the tower
* Fixed: trucks released by ATD (for idle-release or filling) were permanently removed from the tower's vehicle list when another mod's Harmony patch silently blocked `MineTower.AssignVehicle` (e.g. gameplay-plus-plus Parking HQ committing a truck); because `AssignVehicle` is `void`, ATD had no signal the assignment was blocked and cleared its tracking, leaving the truck orphaned; both restore paths now verify the vehicle is in `AllVehicles` after the call and retain blocked vehicles in tracking for retry on the next tick

v0.4.0l | 2026-05-17
* Added **atd_get_assigned_vehicles** console command: lists every mine tower with its assigned vehicles, per-vehicle job state, ATD auto-release setting, and which vehicles ATD has released
* Fixed: filling-area vehicle evacuation issued `CancelAllJobsAndResetState` on all vehicles in the area, including those actively mining or driving; only idle vehicles (no active job) are now ordered to evacuate; vehicles with an active job leave the area naturally when their job finishes
* Fixed: `TickFarmingPreparationSessions` and `TickIdleVehicleRelease` were called from the Unity main thread while the game's simulation thread could concurrently iterate the same terrain designation set, causing an occasional `InvalidOperationException: Set changed while enumerating`; both operations now run on the simulation thread via `simLoopEvents.Update`

v0.4.0k | 2026-05-17
* Fixed: mine tower trucks were unassigned from the tower before vehicles in the fill area were ordered to evacuate; trucks are now released only after the evacuation order has been issued, so vehicles still assigned to the tower can receive a valid park-and-wait job
* Fixed: one truck was always kept assigned to the tower during filling due to a leftover guard; all empty assigned trucks are now released when filling begins
* Fixed: tiles already farmable at analysis time were set directly to the `Done` phase instead of `ReadyForFilling`; because filling only starts when at least one `ReadyForFilling` origin exists, those tiles were stuck hidden permanently with no way to trigger filling; analysis-time farmable tiles now enter `ReadyForFilling` so filling starts normally and the filling pass confirms them as `Done`

v0.4.0j | 2026-05-17 [packaged]
* Added diagonal corner rim alignment designations: when both adjacent cardinal rim designations are placed and both outward probe tiles pass the height criteria, a corner rim is placed at the diagonal position to close the corner of the fill area
* Fixed: completed farming origins (Done phase, designation restored after a previous fill cycle) were not re-hidden when a new adjacent preparation designation was placed; dirt sliding from the z tile into the z-1 preparation area caused the Done origin's terrain to drop, triggering trucks to continuously refill it while excavators dug it out again; Done origins adjacent to any active preparation designation are now re-hidden and will be restored together with the new tiles during the filling phase
* Fixed: ramp failure/truncated notifications were not cleared when **Ramp Width** was set to 0; a stale notification from a previous scan could remain on the tower after re-scanning with ramp generation disabled
* Fixed: farming preparation origins that were hidden pending filling (ReadyForFilling / Done phase) were permanently dropped from the session when an access ramp was routed through their tile; hidden origins have no active designation so the ramp placement was allowed, but the preparation pass then saw a non-leveling designation at the tile and removed the origin from tracking, causing it to never be restored at the end of the process
* Farming access ramp generation is substantially faster for large sessions:
    - Ramp candidate collection now only probes perimeter ore tiles (tiles with at least one non-ore cardinal neighbour); interior tiles that cannot produce a valid ramp exit are skipped, reducing candidate generation work by roughly 7× for large farming areas
    - Pathability update is now called once per ramp placement attempt instead of once per candidate
    - Ramp-mouth reachability BFS results are cached within a placement attempt to avoid re-running identical checks
    - Reachability checks are capped at 50 per placement attempt; candidates beyond the cap fall back to the best already-checked position
    - BFS search margin reduced from 96 to 48 tiles and tile visit cap reduced from 250 000 to 20 000

v0.4.0i | 2026-05-16
* Added **atd_set_auto_release_vehicles_when_idle** console command to set the global default for the **Auto-release when idle** toggle; also added **AutoReleaseWhenIdle** to the **atd_get_settings** output
* Fixed: vehicles released by **Auto-release when idle** were not re-assigned to the tower before saving, so after a save/load the tower had no record of them and could not reclaim them when excavation work resumed

v0.4.0h | 2026-05-16
* Removed ramp warning icon from the **Terrain Designations** panel; ramp outcome warnings are now shown through the vanilla notification system

v0.4.0g | 2026-05-16 [packaged]
* Farming automation performance pass for large auto-reactivated farming sessions:
    - Added throttling for farming access/pathability rechecks so thousand-origin sessions do not repeatedly run the expensive access scan every few ticks when designation state has not changed
    - Cached pending farming fill-area tile sets and rebuild them only when queued filling origins, shoulders, rims, or origin state changes
    - Added targeted `[ATD Farming Perf]` log markers for slow preparation, filling, access, and pending fill-area operations
    - Added preparation breakdown logging with capture/advance/access/summary/state-scan timings to diagnose remaining large-session hitches from tester saves
    - Added `tools/extract-atd-farming-perf.ps1` to extract relevant ATD farming performance rows from the newest Captain of Industry log
* Findings from tester save: initial multi-second farming preparation freezes were reduced substantially; remaining slow rows are mostly access/ramp handling on very large unreachable farming areas, not origin analysis

v0.4.0f | 2026-05-16 [packaged]
* Added **Auto-release when idle** toggle in the Terrain Designations panel: when enabled for a tower, all excavators and trucks are unassigned while none of the tower's managed mining or leveling designations have pending excavation work; released vehicles are tracked and automatically re-assigned when excavation work resumes
    - Global default is controlled by **autoReleaseVehiclesWhenIdle** in ATDsettings.json (default: false); the per-tower toggle in the inspector overrides this default
* Added diagonal corner preparation shoulders: when both adjacent cardinal shoulders are needed, a corner shoulder is placed at the diagonal position with 3 outer corners at z-2 and the inner corner (facing the farming origin) at z-1
* Fixed: filling transition could begin before shoulder designations were fully filled; the transition is now deferred until all active shoulder designations are fulfilled
* Fixed: preparation shoulders were not placed on sides where the terrain drop only crosses the diagonal corner of the designation; the check now samples the center 2×2 tiles of each adjacent 4×4 area instead of the boundary strip
* Fixed: filling access ramps were placed before rim alignment designations had a chance to be built; the BFS pathability check uses actual terrain, so a pending-but-not-yet-raised rim was invisible to it, causing ramps to be routed in wrong directions (e.g. into the sea)
    - Filling ramp placement is now deferred until the current tick places no new rim alignment designations, giving the rim terrain a chance to reach target height first
    - Stale filling ramps are now removed as soon as the fill area becomes pathable, rather than waiting until filling fully completes
* Vehicle clear-out before filling transition now also evicts vehicles from shoulder designation tiles, not only from the fill area itself

v0.4.0e | 2026-05-15
* Added **rampNotificationsEnabled** global setting: suppresses ramp access warning notifications (Failed, Truncated, NotAccessible) on all towers; toggle in **ATDsettings.json** or with **atd_set_ramp_notifications on|off**
* Added **farmingPanelCollapsed** global setting: the **Farmland Preparation** inspector panel now starts collapsed by default; toggle in **ATDsettings.json** or with **atd_set_farming_panel_collapsed on|off**

v0.4.0d | 2026-05-15 [packaged]
* When multiple farming designation clusters are inaccessible at the same time, access ramps are now placed for all clusters in the same tick instead of one per tick; applies to both preparation and filling phases
* Fixed: access ramps could overlap previously placed ramps from the same or another session, or land on any other existing designation
* Fixed: shoulder designations could be placed on hidden (completed) farming origins belonging to another tower's session, potentially corrupting its designation tracking
* Fixed: rim alignment designations could be placed on farming origins belonging to another tower's session
* Ramp generation: removed the half-space direction filter so all four cardinal directions are evaluated as ramp candidates; scoring already ranks by alignment toward the tower, so preferred directions are still tried first while directions away from the tower serve as fallbacks when the preferred corridor is blocked by a building, debris, or void
* Ramp placement now checks vehicle accessibility before committing a candidate; accessible candidates are preferred; if no accessible candidate exists the best available is placed and reported as not accessible
* Farming access ramp failures now show a warning notification on the tower, matching mining ramp notification behavior; the notification is cleared when all farming designations become accessible
* Fixed: German translation of ore mining priority tooltip
* Farming session status messages (diagnostic output from commands such as **atd_farming_dump_all_towers** and the status line returned by farming automation commands) are no longer localized and always display in English; panel labels and tooltips in the **Farmland Preparation** inspector are unaffected

v0.4.0c | 2026-05-15
* Improved ramp generation resilience: when the primary (dominant-axis) direction is blocked, the ramp now falls back to any other direction pointing toward the tower, tried in order of alignment angle

v0.4.0b | 2026-05-14
* Fixed: preparation-phase access ramp was not created for the last remaining preparation tile when all neighbouring tiles had already finished, because done origins were still blocking ramp entry positions

v0.4.0a | 2026-05-14
* Fixed: after farmland filling completes, flat level designations are now placed at rim tiles adjacent to the filled area where the surrounding terrain matches the fill target height, preventing V-shaped pits left by preparation-phase access ramps
    - Rim designations are placed before the filling access ramp is evaluated, so the ramp avoids tiles already covered by a rim designation
    - Rim designations are removed as soon as all fill origins reach Done, before the stabilization period, to prevent them from being picked up as new farming work


v0.4.1 | 2026-05-14
* Revised Swedish, German, and Russian translations — terminology aligned with base game wording and reviewed for accuracy across all panels, tooltips, and status messages


v0.4.0 | 2026-05-14
* Added farmland preparation automation for mine towers
    - Enabled via the **Farmland Preparation Automation** checkbox in the **Farmland Preparation** inspector panel
    - Automatically re-enables on load for towers that were running preparation work; toggle with **reEnableFarmingOnLoad** in **ATDsettings.json** or **atd_set_re_enable_farming_on_load** / **atd_re_enable_farming_on_load**
    - Clears out vehicles from the fill area before committing final fill designations to reduce the chance of vehicles getting trapped
    - Adds temporary sloped support shoulders on exposed sides near steep drops or ocean edges to reduce dirt spill-off during final filling
    - Added a green one-time completion notification when preparation and filling are done
    - Detailed per-tower diagnostics available via **atd_farming_dump_all_towers**
* Added localization support for the **Terrain Designations**, **Farmland Preparation**, and **Ore Composition** panels and related tooltips and status text
    - Includes English, Swedish, German, and Russian translations under **translations/**
* Added a green one-time notification when any vehicle depot completes an excavator; toggle with **excavatorCompletionNotifications** in **ATDsettings.json** or **atd_set_excavator_completion_notifications**
* Changed the excavator completion notification to use the mining toolbar icon
* Improved **ATDsettings.json** migration so defaults from older releases are upgraded to current defaults while user-changed values are preserved


v0.3.1 | 2026-05-09
* Ramp generation warnings now shown as a yellow icon next to the **Create Designations** button (tooltip contains the message):
    - "Ramp generation failed — no valid path found."
    - "Ramp placed but did not reach the surface — excavators may not be able to excavate."
    - "Couldn't find a valid path from the tower to the generated ramp. Check for access problems."
* Increased generated ramp safety margin around nearby buildings by 1 tile. Landslide can still reach buildings if built on deep dirt/sand.
* Added a bottom-flattening pass to reduce rough/bumpy excavation floors; designations floor should now be noticeably flatter. Can be toggled on/off with **atd_set_bottom_flattening on/off**.
* Tuned default **Low** and **Med** ore purity thresholds to reduce the jump from **Off**; existing **ATDsettings.json** values are preserved. Added **atd_reset_to_defaults** to try built-in defaults in memory.
* Renamed the **Target product** label to **Scanning filter** to avoid confusion with mining priority.


v0.3.0 | 2026-05-08
* Corner designations can now be placed manually from within any terrain designation tool (**M**, **Z**, **N**) — use the new **▲**/**▽** toolbar buttons or press **K** (**K** again flips between outer/inner); supports height offset (**Q**/**E**), rotation (**R**), and auto-snap to neighbouring designations; vanilla designations also snap to corner designations
    - Corner mode exits automatically when chosing one of the vanilla modes (flat/ramp) or the designation tool is deactivated
* Better quality thumbnail


v0.2.4 | 2026-05-04
* Settings file renamed from **settings.json** to **ATDsettings.json** to avoid conflicts with other mods
* Existing **settings.json** files are still read as a legacy fallback and migrated into the generated **ATDsettings.json** format
* **ATDsettings.json** is no longer distributed inside the mod ZIP — it is generated automatically in the mod folder on first run, populated with the current defaults and inline documentation
* Settings file now contains a **settingsVersion** stamp; when the mod version advances the file is automatically migrated (user values are preserved and any new keys are added)
* Added **atd_save_settings** console command to write the current in-memory global defaults back to **ATDsettings.json** at any time
* Added global defaults for whether the Terrain Designations and Ore Composition panels start expanded or collapsed, configurable via **ATDsettings.json** or console commands.
* Fixed Corridor clearance modifier-click handling so Shift/Ctrl jumps directly between Off and 2 instead of requiring multiple clicks.

v0.2.3 | 2026-05-03
* Added experimental Remove Debris scan support, available as a dedicated action and as an auto fallback when no useful product is found.

v0.2.2 | 2026-05-03
* Added concise license and attribution notices to source files and README.

v0.2.1 | 2026-05-03
* Added the mod marker/version tooltip to both inspector panels.
* Made the Ore Composition panel explicitly collapsible again while keeping it open by default.
* Added horizontal scrolling to Ore Composition cards so towers with many ore products no longer overflow the inspector column.
* Fixed the Ore Composition panel so it can populate for custom IAreaManagingTower implementations; excavator priority controls remain limited to vanilla mine towers.
* Fixed clearing terrain designations so it only removes mining designations and preserves other designation types such as forestry. Placing mining designations will still overwrite other designations.

v0.2.0 | 2026-05-01
* Changed project license to MIT and added a repository LICENSE file.
* Added short MIT/SPDX license annotations to source files.

v0.1.15 | 2026-05-01
* Fixed incorrect trimming of poor ore near the bottom of the designation.
* Changed the label Override product to Target product to make it clearer.
* Added a per-tower Corridor clearance setting for separated ore components and passability.
* Added in-game console commands for live tuning of all ATD global defaults.
* Updated the API with initialization checks, per-tower settings accessors, panel builder methods, and ramp-width driven designation creation.

v0.1.14 | 2026-05-01
* Improved designation logic to reduce the risk of vehicles getting stuck.
* Connectivity now uses 2-tile-wide designation corridors for mega-vehicle passability.
* Enclosed interior holes inside designation regions are now automatically filled.
* Single-tile pinch points inherited from the ore scan are now widened.

v0.1.13 | 2026-05-01
* Fixed an issue with settings.json not parsing in some locales.

v0.1.12 | 2026-05-01
* Tower settings are now persistent throughout a session.
* Excavator priority is now a sticky state per tower and can be reset to None.
* Ore Purity OFF is now more aggressive when sweeping up ore.
* Externalized global default parameters to settings.json.
* Fixed an invalid settings.json path issue.

v0.1.11 | 2026-05-01
* The Ore Composition panel now refreshes automatically after creating designations.
* Terrain Designations panel is now collapsible.
* Added the Ore Purity Level preset setting and externalized its thresholds to settings.json.
* Ramp generation now tries to avoid all buildings.

v0.1.10 | 2026-05-01
* Improved ramp generation with selectable ramp width, better z-level targeting, better attachment to designation edges, and tower avoidance.
* Added buttons on ore composition cards to set mining priority for all tower excavators.
* Overhauled the UI with a more prominent Create Designations button and visually tuned ore composition cards.

v0.1.9 | 2026-05-01
* Fixed broken non-tower inspectors.
* Fixed stale Ore Composition data after switching inspectors.
* Made the Ore Composition panel always visible and replaced auto-refresh with a manual scan button.
* Ore Composition panel now clears when switching to a different tower inspector.

v0.1.8 | 2026-05-01
* Ore Composition panel now refreshes correctly when switching tower inspectors.
* Ore Composition now counts only material above the designation target level.
* Dumping designations are excluded from the Ore Composition scan.
* Redesigned Ore Composition cards with proportional color-coded progress bars and percentage shares.

v0.1.7 | 2026-05-01
* Added the Ore Composition panel for the mine tower inspector.
* Ore Composition quantities reflect the current Ore Mining Yield difficulty setting.
* Ore Composition panel refreshes automatically when designations change.
* Fixed a mod package extraction issue on Linux.

v0.1.6 | 2026-05-01
* Added Max layers to excavate and Max depth settings.
* Clearing designations is now instantaneous regardless of area size.
* Bedrock is now always excluded from terrain scans.
* Added the AutoTerrainDesignationsApi integration API.

v0.1.5 | 2026-05-01
* Adjusted max slope to 1 to prevent dead spots.

v0.1.4 | 2026-05-01
* Moved UI controls to their own panel for better compatibility and robustness.
* Updated the placement algorithm to reduce the risk of excavators getting stuck.

v0.1.3 | 2026-05-01
* Fixed UI collision by changing from text-based buttons to icon-based buttons.
* Added scanning for any mineable product through the product selector.
* Improved scanning, particularly along deposit edges.
* Added thumbnail.

v0.1.0 | 2026-05-01
* Initial release.
