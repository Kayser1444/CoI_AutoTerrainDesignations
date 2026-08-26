# Vanilla Captain of Industry tutorial goals

Investigation of the local decompiled Captain of Industry source, captured on
2026-08-18. This is a source report, not a claim about behavior verified in a
running game.

## Scope and source selection

The goal definitions are registered by `GoalsData.RegisterData` in:

`C:\Users\jonas.adolphson\AppData\Roaming\Captain of Industry\Mafi\Mafi.Base\Mafi\Base\Prototypes\Messages\GoalsData.cs`

The runtime implementations used below are in the newer decompile tree:

`C:\Users\jonas.adolphson\AppData\Roaming\Captain of Industry\Mafi\Mafi.Core\Mafi\Core\Messages\Goals\`

`GoalsData.cs` retains a compatibility note: removed goal lists should remain
as empty/obsolete stubs, removed goals should remain in their list and be
marked obsolete, and triggers are not saved. See
`GoalsData.cs:156-174` (absolute path above). The decompile also contains a
flattened `Mafi.Core.Messages.Goals` copy dated 2026-04-03 and a nested copy
dated 2026-08-13. The nested copy is used here because it is the newer output
and contains the current runtime classes; the flattened copy may describe an
older game build.

## Model and global runtime rules

* A list prototype has an ID, an ordered array of goal prototypes, one trigger
  data object, rewards, a long-term flag, and an optional exclusion from the
  global full-completion check. `GoalListProto` constructs the localized list
  title from the ID and title string (`...\\GoalListProto.cs:8-31`).
* `goalId("X")` produces the list ID `Goal__X`
  (`...\\GoalsData.cs:456-459`). Individual `GoalProto` constructors prepend
  `Goal_` to the goal ID (`...\\GoalProto.cs:9-39`).
* New games instantiate every non-obsolete list into a waiting-for-activation
  collection (`...\\GoalsManager.cs:145-153`). A list becomes active only when
  its trigger calls `GoalListTrigger.ActivateGoal`, which destroys the trigger,
  calls `GoalsManager.ActivateGoal`, shows any configured tutorial messages,
  performs an immediate update, and records activation time
  (`...\\GoalListTrigger.cs:19-39`; `...\\GoalsManager.cs:195-214`).
* Each active list is updated from the simulation loop. The manager updates
  roughly one tenth of the active lists per simulation update, removes a
  completed list, emits `OnGoalFinished`, and logs completion
  (`...\\GoalsManager.cs:159-193`). A goal update calls `UpdateInternal` only
  until it first returns true; completion then calls `Destroy`
  (`...\\Goal.cs:31-47`).
* A list is complete only when every instantiated, non-obsolete goal is
  complete (`...\\GoalsList.cs:101-125`). A goal with `LockedByIndex >= 0` is
  initially locked and is unlocked when the goal at that runtime list index is
  complete (`...\\GoalsList.cs:49-59`, `:82-93`, `:114-122`). The source
  stores the index in the prototype and does not rewrite it after obsolete
  goals are removed; the authored indices below are therefore shown exactly,
  with an explicit note where obsolete filtering shifts the effective list.
* A goal being locked does not stop `GoalsList.Update` from calling its
  `UpdateInternal`; the lock is presentation/state gating, not an execution
  guard. This is a notable source-level behavior: `GoalsList.Update` updates
  every goal before it processes lock transitions (`...\\GoalsList.cs:101-123`).
* When a completed list has rewards, it is placed in `CompletedGoals`; rewards
  are not delivered at completion. The player-facing completion command later
  stores each reward product as loot and removes the completed list
  (`...\\GoalsManager.cs:184-193`, `:227-250`). A skipped active list emits the
  finish event but is not placed in the reward queue (`...\\GoalsManager.cs:216-226`).
* Tutorial messages attached to goals are unlocked on list activation. The
  modes are `DoNotUnlock`, `UnlockSilently`, and `UnlockAndNotify`
  (`...\\GoalProto.cs:11-20`; `...\\GoalsManager.cs:202-209`).
* `GoalEntityTracker<T>` initially includes only constructed entities, adds
  entities on construction, removes them when deconstruction starts, and
  tracks upgrades (`...\\GoalEntityTracker.cs:13-88`). Consequently, the
  construction goals below count constructed entities, not merely planned or
  under-construction entities.
* `GoalsManager.GetAreGoalsCompleted()` returns true when no active goals remain
  and every still-waiting list is either gone or marked
  `IgnoreFromFullCompletionList`; this is why `PauseBeacon` is excluded
  (`...\\GoalsManager.cs:91-121`; `GoalsData.cs:450-453`).

## Trigger implementations

The trigger shown for each list below is the activation/dependency condition,
not a goal completion condition.

| Trigger | Concrete rule |
| --- | --- |
| `OnMessageDelivered(message)` | Subscribes to `MessagesManager.OnNewMessage`; activates when a delivered message has the requested prototype ID (`...\\GoalsListTriggerOnMessageDelivered.cs:40-60`). |
| `OnGoalListDone(list)` | Activates when the named list emits `OnGoalFinished`. The one-list constructor uses `AnySatisfied`; the multi-list form accepts `AllSatisfied` or `AnySatisfied` (`...\\GoalsListTriggerOnGoalListDone.cs:17-73`). |
| `OnGoalsOrProductLow(list, product < quantity)` | Activates when the named dependency finishes, or at a day end after `RelGameDate.TotalMonthsFloored > 1` when stored-available product quantity is below the threshold (`...\\GoalsListTriggerOnGoalsOrProductLow.cs:71-104`). |
| `ForFarm(after, farm count, food)` | At day end, removes itself if constructed farm count is already at least the configured count; otherwise activates when `MonthsOfFood <= 20` and the dependency appears in completed goals (`...\\GoalsListTriggerForFarm.cs:53-80`). |
| `ToPauseBeacon` | At day end, removes itself once total population reaches 300; otherwise activates when free workers exceed 70, a beacon exists, and the beacon has made positive progress (`...\\GoalsListTriggerToPauseBeacon.cs:44-70`). |

## Goal implementation catalog

The inventory uses the following concrete predicates. The source line numbers
are in the newer nested runtime tree unless the path says `Mafi.Base`.

| Kind | Completion predicate |
| --- | --- |
| `GoalToConstructStaticEntity` | For every requested `(StaticEntityProto, count)` slot, at least `count` constructed matching entities must exist. A requested proto's immediate upgrade tier also matches. All slots are required (`...\\GoalToConstructStaticEntity.cs:274-310`; tracker behavior above). |
| `GoalToConstructVehicle` | The world count of the requested vehicle prototype must be at least `startingFactoryCount + NumberToOwnSinceStart`. Starting trucks, excavators, and tree harvesters are therefore not counted as newly required vehicles (`...\\Mafi.Base\\Mafi\\Base\\Prototypes\\Messages\\GoalToConstructVehicle.cs:92-125`). |
| `GoalToConstructNumberOfStaticEntities` | The constructed count across the listed static-entity IDs must reach `EntitiesCount` (`...\\GoalToConstructNumberOfStaticEntities.cs:68-86`). |
| `GoalToResearchNode` | The requested research node must be `Researched`. If the node cannot be found, the implementation logs an error and returns true, treating the missing node as complete (`...\\GoalToResearchNode.cs:61-79`). |
| `GoalToActivateRecipe` | At least `EntitiesCount` constructed machines of the specified machine/recipe pairs must have the specified recipe in `RecipesAssigned` (`...\\GoalToActivateRecipe.cs:122-149`). The two-pair form allows either machine type. |
| `GoalToReachProductStatsValue` | The selected `ProductStats` lifetime value must reach the required quantity. With no selector this is `CreatedByProduction.Lifetime`; data can instead select mining, dumping, import, deconstruction, quick-trade, processing, or total-use statistics (`...\\GoalToReachProductStatsValue.cs:40-54`, `:122-132`; selectors are supplied in `GoalsData.cs`). |
| `GoalToAssignTrucksToTreeHarvester` | The sum of `AllVehicles.Count` across tree harvesters must reach the requested assigned-truck count (`...\\GoalToAssignTrucksToTreeHarvester.cs:54-63`). |
| `GoalToSetupDumping` | `ITerrainDumpingManager.HasEligibleDumpingDesignationsFor(looseProduct)` is true (`...\\GoalToSetupDumping.cs:54-57`). |
| `GoalToSetupMining` | A constructed mine tower has at least one excavator and two trucks, and one of every second managed designation examined has a matching mined product in the first layer, or in an exposed second/third layer according to the thickness tests (`...\\GoalToSetupMining.cs:79-123`). |
| `GoalToBuildStorage` | A constructed storage of the requested storage prototype stores the requested product and satisfies every enabled flag: positive import slider, export slider below 100%, and/or logistics input disabled (`...\\GoalToBuildStorage.cs:83-101`). |
| `GoalToStockpileProducts` | Sum `CurrentQuantity` over all constructed storages containing the requested product; complete when the sum reaches the target (`...\\GoalToStockpileProducts.cs:76-96`). |
| `GoalToBuildHousing` | The largest settlement's count of constructed housing modules reaches the target (`...\\GoalToBuildHousing.cs:60-69`). |
| `GoalToReachRefugees` | `SettlementsManager.NewPopsFromAdoptions.Lifetime` is positive; the prototype's numeric argument is not used by `UpdateInternal` (`...\\GoalToReachRefugees.cs:49-52`). |
| `GoalToPauseEntity` | If any matching constructed entity exists, complete when at least one is paused. If no matching entity exists, returns true (`...\\GoalToPauseEntity.cs:66-82`). |
| `GoalToActivateEdict` | The requested edict exists in `AllEdicts` and its `IsActive` flag is true (`...\\GoalToActivateEdict.cs:48-60`). |
| `GoalToConstructFuelStation` | A constructed fuel station has positive stored fuel and, when requested, at least one assigned vehicle (`...\\GoalToConstructFuelStation.cs:62-76`). |
| `GoalToRepairShip` | A traveling fleet exists and `NeedsRepair` is false. This kind is currently retained only as an obsolete data goal in `ExploreWithShip` (`...\\GoalToRepairShip.cs:44-51`; `GoalsData.cs:360`). |
| `GoalToRepairCargoShip` | Complete if there is at least one repaired unused cargo ship, or if at least one cargo ship is already in use (`...\\GoalToRepairCargoShip.cs:45-52`). |
| `GoalToRefuelShip` | A traveling fleet exists and its fuel buffer quantity reaches buffer capacity (`...\\GoalToRefuelShip.cs:46-59`). |
| `GoalToManShip` | A traveling fleet exists and current crew reaches the fleet's required crew (`...\\GoalToManShip.cs:46-59`). |
| `GoalToExploreWithShip` | More than one non-home world-map location is in the `Explored` state (`...\\GoalToExploreWithShip.cs:46-49`). |
| `GoalToDiscoverWorldMine` | Any world-map location contains a mine whose produced product matches the requested product, and, if required, that mine is repaired (`...\\GoalToDiscoverWorldMine.cs:54-57`). |

Several player-facing titles mention connecting machines, attaching modules, or
ensuring deliveries. Those extra relationships are not present in the
`GoalToConstructStaticEntity` predicate; it counts constructed prototypes only.
The storage predicate checks product assignment and the requested slider/input
state, but not a physical connection. The explicit setup goals (recipes,
mining, dumping, and fuel stations) do have their own state checks.

## Complete registered goal-list inventory

Notation: `lock=n` is the literal `LockedByIndex` argument in the prototype;
`lock=-1` means no lock. `obsolete` means the prototype is retained but
removed from the live `GoalsList` during load/initialization. Conditions use
the catalog above but include all product/statistic thresholds and special
flags so each row is concrete.

### Early production and settlement foundations

| List ID / title | Activation/dependency | Goals in authored order: ID — concrete condition; lock | Rewards |
| --- | --- | --- | --- |
| `Goal__IronProduction` — Iron production | `OnMessageDelivered(MessageWelcome)` | `Goal_BuildCoalMaker` — construct 1 charcoal maker and 1 smoke stack; -1. The title says to connect them, but this predicate only counts constructed entities. `Goal_AssignTruckToHarvester` — assign 2 trucks to tree harvesters; -1. `Goal_ProduceWood` — mining-stat lifetime wood >= 5; -1. `Goal_ProduceCoal` — production lifetime coal >= 8; 0. `Goal_BuildFurnace` — construct 1 smelting furnace, 2 casters, and 1 smoke stack; -1. `Goal_ProduceScrap` — deconstruction-stat lifetime iron scrap >= 10; 3; obsolete. `Goal_ProduceIron` — production lifetime iron >= 16; 3. | 50 diesel; 50 construction parts; 30 vehicle parts. |
| `Goal__FoodProduction` — Food production | `OnMessageDelivered(MessageWelcome)` | `Goal_ResearchFarming` — research Basic Farming; -1; obsolete. `Goal_BuildFarm` — construct 1 farm on fertile grass; -1. `Goal_ProduceFood` — production lifetime potatoes >= 20; 0. | 40 potatoes; 20 construction parts. |
| `Goal__CpIProduction` — Construction parts | `OnGoalListDone(Goal__IronProduction)` | `Goal_BuildPowerGen` — construct 1 diesel generator; -1. `Goal_BuildResearchLab` — construct 1 research lab I; -1. `Goal_ResearchCp1Production` — research CP I packing; 1. `Goal_BuildAssembly` — construct 1 manual assembly; 2. `Goal_SelectCpRecipe` — assign CP assembly recipe on 1 qualifying assembly; 3. `Goal_ProduceCPs` — production lifetime construction parts >= 8; 4. | 50 wood; 60 vehicle parts; 30 construction parts. |
| `Goal__WasteDumping` — Waste dumping | `OnGoalListDone(Goal__IronProduction)` | `Goal_BuildSettlementWasteModule` — construct the settlement landfill module; -1. `Goal_SetupWasteDumpDesignations` — have an eligible waste dumping designation; -1. `Goal_DumpWaste` — dumping-stat lifetime waste >= 20; 0. | 80 concrete slabs; 50 construction parts. |
| `Goal__Maintenance` — Maintenance | `OnGoalListDone(Goal__CpIProduction)` | `Goal_ResearchPowerMaintenance` — research Power and Maintenance; -1. `Goal_BuildMaintenanceDepot` — construct 1 maintenance depot; 0. `Goal_MaintenanceAssembly` — assign the iron mechanical-parts recipe on a qualifying manual/electrified assembly; -1. `Goal_ProduceMaintenance` — production lifetime maintenance I >= 100; 2. | 40 electronics; 50 construction parts. |
| `Goal__SetupTradings` — Trading | `OnGoalListDone(Goal__FoodProduction OR Goal__IronProduction)`, any | `Goal_ResearchTradeDock` — research Trade Dock; -1. `Goal_BuildTradeDock` — construct 1 trade dock; 0. `Goal_BuildWoodStorage` — construct/assign wood storage; -1; obsolete. `Goal_FillWoodStorage` — stockpile 40 wood in storage; -1; obsolete. `Goal_TradeForBricks` — quick-trade lifetime concrete slabs >= 20; 1. | 80 concrete slabs; 60 construction parts. |
| `Goal__IronOreMining` — Iron ore mining | `OnGoalListDone(Goal__Maintenance)`, all | `Goal_ResearchMining` — research Vehicle and Mining; -1. `Goal_BuildVehicleDepot` — construct 1 vehicle depot; 0. `Goal_ConstructExcavator` — own 1 new T1 excavator beyond starting count; 0. `Goal_ConstructTruck` — own 2 new T1 trucks beyond starting count; 0. `Goal_SetupIronMine` — tower with >=1 excavator, >=2 trucks, and a qualifying iron-ore managed designation; 1. `Goal_MineIronOre` — mining-stat lifetime iron ore >= 10; 4. `Goal_OreSortingPlant` — construct 1 ore sorting plant; 2. | 50 electronics; 100 construction parts; 50 vehicle parts. |
| `Goal__ProcessIronOre` — Processing iron ore | `OnGoalListDone(Goal__Maintenance)`, all | `Goal_ActivateIronOreRecipe` — assign the coal iron-smelting recipe to 1 furnace; -1. `Goal_ProcessIronOre` — total-use lifetime iron ore >= 24; 0. `Goal_SetupSlagDumpDesignations` — have an eligible slag dumping designation; 0. `Goal_DumpSlag` — dumping-stat lifetime slag >= 20; 1. | 80 diesel; 60 vehicle parts. |
| `Goal__StockpileProducts` — Stockpile products | `OnGoalListDone(Goal__IronOreMining)` | `Goal_AnotherCpAssembly` — assign CP assembly recipe on 2 qualifying assemblies; -1. `Goal_BuildIronStorage` — construct/assign iron storage; -1. `Goal_BuildCpStorage` — construct/assign construction-parts storage; -1. `Goal_FillIronStorage` — sum current iron in matching storages >= min(80, storage capacity); 1. `Goal_FillCpStorage` — sum current construction parts in matching storages >= min(20, storage capacity); 2. | 100 wood; 40 electronics. |
| `Goal__PopulationGrowth` — Population growth | `OnGoalListDone(Goal__Maintenance AND Goal__FoodProduction)`, all | `Goal_ResearchBeacon` — research Beacon; -1. `Goal_ConstructBeacon` — construct 1 beacon; 0. `Goal_BuildPowerGen2` — construct 1 diesel generator; -1; obsolete. `Goal_BuildHousing` — construct 3 housing modules in the largest settlement; -1. `Goal_GetRefugees` — adoption/refugee lifetime count becomes positive; -1. | 60 potatoes; 40 vehicle parts. |

### Diesel, ship, and intermediate production

| List ID / title | Activation/dependency | Goals in authored order: ID — concrete condition; lock | Rewards |
| --- | --- | --- | --- |
| `Goal__SetupDiesel` — Diesel production | `OnGoalsOrProductLow(Goal__PopulationGrowth OR diesel < 100)`: dependency completion is immediate; low-stock branch is checked at day end only after month 1 | `Goal_ResearchBasicDiesel` — research Basic Diesel; -1. `Goal_BuildOilPump` — construct 2 oil pumps and 1 basic diesel distiller; 0. `Goal_BuildLiquidDump` — assign the waste-water dumping recipe on 1 waste dump; 0. `Goal_ProduceDiesel` — production lifetime diesel >= 16; 0. `Goal_DumpWasteWater` — dumping-stat lifetime waste water >= 8; 0. | 50 construction parts; 60 concrete slabs. |
| `Goal__StockpileDiesel` — Stockpile diesel | `OnGoalListDone(Goal__SetupDiesel)` | `Goal_BuildDieselStorage` — construct/assign a fluid storage for diesel; -1. `Goal_DisableImportToDieselStorage` — matching diesel fluid storage has logistics input disabled; 0. | 60 diesel; 60 copper. |
| `Goal__SetupVehicleParts` — Vehicle parts | `OnGoalListDone(Goal__ProcessIronOre AND Goal__SetupDiesel)`, all | `Goal_ElAssembly` — assign electronics assembly recipe on a qualifying assembly; -1. `Goal_VehPartsAssembly` — assign vehicle-parts assembly recipe on a qualifying assembly; -1. `Goal_ProduceElectronics` — production lifetime electronics >= 8; 0. `Goal_ProduceVehicleParts` — production lifetime vehicle parts >= 8; 1. | 80 copper; 40 construction parts. |
| `Goal__SetupBricks` — Concrete production | `OnGoalListDone(Goal__ProcessIronOre)` | `Goal_ResearchBricksProduction` — research Bricks Production; -1. `Goal_MineLimestone` — mining-stat lifetime limestone >= 20; -1. `Goal_BuildBricksMaker` — construct 1 bricks maker and 1 rainwater harvester; 0. `Goal_ProduceBricks` — production lifetime concrete slabs >= 8; 2. | 80 concrete slabs; 60 vehicle parts. |
| `Goal__ExploreWithShip` — Repair shipyard and set sail | `OnGoalListDone(Goal__StockpileDiesel)` | `Goal_ResearchShipDockRepair` — research Ship Dock Repair; -1. `Goal_RepairShipyard` — construct 1 advanced shipyard; 0. `Goal_RepairShip` — fleet exists and does not need repair; 1; obsolete. `Goal_RefuelShip` — traveling fleet fuel buffer reaches capacity; 1. `Goal_LoadCrewOnShip` — traveling fleet crew reaches required crew; 1. `Goal_ExploreFirstLocations` — more than one non-home map location is explored; 3. | 100 concrete slabs; 50 construction parts; 50 diesel. |
| `Goal__FoodProduction2` — Food production II | `ForFarm(after Goal__FoodProduction, stop once 2 constructed farms exist)`; otherwise day-end activation at `MonthsOfFood <= 20` | `Goal_BuildFarm2` — have 2 farms constructed in total; -1. | 40 potatoes. |
| `Goal__RubberProduction` — Synthetic rubber | `OnGoalListDone(Goal__SetupBricks)` | `Goal_ResearchRubber` — research Rubber Production; -1. `Goal_BuildRubberMaker` — construct 1 vacuum distillation tower; 0. `Goal_ProduceRubber` — production lifetime rubber >= 8; 1. | 50 electronics; 80 concrete slabs. |
| `Goal__CopperProduction` — Copper production | `OnGoalListDone(Goal__SetupBricks)` | `Goal_ResearchCopper` — research Copper Refinement; -1. `Goal_MineCopperOre` — mining-stat lifetime copper ore >= 20; -1. `Goal_ActivateCopperOreRecipe` — assign copper smelting recipe on 1 furnace; 0. `Goal_BuildCopperElectrolysis` — construct 1 copper electrolysis and 2 rainwater harvesters; 0. `Goal_ProduceCopper` — production lifetime copper >= 10; 2. | 80 cement; 50 concrete slabs. |
| `Goal__SetupCp2` — Construction II | `OnGoalListDone(Goal__CopperProduction AND Goal__RubberProduction)`, all | `Goal_ResearchCp2` — research CP II packing; -1. `Goal_ProduceCp2` — production lifetime construction parts II >= 8; 0. | 50 construction parts; 60 concrete slabs. |
| `Goal__MineCoal` — Mine coal | `OnGoalListDone(Goal__CopperProduction)` | `Goal_ConstructExcavator2` — own 2 new T1 excavators beyond starting count; -1. `Goal_ConstructTruck2` — own 4 new T1 trucks beyond starting count; -1. `Goal_MineCoal` — mining-stat lifetime coal >= 20; -1. `Goal_PauseCoalMaker` — at least one matching constructed charcoal maker is paused, or no matching maker exists; 2. | 80 cement; 80 vehicle parts; 100 diesel. |

### Logistics, settlement services, and late-game goals

| List ID / title | Activation/dependency | Goals in authored order: ID — concrete condition; lock | Rewards |
| --- | --- | --- | --- |
| `Goal__ConveyorBelts` — Set up conveyor belts | `OnGoalListDone(Goal__SetupCp2)`, all | `Goal_ResearchConveyors` — research Conveyor Belts; -1. `Goal_BuildConveyorBelts` — construct at least 2 entities whose IDs are flat or loose-material conveyors; 0. | 80 construction parts II. |
| `Goal__FuelStation` — Fuel station | `OnGoalListDone(Goal__SetupCp2)`, all | `Goal_ResearchFuelStation` — research Fuel Station; -1. `Goal_BuildFuelStation` — construct a fuel station with positive stored fuel; 0. `Goal_AssignTruckToFuelStation` — construct a fuel station with positive stored fuel and at least one assigned vehicle; 1. | 60 construction parts II; 80 diesel. |
| `Goal__BuildSlagStorage` — Improve slag export | `OnGoalListDone(Goal__ConveyorBelts)` | `Goal_ResearchStorage` — research Storage I; -1; obsolete. `Goal_BuildSlagStorage` — construct/assign a loose-material storage for slag; -1. `Goal_ExportSlag` — matching storage has slag and export slider below 100%; 0. | 40 construction parts II; 60 diesel. |
| `Goal__SettlementWater` — Water for the settlement | `OnGoalListDone(Goal__ConveyorBelts)` | `Goal_ResearchSettlementWater` — research Settlement Water; -1. `Goal_BuildSettlementWater` — construct 1 settlement water module; 0. `Goal_BuildSettlementWaterPump` — construct 1 land water pump; 0. `Goal_BuildSettlementWaterDump` — construct 1 waste dump; 0. | 80 construction parts II. |
| `Goal__BuildCaptainOffice` — Captain office; long-term | `OnGoalListDone(Goal__SettlementWater)` | `Goal_BuildCaptainOffice` — construct 1 Captain's Office; -1. `Goal_ActivateFirstEdict` — the Fuel Reduction edict is active; -1. | 80 construction parts II. |
| `Goal__BuildResearchLab2` — Advanced research; long-term | `OnGoalListDone(Goal__SettlementWater)` | `Goal_BuildResearchLabT2` — construct/upgrade to 1 Research Lab II; -1. `Goal_ProduceLabEquipment` — production lifetime lab equipment >= 8; -1. | 80 construction parts II. |
| `Goal__DiscoverOilRig` — Find oil rig; long-term | `OnGoalListDone(Goal__BuildResearchLab2 AND Goal__ExploreWithShip)`, all | `Goal_DiscoverOilRig` — discover a world-map mine producing crude oil; -1. | 80 construction parts II; 80 diesel. |
| `Goal__PowerFromCoal` — Coal power plant | `OnGoalListDone(Goal__BuildResearchLab2)` | `Goal_ResearchLooseStorage` — research Storage I; -1; obsolete. `Goal_ResearchPowerGen2` — research Power Generation II; -1. `Goal_BuildBoiler` — construct 1 coal boiler and 1 land water pump; 0. `Goal_BuildCoalStorage` — construct/assign a loose-material coal storage; 1. `Goal_ImportIntoCoalStorage` — same coal storage has a positive import slider; 2. `Goal_BuildTurbine` — construct 1 high-pressure steam turbine and 2 power generators; 3. | 80 construction parts II; 40 vehicle parts. |
| `Goal__AdvancedDiesel` — Advanced oil processing; long-term | `OnGoalListDone(Goal__DiscoverOilRig)` | `Goal_ResearchAdvancedDiesel` — research Crude Oil Distillation; -1. `Goal_BuildDistillationTower` — construct 1 distillation tower I; 0. `Goal_UseMediumOil` — total-use lifetime medium oil >= 16; 1. `Goal_BurnLightOil` — dumping-stat lifetime light oil >= 4; 1. `Goal_BurnHeavyOil` — dumping-stat lifetime heavy oil >= 4; 1. `Goal_DumpSourWater` — dumping-stat lifetime sour water >= 6; 1. | 80 construction parts II. |
| `Goal__CrudeOilImport` — Automate crude oil import; long-term | `OnGoalListDone(Goal__AdvancedDiesel)` | `Goal_RepairCargoShip` — at least one repaired unused cargo ship exists, or a cargo ship is already in use; -1. `Goal_RepairOilRig` — discover a crude-oil world mine and require it to be repaired; -1. `Goal_BuildFluidModules` — construct 1 cargo depot I and 2 fluid modules; -1. `Goal_ImportCrudeOil` — import-stat lifetime crude oil >= 200; 2. | 200 construction parts; 100 construction parts II. |
| `Goal__PauseBeacon` — Reduce refugees inbound | `ToPauseBeacon`: day-end condition, not a dependency | `Goal_PauseBeacon` — at least one constructed beacon is paused, or no constructed beacon exists; -1. | None. The list is explicitly excluded from full-completion evaluation. |

## Obsolete goals and compatibility behavior

The currently authored obsolete goal prototypes are:

* `Goal_ProduceScrap` in `IronProduction`.
* `Goal_ResearchFarming` in `FoodProduction`.
* `Goal_BuildWoodStorage` and `Goal_FillWoodStorage` in `SetupTradings`.
* `Goal_BuildPowerGen2` in `PopulationGrowth`.
* `Goal_RepairShip` in `ExploreWithShip`.
* `Goal_ResearchStorage` in `BuildSlagStorage`.
* `Goal_ResearchLooseStorage` in `PowerFromCoal`.

They are still present in the prototype arrays, but `GoalsList.initSelf`
removes obsolete goal instances and removes goals no longer present in the
prototype (`...\\GoalsList.cs:82-93`). This preserves prototype/save identity
while preventing the old requirement from appearing in the live list. Because
`LockedByIndex` is an integer into the post-filter runtime list and is not
rewritten, the effective dependency should be read after those removals; the
authored indices are retained in the inventory above for auditability.

## Uncertainty and limits

1. The report follows the newer nested decompile output. The repository also
   contains a materially older flattened `Mafi.Core.Messages.Goals` copy, so
   behavior may differ if the game DLLs were regenerated between those two
   snapshots.
2. `GoalsData.cs` is decompiler output: local names such as `proto4` and
   `CS_...` are not authored names. Product names in this report were resolved
   from the surrounding prototype assignments in `GoalsData.cs:176-205`.
3. The source proves the predicates and event wiring, but not UI presentation,
   exact activation timing relative to other simulation events, or whether an
   actual game build has additional runtime patches.
4. The `GoalsList.Update` implementation updates locked goals too. This is a
   direct source observation, but the intended player-facing meaning of the
   lock is inferred as presentation/sequencing state because the shown runtime
   code does not guard execution on `IsLocked`.
