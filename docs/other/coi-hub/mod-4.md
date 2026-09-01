# ![ChatGPT Image Aug 1, 2026, 05_03_23 PM.png](/content-images/bf0410e2c3608fbafe3e7c7b12c73317c1b11227874b5825cf45ed218fe36bbb/ChatGPTImageAug1202605_03_23PM.png)

# ⛏️ Automatic Terrain Designations

*One Mine, One Click — Once You Plop, You Can’t Stop.*

[🛡️ **Code analysis explained**](#code-analysis-explained)

## 📋 Overview

***Kayser’s Automatic Terrain Designations (ATD)*** is a quality-of-life mod for Captain of Industry that generates tailored mining designations for Mine Towers. Instead of manually maintaining designations across complex deposits, ATD analyzes the terrain and creates a mining plan that follows the ore body.

Beyond its core one-click mining workflow, ATD provides routed accessways, live ore composition, tower-level mining and dumping priorities, controlled Ore Sorting Plant exports, vehicle ordering and idle policies, debris clearing, manual corner designations, and automated farmland preparation.

Version 0.8.0 moves mining planning to a dedicated worker thread, places completed plans through one fast native batch, and adds an enabled-by-default correction for ultra-thin vanilla ore spikes that can otherwise drag large mine plans deep into high-yield bedrock.

For forestry automation, see [*Automatic Forestry Designations (AFD)*](https://coigame.com/Mod/5/Kaysers-Automatic-Forestry-Designations). Blueprint authors may also like [*Blueprint Designer's Toolkit (BDT)*](https://coigame.com/Mod/1081/Kaysers-Blueprint-Designers-Toolkit).

All tower settings are persisted in the vanilla save file. The mod can be added to or removed from games at any time. 100% open source.

## ⚙️ Feature List

[⛏️ **Create designations**](#create-designations)

[🛠️ **Vanilla fixes**](#vanilla-fixes)

[🛣️ **Routed accessways**](#routed-accessways)

[🧹 **Clear designations**](#clear-designations)

[📊 **Ore composition**](#ore-composition)

[🎯 **Mining and dumping priorities**](#mining-and-dumping-priorities)

[🚛 **Ore Sorting Plant exports**](#ore-sorting-plant-exports)

[🏗️ **Vehicle ordering**](#vehicle-ordering)

[🚚 **Idle vehicle management**](#idle-vehicle-management)

[🪨 **Clear debris**](#clear-debris)

[📐 **Corner designations**](#corner-designations)

[🌾 **Farmland preparation**](#farmland-preparation)

[⚙️ **Additional settings**](#additional-settings)

## ⚡ Quick Start Guide

1. Select a Mine Tower whose area covers a resource deposit.
2. Choose the scanning filter and adjust the tower's designation settings if desired.
3. Click **Create Designations**.
4. If needed, clear the result, adjust the settings, and create it again.
5. Watch your mining crews work through the deposit.

### ⛏️ Create designations

![image.png](/content-images/6a163b2490b58242716c5cb752bd79f20010b349e7379e732de75221c6d922a3/image.png)

*ATD's integrated Mine Tower controls, with detailed explanations available in tooltips.*

Choose a product or use **AUTO**, then scan the tower area and create a tailored mining plan with one click. Per-tower controls cover ore quality, excavation depth, elevation limits, corridor clearance, dumping priority, and accessway mode. Snapshot capture is sliced to keep frames responsive, pure planning runs on a dedicated worker thread, and the final plan is placed through one native batch.

![image.png](/content-images/3f41494ce10ad35a656b8e2e7dd123a3103a6776d2b321a750669581b003b044/image.png)

*An entire deposit designated through a single click.*

![image.png](/content-images/a0997d7fab775c43c4a570b8315c89e9bd9e0513fcea4fc63c08b3e50ec6a79a/image.png)

*The completed excavation follows the deposit with minimal unnecessary digging.*

### 🛠️ Vanilla fixes

The enabled-by-default **Filter ore spikes** world option corrects isolated ultra-thin ore tails produced by vanilla terrain generation before **Ore quality** and bottom flattening are applied. These tails can drag broad mining designations many levels into bedrock, which produces 2.5 times as much Rock as ordinary rock for the same excavated volume.

Across captured spike-affected mines, the filter reduced planned bedrock excavation by up to **99% locally and 36% across entire designation plans**. In the strongest whole-plan test it prevented an estimated 4.8 million units of Rock while changing estimated target ore by about 0.002%. Raw terrain and existing designations remain unchanged.

### 🛣️ Routed accessways

![image.png](/content-images/2abe8a41147dc6cde9ff42a475bc0b237150b61b8d8d7b86e9a587ee3ef474e6/image.png)*A routed accessway connecting isolated terrain work to ground the assigned excavators can reach.*

ATD automatically enables routed turning accessways for eligible modes, sized for T1, T2, or T3/Mega Excavators. **AUTO** selects a suitable clearance from assigned or available excavators, while explicit T1-T3 modes give you direct control. Legacy straight ramps remain available in the Legacy modes and as an explicit fallback when preferred.

Large accessway searches now perform more work asynchronously and in short slices, keeping the game responsive during complex plans.

### 🧹 Clear designations

*The Clear control removes this tower's ATD-generated work, with Shift available for a wider area cleanup.*

Use **Clear designations** to remove the selected tower's ATD-generated terrain and tree-harvest designations. Shift-click clears all terrain designations in the tower area while still limiting generated tree-harvest cleanup to that tower.

### 📊 Ore composition

![image.png](/content-images/006b3e17740b08334a9fcdbcbc0dd0424499217939153f9322497b80bb48c0bd/image.png)*Live ore composition estimates for the terrain designations currently managed by the Mine Tower.*

The **Ore composition** panel estimates the products contained in the tower's current mining and leveling designations. It includes Rock produced by digging into bedrock, applies bedrock's higher vanilla yield, reflects the current Ore Mining Yield difficulty setting, and works with both ATD-generated and manually placed designations.

### 🎯 Mining and dumping priorities

![image.png](/content-images/0de84c857f6e8394b66368e049bbb1640af6cb414c2cc8681013b9aea507e1fe/image.png)*Tower-level product and dumping priorities keep excavation and material delivery focused where you want them.*

Set a preferred mining product once at the tower level and ATD applies it to assigned excavators. Dumping can remain **Passive** or use vanilla-style active priorities from 1 to 15; farmland topsoil filling is activated automatically when required.

### 🚛 Ore Sorting Plant exports

![image.png](/content-images/875cd1e0a758d3cea39fd5d1809df0cc56cbba8beb26e2c3baed5ea918dfeb7a/image.png)*Control where Ore Sorting Plants send their output and which trucks may handle it.*

ATD adds vanilla-style export controls to Ore Sorting Plants: configure compatible storage and Mine Tower destinations, set an export priority, and assign trucks directly. Enable **Assigned trucks only** to restrict cargo collection and delivery to trucks assigned to the plant or its participating Mine Towers. Routes and settings are saved with the game.

### 🏗️ Vehicle ordering

![image.png](/content-images/f6de06a7d560d26012c3563b6fce2626a64f903dc024729dabc4ad7a160bd7b6/image.png)*Order and pre-assign excavators or trucks directly from the Mine Tower inspector.*

Order vehicle construction from the tower's vehicle assignment UI. ATD selects the closest eligible Vehicle Depot, records the pre-assignment, and sends the completed vehicle to the tower automatically. Shift and Ctrl modifiers order 5 or 10 vehicles; Shift+Alt-click orders directly even when a free vehicle is available.

### 🚚 Idle vehicle management

![image.png](/content-images/250d2f34115bee61432e5ed3579594e03212fe239b6b4ea548ad4af99ddc89ae/image.png)*Per-tower idle policies keep vehicles nearby, park them normally, or release them for work elsewhere.*

Idle Excavators can be released and automatically recalled when excavation resumes. Trucks offer three policies: **Park at tower** (the vanilla behavior), **Stay put**, and **Soft release**, allowing each Mine Tower to balance responsiveness against fleet sharing.

### 🪨 Clear debris

![image.png](/content-images/c5b64645afee979bb1282c1e028f9790e9529ba971aa590c744c17eb1c3250db/image.png)*The Clear debris control queues reachable rocks, bushes, and other obstructions for excavator removal.*

Request removal of reachable debris in the tower area without spending Unity on Quick remove. ATD temporarily works around conflicting terrain designations and restores them after cleanup. Ctrl-click also includes debris that is not currently reachable.

### 📐 Corner designations

![image.png](/content-images/111cee97faf31168bf46bb5daf4b83e39f5fc49b1a67f55fe935e594840aa306/image.png)*Outer, inner, and planar corner tools integrated into the terrain-designation toolbar.*

![image.png](/content-images/9eeea6d2981ef065349816ec0b9b8acf77dc57f3aef4e6647a91b62a5a9c2c2f/image.png)

*Manual corner designations create smooth, connected surfaces in 3D.*

Place outer, inner, and planar corner profiles with four rotations using mining, dumping, or leveling designations. The tools support preview, drag placement, and keyboard cycling for fast terrain shaping.

### 🌾 Farmland preparation

*Farmland preparation automatically excavates, fills, and reconnects a level area with fertile material.*

Turn flat leveling work into farmable ground with per-tower automation. ATD manages excavation, farmable-material filling, vehicle access, and completion; Farm Placement Assist can hold farm construction until the required terrain cells are ready.

### ⚙️ Additional settings

*ATD's Mod Settings tabs provide worldwide behavior, reusable defaults, and optional expert controls.*

Open ATD in the Mod Settings window for controls that are not available directly from an individual Mine Tower:

- **Terrain safety** — Choose how cautiously ATD predicts landslides and keeps generated work away from oceans and buildings.
- **Vanilla fixes** — Keep the ore-spike correction enabled, or disable it for exact unfiltered vanilla deposit geometry.
- **Ramps outside tower areas** — Allow a bounded retry just beyond the tower boundary when no valid in-area accessway can be found.
- **Debris and tree handling** — Control disrupted-tree harvesting, terrain changes used to remove debris, and when accessway cleanup may spend Unity on Quick remove.
- **Notifications** — Enable or disable excavator-completion and accessway warning notifications.
- **Tower panel defaults** — Choose whether the Mining Designations, Ore Composition, and Farmland Preparation panels start collapsed.
- **Mine Tower defaults** — Set the initial accessway mode, dumping priority, excavation depth, elevation limit, ore quality, and idle-vehicle behavior, then optionally save them as the configuration for new games.

Accessway search uses A\* by default. Advanced users can temporarily switch to Dijkstra with the `atd_set_access_astar` console command when comparing routes; the choice applies only to the current session.

For mining connoisseurs who enjoy tuning the last grain of ore, ATD also exposes expert ore-quality thresholds and accessway pathfinder controls. Most players can safely leave these at their defaults.

---

## 🛡️ Code analysis explained

CoI Hub's code analysis flags capabilities that can be legitimate parts of a mod but deserve context. ATD is open source, and these warnings mostly reflect its game integration and access-search development tools:

- **Loads other code at runtime** — ATD's access-search replay tools can load the exact built ATD assembly so recorded searches can be replayed against the same code. The in-game mod does not download or execute arbitrary third-party code.
- **Spawns processes** — ATD does not launch child processes. The warning is triggered by diagnostic code that reads the current game's process information to measure replay CPU and memory use; it is not used to start external programs.
- **Accesses filesystem** — ATD reads bundled translation files and its settings, and its replay tools read and write diagnostic cases under Captain of Industry's user-data folder. It does not need broad arbitrary file access for normal mining or accessway gameplay.
- **Uses Harmony** — Harmony is the patching library ATD uses to integrate with Captain of Industry. It patches specific game and UI methods to add controls, behaviors, and compatibility hooks.
- **Uses reflection by name** — Some game APIs and UI fields are private or vary between versions. ATD uses named reflection to find optional vanilla types, methods, and fields, then skips that integration or uses a fallback when the expected member is unavailable.

The worker thread itself receives a sealed snapshot of primitive world data and returns a mining or accessway plan. Snapshot capture, live validation, and designation changes remain on the game thread; the worker does not access live Unity or Mafi world objects.

Mine away!

PS. Leave a 👍 or a ⭐️ if you found this mod useful.