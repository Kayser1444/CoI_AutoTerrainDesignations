# Roadmap

Planned and candidate improvements for Kayser's Automatic Terrain Designations.

# Planned
* Cut/Copy/Paste/Blueprint designations?
* rail incline (12.5%) designations

## Accessways and pathfinding
Domain pruning: envelope
Harmonize V1 and V2

Remaining work: reuse the material-aware disturbance ray tracer and tower-owned harvest-marker tracking when generating ordinary mine designations. Keep direct building footprint/clearance checks separate from terrain-disturbance prediction. When either avoidance option is disabled, allow the plan but warn when the corresponding projected hazard is detected.

## Generated mining-body vehicle clearance — future refinement

Rare generated mining bodies can contain a one-origin waist that is too narrow for Mega/T3 vehicles. Add a clearance-aware minimal-change widening/removal pass eventually; this is not a blocker for V2 accessway rollout.

## Construction assist
Handle overlapping towers?
Auto-prepare ground anywhere
- If farm placed outside mining tower area -> warn that global filling rules must be adjusted
- Handle case where no storage exports soil -> warn, force-export, or alter truck behavior
- Not supported yet: Gas injection pump (requires 6 levels of limestone)

## Make Create Designations consider possible farming work

## Issue?: Vehichles auto-released can be assigned to the tower again (or to something else)
Maybe reverse meaning and make auto-assign instead of auto-unassign?

## Saddle designation?

### Long term: Support underground pipe construction both for modded and unmodded games
* Highly complicated if unmodded: must dig trench, place pipes, build pipes, prepare remaining ground, place rest of BP.

## Topsoil Optimization
Investigate placing only the minimum required soil to satisfy farmability (e.g., 95% thickness) instead of a full topsoil band. Deferred during the access framework rewrite.

## Concurrency issue in TickIdleVehicleRelease
`AutoDepthDesignation.TickIdleVehicleRelease()` runs on the simulation thread (`~Sim` thread) and iterates over active towers using `m_entitiesManager.GetAllEntitiesOfType<MineTower>()`. This can collide with the Unity main thread (`~Mai` thread) when other UI elements (like `PollutionWorldRenderer` or other mods) try to enumerate entities of any type concurrently using MaFi's non-thread-safe `LystMutableDuringIter` collections, triggering `Outer enumerator finished first?` assertions in the game's log.

**Solution:** Query and snapshot the entities safely on a main-thread tick or copy them to avoid concurrent enumeration.
