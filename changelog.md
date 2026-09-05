v0.8.2 [unreleased]

* Fixed: Ore Sorting Plant export routes, priorities, and assigned-truck state now save successfully by registering the `atdOreSorterExports` state parameter in `config.json`.

* Added: **PERFORMANCE → Reduce oversized areas** is a default-on per-world option. When the normal access snapshot exceeds its memory ceiling, ATD now builds one deterministic, geometry-only low-turn corridor from the active source cluster toward known goals or the tower access, captures terrain and blockers only inside its sparse context mask, and runs the normal route search there. The reduced attempt may select a different route; `ReducedAreaNoPath` remains inconclusive. Requests whose full snapshot fits keep the existing path.

* Fixed: Reduced-domain planning no longer rebuilds the same corridor for every nearby source in a large cluster. Sources already covered by the selected corridor are admitted without another mask construction, avoiding the multi-second game-thread freeze and allocation spike observed with more than 2,000 mining origins.

* Fixed: Reduced-domain planning now spends remaining memory capacity on balanced corridor widening after admitting complete source branches. Higher ceilings therefore produce larger useful masks instead of repeating the same narrow corridor; widening stops at the largest four-tile increment that fits the conservative estimate.

* Fixed: Corridor widening now grows the existing sparse mask one ring at a time instead of repeatedly rebuilding larger trial masks. Its budget uses the same retained-memory accounting as snapshot preflight, including worst-case building occupancy, so a planned reduced mask cannot immediately fail capture as `SnapshotTooLarge`.

* Fixed: Accessway cleanup now leaves props to planned terrain work when explicit targets or projected side-ray work exceed vanilla's destruction threshold by more than an internal 0.5 terrain levels. This also takes precedence over **Always** quick removal. Live placement-height offsets and scaled burial thresholds are respected; safety-only, missing, conflicting, and borderline projections retain cleanup. Route selection/scoring and tree harvesting are unchanged; no public parameter or saved state was added.

v0.8.1 [released]

* Added: World settings now includes **PERFORMANCE → Use worker thread**, enabled by default. The per-world opt-out runs new planning requests on the game thread; Save as config stores the default for future worlds. Mining passes can pause the game in this mode. Backend changes wait for any cancelled worker computation to stop before starting game-thread planning.

* Fixed: Mining ocean and building protection now rechecks the final excavation boundary after removing unsafe designations and rebuilding corner heights. Previously, a single pass could leave newly exposed faces unchecked. Protection repeats until the remaining plan passes, capturing additional terrain facts as needed.
* Improved: Added outward diagonal protection rays at exposed convex mining corners, covering ocean and building hazards missed by cardinal-only rays. Diagonal rays use the same material-dependent height increment per grid-neighbor step and the configured end buffer.
* Verified: Ocean/building regression cases cover repeated boundary removal, diagonal directions, end buffers, staged capture, missing facts, and map bounds. All 13 fixture groups and the Debug build passed. On the recorded gold-island case, the corrected plan decreased from 2,239 designations to 1,052 with repeated cardinal checks, then to 670 with diagonal checks.
* Accepted limitation: In-game testing on the gold island held back the ocean with a margin at maximum safety. Medium safety left a tiny breach between cardinal and diagonal directions. This is accepted as a player-controlled trade-off between excavation reach and protection; sampled rays are not a universal guarantee against flooding or landslides.

v0.8.0 [released]

* Fixed: **Ore composition** now includes the Rock produced when designations excavate below the stored terrain layers into bedrock. Vanilla mines bedrock indefinitely toward the target height and gives it a 200% quantity multiplier versus ordinary rock's 80%; the panel now converts each material with its own live yield before combining products.
* Improved: Mining snapshot capture now yields by elapsed time, pure planning runs on the shared dedicated worker thread, and final mining designations are submitted through one native batch. Large plans remain responsive during capture and placement is dramatically faster.
* Improved: Bedrock-aware replay of nine captured mines confirms the production spike filter as the best first-release balance. Captured cases measured up to 99% less bedrock locally and 36% across entire designation plans, with the strongest whole-plan case preventing an estimated 4.79 million Rock while changing estimated target ore by about 0.002%.

* Improved: Added a per-world **Filter ore spikes** option under **Vanilla fixes**, enabled by default. Mining planning now trims isolated ultra-deep vanilla ore tails before Ore quality and bottom flattening while preserving raw captured terrain for safety and replay. The initial strict eight-neighbor, four-layer policy remains tunable through the mining laboratory.
* Developer: Mining replay decoding accepts the one-field-short legacy `MiningPolicy` layout and initializes its new spike-filter flag to false, preserving exact archived baselines. New captures persist the flag normally.

* Fixed: Background mining replay recording could finish without ever showing a progress toast because visibility waited for game-thread processing time. Toasts now use active wall time; recording shows capture progress and Abort without path-search statistics. The same fix covers worker jobs with inexpensive polling.

* Experimental: Mining capture now yields by elapsed time and shares the terrain-column collector and single worker with access planning. With spike correction disabled, the extracted planner preserves existing ore/depth/geometry rules. Full columns retain material, product, thickness and bedrock facts for later experiments.
* Changed: Create Designations queues different towers and replaces only the same tower's pending request. Mining submits one native designation batch and verifies actual placements for ownership; a submitted batch finishes without cancellation rollback.
* Developer: The existing laboratory accepts mining cases (`atd_access_replay_arm <name> <family> mining`), with independent input/expected geometry, exact-DLL replay and benchmarks. Synthetic parity, codec, safety and worker checks pass; representative large-mine placement and spike-filter corpus qualification completed before v0.8.0. See the mining worker design note.
* Developer: Newly recorded mining and access laboratory manifests include the active map name. Existing cases remain replayable but cannot recover this missing provenance retroactively.
* Fixed: Map-name capture no longer requests the unregistered raw island-map object and aborts ATD initialization. It reads the optional registered legacy map manager with island- and world-map config fallbacks, and settings-path setup now happens before dependency resolution so a later initialization failure cannot produce a misleading missing-settings warning.
* Experimental: The first ore-spike filter sweep compares strict-neighbor, median-neighbor, and morphology-gated clamps with Ore quality and bottom flattening disabled. Positive captures show strong rock savings with little target-product loss, but the quartz case is materially less favorable. A mostly excavated mop-up capture is retained separately as a provisional negative control; r8/r10 candidates leave its final plan unchanged.
* Experimental: A known-spike post-Medium mop-up capture shows that intact ore-neighbor detectors fail after surrounding ore is removed. The in-game viewer confirms all six plan-affecting bedrock-r4 sources as spikes; r6 catches two, while r8/r10 change no final geometry. The rock-to-target tradeoff is much tighter than on untouched deposits, and sample 4 remains the false-positive check.
* Developer: Added a transient laboratory overlay for ore-spike candidate review. It loads the generated marker CSV, colors points by review status, and shows captured depth facts on hover; markers are cleared on world changes and never enter saves.

v0.7.1 [released]

* Changed: Routed experimental accessways are now always enabled for eligible accessway modes. The old `turningRampsExperimental` settings toggle has been removed; legacy straight-only modes and the explicit legacy fallback remain unchanged.

* Changed: Access searches now use A* by default. The persistent `experimentalAccessUseAStar` setting has been removed; `atd_set_access_astar on|off` can still switch the algorithm for the current session when route comparisons are needed.

* Changed: Raised the default conservative estimated-retained-memory ceiling for one access snapshot from 512 MiB to 1,024 MiB. `accessSnapshotMemoryCeilingMiB` remains configurable in `ATDsettings.json` and through `atd_set_access_snapshot_memory_ceiling`.
* Fixed: Access snapshot capture now uses the union of the 48-tile outside-area G margin and the V side-ray reach instead of adding both margins. The nearby-building capture follows the same boundary while retaining its dedicated safety buffer.
* Improved: Access Search Laboratory reports the captured snapshot estimate beside replay memory observations. Newly armed captures add optional capture-boundary evidence to manifest metadata; existing replay payloads remain unchanged.

* Fixed: Prop-removal's temporary designation suspend/restore cycle no longer
  revokes the owning tower's generated-designation bookkeeping, so the clear
  button can remove an overlapping access terminal instead of leaving an
  orphaned restored designation.

* Experimental: Farming access searches now exercise the dormant internal
  single-thread worker. Snapshot capture, live validation, and designation
  commit remain on the game thread; pure workspace preparation, route search,
  and plan materialization run off-thread through the same executor used by the
  Access Search Laboratory. This is not yet a player-selectable mode.

* Experimental: Interactive Create Designations access searches now use the
  same internal worker path. Their snapshot capture, live validation, cleanup
  acceptance, and designation commit remain on the game thread, and the mode
  is still not player-selectable.

* Improved: Interactive access snapshot preparation now slices existing-work
  outside-corner projection within scan rows and the Mega ground graph's
  cleanup, component, and goal-potential construction against the configured
  frame budget. The projection also reuses captured terrain heights instead of
  repeatedly querying live terrain; route semantics remain unchanged.

* Improved: The legacy-ramp comparison performed after a routed worker result
  now yields within long dry placements and between evaluated candidates using
  the active frame budget. Its geometry, scoring, retry order, and final
  routed-versus-legacy choice are unchanged.

* Improved: V2 ray-overlay queries now reject tiles outside the immutable
  history's accumulated ray bounds before scanning ancestry or allocating an
  empty cache entry. The expensive Laboratory case retained its exact route
  while reducing search time and peak memory in the initial directional
  benchmark.

* Improved: History-qualified V2 handoff entries now apply optimistic
  ordinary-ground label dominance before expensive ray, cleanup, and local
  escape validation. A successor is skipped only when it cannot beat the
  existing label even with zero cleanup cost; goal suffixes remain untouched.
  The expensive Laboratory case retained its exact route while search time
  fell from 40.2 to 33.6 seconds in the initial directional comparison.

* Improved: Plain physical-ground V2 handoff entries are now rejected before
  entering the queue when every geometrically possible ordinary-ground and
  ground-to-V successor is already cheaper even at a zero-additional-cost
  lower bound, or the exact ground-to-V transition cannot pass its structural
  and accumulated-history checks. Goal suffixes, projected ground, fixed
  navigation, and any unmatched successor remain history-qualified. The exact
  Cluster 2 replay removed 11,147 further dead expansions without changing its
  route; a three-run search median improved from 37.9 to 34.8 seconds.

* Improved: Shallow V2 frontiers that return over an already cheaper ground
  component now attempt an exact ordinary-ground replacement. The replacement
  is accepted only when the real ground-to-V seam and transition evaluators
  reproduce the identical band for no greater total cost, so prior ray and
  cleanup credit is repriced instead of discarded optimistically. In the
  Cluster 2 replay, the late `(832,1670)` family fell from 4,782 expansions to
  320 and its shallow post-merge wave fell from 3,166 expansions to 3 without
  changing the selected route.

* Fixed: The diagnostic explored-node overlay now receives bounded sampled
  events from worker searches. A full visualization buffer drops
  samples instead of slowing or blocking route search.

* Fixed: Worker-search progress now reports worker elapsed time instead of
  showing the cooperative frame budget and the coroutine's much smaller
  polling time. Cooperative capture and search retain their slice diagnostics.

* Fixed: Stale or otherwise disposed farming access work now abandons its
  submitted worker job, cooperatively cancels active computation, discards the
  unowned terminal result, and releases the single worker slot for retry.

* Fixed: Saving during an active farming access search now bypasses the normal
  failure backoff once the save-restored designations are available. The stale
  pre-save result remains rejected and a fresh search is queued immediately.

* Added: A developer-only Access Search Laboratory foundation can arm and
  atomically record the next validated routed search, then replay its owned
  snapshot and exact canonical result outside the game through the selected
  Release mod DLL. Normal searches remain dormant unless capture is armed.

* Added: The developer-only Laboratory runner can emit an ordered V2 expansion
  trace with timing, queue age, ground-relaunch, direction, handoff, portal,
  history, potential-owner, label identity, and accepted-successor outcome
  fields. Production searches do not collect this trace.

* Improved: Armed access-search replay capture now encodes and compresses its
  immutable request in a background operation while the progress surface shows
  the current stage and exact work-unit percentage, avoiding a long opaque
  main-thread stall. Its stop button aborts only replay capture, removes partial
  output, and preserves the already accepted access route.

* Fixed: Arming an access-search replay now archives the exact Release DLL by
  hash in the private Laboratory directory, preventing later builds from
  orphaning an otherwise valid captured case.

* Fixed: Quick prop cleanup now preserves overlapping pathfinder work as the
  designation to restore and retries a manager-owned preview that vanilla
  removed, instead of misclassifying the missing preview as a player override.
  Replay captures remain staged at 99% until cleanup completes and the live
  route passes authoritative validation; failed, replaced, or aborted cleanup
  deletes the staged case rather than publishing a false accepted baseline.

* Fixed: Access replay canonicalization now ignores non-semantic object-sharing
  differences between sliced in-game route steps and unsliced laboratory route
  steps. Equal step values therefore reproduce exactly even when one execution
  reuses an internal transition object and the other creates an equal copy.

* Improved: The authoritative cleanup wait now explicitly displays `99%`
  alongside its phase text before the recorder publishes the completed case.

v0.7.0 [released]

* Added: Ore Sorting Plants now support configurable export routes to compatible
  storages and Mine Towers, with a vanilla-style export priority and persistent
  route state.

* Added: Ore Sorting Plants can assign trucks directly and optionally restrict
  cargo collection and delivery to trucks assigned to the plant or its
  participating Mine Towers.

* Improved: Cooperative V2 access searches now resume exact ground suffix,
  fixed-navigation path/portal, and handoff-entry work across frame slices
  instead of treating a whole large expansion as one atomic step. Cancellation
  and snapshot semantics remain unchanged; the worker-thread rollout is still
  separate.

* Improved: Access preparation now captures the primitive terrain, prop, stump,
  and exact layout-occupancy facts needed to evaluate vanilla mining and
  dumping readiness without consulting live designation state.

* Fixed: Oversized access snapshots now fail their memory preflight before
  expensive reachability classification, and out-of-area origin checks reuse a
  shared bounded reachability flood. Snapshot-size failures retain their
  diagnostic reason instead of being reported as generic no-candidate blocks.

* Improved: The access-search progress toast now keeps a fixed-width
  description row above a separate statistics row, preventing changing phase
  text and live counters from shifting the text and controls.

* Added: Access searches now show a tower warning when the area is too large
  for the turning-ramp snapshot, instead of reporting only a generic failure.

* Improved: Production access snapshots now create their search workspace from
  captured facts and policy; caller-built handoff evaluator closures are
  retained only by synthetic fixtures, and the immutable snapshot no longer
  accepts callback-shaped compatibility inputs. Single-cell and span handoff
  evaluation now lives in a pure access-search module instead of the runtime
  designation type.

* Added: Debug access-search diagnostics now attribute capture and navigation
  time, allocation setup, and the largest atomic slice to named steps so future
  spikes can be investigated without changing the production search result.

* Changed: Raised the configurable access-search frame-budget ceilings by 10x:
  the legacy sliced-search setting now accepts up to 1000 ms, automated manager
  work up to 150 ms while running, and interactive or paused manager work up to
  300 ms. Existing defaults remain unchanged.

v0.6.1 [released]

* Fixed: Interactive accessway searches now invalidate before their next slice
  when the tower's accessway mode, ramp width, global access policy, owner, or
  area changes. A search started for T3 clearance can no longer materialize a
  T3/V2 route after the player switches the tower to T1.

* Improved: Access snapshots now carry the tower-local ramp/clearance revision
  and invalidate during capture when those settings change, keeping capture,
  search, and commit on the same access identity.

* Improved: Access capture now copies building occupancy and fixed-height facts
  before cleanup and durability preparation, so those phases no longer consult
  the mutable farming building cache while a snapshot is being assembled. The
  retained-memory estimate now accounts for copied building occupancy too.

* Improved: Captured terrain heights now drive handoff geometry, exact-profile,
  leveling-face, and rank-two work checks during access preparation. Vanilla
  designation-readiness callbacks remain on the cooperative game-thread path
  until their live API inputs are captured separately.

* In progress: Accessway capture now records source revisions and bounded
  retained-memory estimates, marks environmental changes without conflating
  them with hard invalidation, and reports `SnapshotTooLarge` diagnostics
  through farming requests. The conservative snapshot ceiling is configurable
  for profiling; the pure-preparation extraction remains in progress.

* Refined per-tower **Dumping priority** to use the vanilla green import-priority control: numeric priorities 1–15 (1 - high through 15 - low) advertise runtime-only input demand for every eligible dumping designation managed by the tower, while a separate **Passive** option preserves vanilla passive dumping and only imports from active exporters. Numeric levels no longer have redundant per-option tooltips; Passive retains its explanatory tooltip. The world default, ATDsettings persistence, clone support, and `atd_set_dumping_priority` console command follow the same model. Because this is pre-release, existing value `15` is intentionally reinterpreted as active lowest priority; choose **Passive** for the old behavior.

* Changed the default dumping priority to **Passive** to avoid tower-wide active demand locking vehicles onto distant or unreachable dumping origins. During ATD farmland filling, while the tower's dumpable products are restricted to farmable materials, Passive is temporarily evaluated as active priority 14 and restored to Passive after the fill window.

* Refined the dumping-priority tooltip to state that farmland topsoil always employs active dumping, and aligned the widened Passive selector with surrounding controls.

v0.6.0 [released]

* Changed: Routed turning ramps are now the default accessway generator for AUTO and T1-T3. The legacy straight-ramp generator remains available as an explicit fallback option.

* Improved: Interactive Create Designations accessway generation now runs as a
  manager-owned `CreateDesignations` request under the cooperative frame
  budget, with request-scoped cancellation, progress diagnostics, and a
  type-specific progress toast. The scan remains the owner of the final ramp
  cleanup and designation commit.
* Improved: Existing-terrain repair and planned mining-tower ghost access now
  use the same manager-owned interactive handoff instead of directly draining
  their access searches inside the Create Designations coroutine.
* Fixed: Farmland preparation automation now reconnects persisted sessions to
  their live mine towers before lifecycle cleanup, preventing the option from
  turning itself off after loading a save unless the tower was actually removed.
 * Added: Farmland preparation filling can actively import farmable soil
   materials from neutral storages, including storages that neither import nor
   export; tower material routes and dumpable-product settings still constrain
   the sources and products used.
* Improved: ATD console setter commands now report their current values when
  invoked without arguments and write those values to the ATD log without
  changing the settings.
* Added: Mine Tower copy, blueprint, repeated placement, eligible Cut, and
  Copy settings workflows now preserve the tower's exact managed area and
  player-authored ATD settings, including ore selection and quality, mining
  priority, accessway clearance, and vehicle idle policies. Vanilla continues
  to handle vehicle assignments, routes, and ordinary tower configuration.

v0.5.12 [released]

* Added a three-option idle truck behavior dropdown for mine towers: **Park at tower**, **Stay put** (the new default), and **Soft release**. Stay put keeps trucks assigned without issuing vanilla return-to-tower parking jobs.
* Added migration for the former `autoReleaseTrucksWhenIdle` setting and per-tower state.
* Fixed: Soft release now treats paused towers and towers without an unpaused assigned excavator as idle, and the tower tooltip refreshes its soft-released truck list after each release tick.
* Improved: The idle truck behavior tooltip now shows a clickable soft-released truck card; clicking it cycles through and pans to the released trucks.
* Improved: The excavator idle-release tooltip now shows a clickable card for soft-released excavators.

v0.5.11 [packaged]

* Fixed: Right Alt keybindings no longer capture the keyboard layout's synthetic Ctrl and AltGr aliases.
* Fixed: Settings migration now promotes legacy turning-ramp values to the enabled default without disabling users who already enabled experimental accessways; the revision-2 downgrade is repaired on the next launch.
* Improved: Large farming accessway searches now capture terrain and advance cooperatively, reducing long synchronous pauses while access is being planned.
* Changed: Access progress toasts now show the active phase and search progress for both interactive and managed searches; Hide suppresses the toast for the active request without cancelling it.
* Fixed: Farming access searches can now finish through existing fixed provider networks and reuse served provider work without creating unnecessary duplicate routes.
* Improved: Request-scoped fixed-provider ground graph preparation reuses immutable snapshot topology and incrementally updates goal distances, reducing preparation time for existing provider networks.
* Added: Deterministic access fixtures run during world initialization and fail managed access preparation closed if a core access invariant regresses.

v0.5.10 [packaged]

* Added: Manual Planar corner designations with four rotations, preview/drag support, a dedicated diagonal-plane toolbar icon, and K-key cycling alongside outer and inner corners.
* Changed: Ordered the corner toolbar buttons and K-cycle as Outer, Inner, Planar; the Planar icon is now an embedded SVG rendered as a tintable runtime icon.

v0.5.9 [released]

* Improved: The accessway manager now validates farming ownership, work, tower area, and access settings before every cooperative slice. Stale work terminates before placement and enters the existing bounded retry policy; completed owners cancel without retry.
* Improved: The accessway manager now bounds its pending queue at 32 requests, preserves interactive priority under pressure, returns retryable diagnostics for evicted work, and periodically reports active timing, queue age, coalescing, stale, dropped, and completion health counters at Debug level.
* Improved: Non-success accessway terminals now report request owner, work context, prior phase, queue and active age, processing time, visited/pending work, reason, and retry eligibility. Debug manager health sampling is skipped entirely below Debug diagnostic level.
* Fixed: Flat leveling cells generated as part of a farming accessway no longer enter the farming-preparation lifecycle as new player farming intents. Their accessway targets and ownership now remain intact until normal accessway cleanup.
* Fixed: Directly replacing an ATD-generated terrain designation now revokes its ordinary, accessway, and farming ownership before the replacement is adopted. Farming cleanup no longer treats the player's replacement as ATD-owned.
* Fixed: A pending accessway for one farming cluster no longer blocks searches for other inaccessible clusters. Farming skips only clusters that already have a pending owned provider, keeps non-selected farming work as fixed provider context rather than rediscovering it as another access obligation, and lets later clusters plan through that projected terrain.
* Fixed: After placing one farming-cluster provider, the manager now re-evaluates and queues the next cluster immediately instead of waiting for the next 10-second farming poll. Previously served clusters are also exposed as projected provider goals, allowing later searches to terminate through their still-pending accessways instead of unnecessarily cutting a separate route to tower ground.
* Fixed: Width-two V2 searches now translate projected fixed-provider origins into request-scoped vehicle-center goals used by both A* and replay validation. Previously the request advertised those goals, and the frontier could traverse them, but V2 only stopped at the snapshot's tower-ground goals.
* Fixed: Holding Left Alt while adding a tower vehicle now checks assignable vehicles in the default logistics zone, matching the vanilla assigner's Alt override.
* Fixed: Added the missing translations for the **Allow ramps outside tower areas** world-setting label across all supported non-English languages.

v0.5.8 [released]

* Fixed: Restricted vehicle pre-allocation UI updates to MineTower inspectors and registered pending orders for save/load persistence, preventing cross-mod display overwrites and lost ATD orders after reload.
* Fixed: Farming access generation now stops its synchronous fixpoint at the first failed search and suppresses unchanged retries for at least 10 seconds. Relevant work/settings changes may reopen the obligation after that grace period, while a 60-second maximum retry covers undetected terrain or other-mod changes.
* Changed: Farming access failures now emit one informational attempt summary without the per-cluster warning stack traces.
* Fixed: Live G-to-V validation now preserves the direct-leveling quick-bridge representation while independently rechecking the full T3 post-work corridor. Valid direct leveling accessways are no longer rejected because the rough replay includes one additional V-face center.
* Changed: Sliced access searches now deliver each caller its own terminal search-result and designation-plan pair. Farming and planned-tower dry runs no longer recover that pair from the shared global last-result slots, and cancelled requests retain their search diagnostics without exposing a stale plan.
* Fixed: Farming no longer treats a valid unfinished accessway as terrain damage merely because its projected route is not traversable before excavation or filling occurs. Compatible accessway designations remain one pending obligation without repeated searches; if the complete plan disappears while work is still inaccessible, replanning uses the bounded retry policy.
* Fixed: Farming access generation now runs through one runtime-only cooperative manager instead of synchronously draining a complete search inside a farming tick. One new-planner-only request advances per rendered frame, unchanged obligations coalesce, and each request may place at most one accessway before farming re-evaluates live reachability.
* Improved: Managed farming access uses a 10 ms normal-play budget and up to 30 ms while paused, reports preparation or filling progress in a cancellable toast, and suspends while the legacy interactive Create Designations operation is active so the searches do not compete.
* Changed: Stopping a farming access search from its progress toast suppresses further automatic access attempts for that tower phase until farming automation is disabled and re-enabled. Save and world boundaries discard all manager requests and reconstruct demand from live farming state.
* Fixed: Managed access searches now use the simulation's authoritative pause state, so an already-running search switches from its normal 10 ms frame budget to the configured paused budget immediately. The active budget is shown in the progress toast and logged when it changes.
* Fixed: The managed farming progress toast now retains one stable button for the lifetime of a request and updates only its text. Rebuilding the complete toast every rendered frame previously prevented the Stop button from completing a mouse click.
* Fixed: Disabling farming, removing its tower, changing farming phase, or crossing a save boundary now adopts origins from an already-committed terminal access request before lifecycle cleanup. Accessway cells committed while paused can no longer escape farming ownership merely because the next simulation poll had not yet consumed the manager result.
* Fixed: Manually removing or replacing a farming-owned accessway designation now revokes the farming session's ownership as well as ATD's general generated-origin ownership. A later farming-off cleanup no longer deletes the player's replacement at that origin.

v0.5.7 [released]

* Fixed: ATD notification messages now refresh after the game applies the selected mod translation, including the excavator-completed notification.
* Changed: Removed the queued debris-cleanup notification; the main-thread removal designations now provide that progress feedback.
* Fixed: Dot-prefixed translation metadata is excluded from runtime localization scans and release packages.
* Fixed: Shift-Alt-clicking a tower vehicle-order button no longer enqueues both a pre-assigned truck and a second free truck.
* Improved: Added an enabled-by-default **Allow ramps outside tower areas** world setting. When an in-area experimental ramp search drains its frontier or finds no feasible V2 start within the current bounds, narrow and T3/Mega accessways retry within 16 tiles beyond the tower boundary. Timeouts and other interrupted searches do not retry. A successful fallback relies on the game's normal outside-area alarm and suppresses ATD's redundant ramp warning.
* Improved: Replaced V2 A*'s dense per-tile route potential with the sparse P-field foundation over paid generated origins and reusable FV nodes. Generated steps charge the next origin overhead, FV retains exact cardinal/diagonal suffix costs, and disconnected ground escape fields are built lazily per component.
* Improved: Independent diagnostic controls now govern the commonly used fading access-search frontier and the persistent sparse P-field trace. Generated potential values use `P`; reusable fixed-navigation values use `FX` and `FY`. The designation Clear button and `atd_clear_diagnostic_overlays` command clear all stored diagnostic traces.
* Improved: The access-search frontier overlay now retains its newest 3,000 points in an O(1) circular queue instead of shifting the full capped list for every explored node.
* Improved: V2 ray-history lookups now collapse each transition's duplicate tile constraints and memoize the resulting per-tile envelope, replacing repeated linear scans through thousands of historical ray samples while preserving the current route rules.
* Improved: V2 ray history now models projected terrain rather than ray presence. Same-sort cut/fill contact is height-aware and receives incremental work credit, termination uses the ordinary safety tail, safety-only cells carry no usable ground height, and self-disruption checks become strict only after a connected V segment introduces a third travel direction.
* Fixed: Refined projected terrain no longer rejects generated exits through the complete connected fixed predecessor band, and immutable ray-clearance dilation retains projected work heights instead of being misclassified as post-termination safety-only terrain.
* Fixed: V2 generated paths now retain safety ownership for the complete compatible connected fixed predecessor structure after the first fringe step, so neighboring FV rays cannot form a false wall around dense fringes. Replay/materialization preserves the same ownership as search, while projected work heights and disconnected structures remain authoritative.
* Fixed: Deep generated cuts can now enter and undercut cut-ray safety tails (with fill handled symmetrically), instead of treating heightless same-operation safety as an absolute wall. Immutable FV rays also resolve height-aware against earlier same-sort projected work, preventing dense boundary rays from independently over-projecting through one another.
* Fixed: V2 strafe transitions now validate and cost both endpoints of the exposed rear face on their newly introduced predecessor-outer cell. Strafing can no longer bypass reverse-direction clearance and select artificially cheap constant-height routes through mountains.
* Fixed: V2 rays now charge the unique clearance-dilated projected work volume that later generated profiles can credit, instead of charging only the ray centerline while exposing a much wider free-work envelope. Overlapping captured, historical, and same-transition projected work is charged only once.
* Fixed: Raised single-origin V2 sources can now launch downhill through their own projected dumping surface. Same-sort projected terrain blocks a later profile only when that profile actually performs the opposing cut or fill; reaching captured terrain or continuing the same operation remains compatible.
* Fixed: Experimental accessways no longer reject direct ramps that overlap a same-operation landscaping ray. New cut rays terminate when they meet prior cut rays, and fill rays likewise merge into prior fill rays, in both V1 and V2; opposing operations remain blocked.
* Fixed: The normal **Clear designations** action now also cancels pending accessway Quick remove requests and removes their temporary previews.

v0.5.6 [released]

* Improved: While the game is paused, the prop-removal manager now advances pending cleanup far enough to show its temporary terrain designations.
* Changed: Pending Quick remove requests show a temporary mining preview at `ceil(ground height + 1)` when their origin has no existing designation; Unity is spent only after the game is unpaused.
* Improved: A player or pathfinder designation already present at a Quick remove origin remains in place and serves as that origin's visual marker.
* Improved: Experimental T3 (V2) accessways can now connect directly to jagged and slanted terrain-designation fringes. Corner-capable transition bands allow efficient exits without detouring to a smooth edge.
* Improved: Vehicle depot completion notifications trigger only for unassigned (free) excavators, skipping pre-assigned excavators and specifying the built excavator model.
* Fixed: Manually clearing or replacing an ATD-owned terrain designation now releases its tower ownership and correctly marks designation generation dirty.


v0.5.5 [released]

* Fixed vehicle assigner row visibility when starting a new game where vehicle technology is not yet unlocked but initial starting vehicles (Pickups/Excavators) are owned (`stats.Owned > 0`).
* Improved: Completed all supported language entries for the Accessways settings and mine-tower vehicle-status summary.
* Improved: Replaced the Russian translation with the reviewed community-provided localization.


v0.5.4 [released]
* Fixed: Deleted mine control towers now properly reset farmland work in progress.
* Changed: Retuned World safety policy slope and buffer presets (`Min`: [0.8, 0], `Low`: [0.85, 1], `Med`: [0.9, 2], `High`: [1.0, 3], `Max`: [1.1, 4]).
* Changed: Set default landslide slope factor (`accessRaySlopeConservatism`) to `0.9` and default ray end buffer (`accessRayEndBuffer`) to `2` tiles.
* Improved: Added `_comment_*` string keys for all expert access pathfinding tuning parameters in `ATD.Settings.cs` and `ATDsettings.json`.
* Added: `atd_toggle_v2_pathability_overlay` debug console command and V2 route handoff/ground suffix search diagnostics.
v0.5.3 [released]
* Fixed: Completed removal of the abandoned reverse BFS prototype by restoring the height-aware paired-goal lower bound and its regression fixture.
* Restored vehicle-prototype-based pre-allocation UI patching for excavators and trucks, including compatible modded subclasses and non-tower assignment panels.
* Fixed the pre-allocation visibility observer to use the stable inspector parent, matching vanilla and avoiding a hidden-row update cycle.
* Added a full Shift-Alt-click vehicle-order hint to the vanilla assign tooltip and aligned the confirmation wording and action button on "Order".
* Fixed the full vehicle-order tooltip to resolve its target and depot on hover, after the inspector entity provider is initialized, while always preserving the vanilla floater.

v0.5.2 [released]
* Removed: Unwired the reverse BFS prototype and deleted AccessV2ReverseBfsHeuristic.cs.
* Fixed: Restricted vehicle pre-allocation UI patches to MineTower entities only, resolving the truck assignment issue on Tree Harvesters.
* Changed: V2 turns are now orientation-only transitions over an existing flat 2x2 landing. Flat and strafe successors are suppressed after a turn; the pending orientation may terminate or continue through either positive or negative uniform ramp, including ramp-up. Turn discovery also works after lateral flat strafes, with old-direction clearance rays retained and no duplicate landing terrain delta.
* Changed: Set default access height envelope allowances so both V1 are 1.0 (previously 1.0 lower and 0.5 upper) and both V2 are 1.5 (previously 2.0 lower and 1.0 upper).


v0.5.1 [released]
* Changed: Vehicle construction confirmations now use the same enqueue-for-tower wording, button text, and depot zoom tooltip as AFD.
* Fixed: useful-height hull pruning now extends every potential fixed start and fixed goal cone in the request before building the effective hull. Lower endpoint extensions default to `1.0` for V1 and `2.0` for V2; upper extensions remain `0.5` and `1.0`. Ramps can leave any eligible mining-designation start and retain local room for a flat landing and turn toward a high or low goal, while generated-center checks remain strict elsewhere. Large flat ground components no longer admit rising and falling G-to-V handoff candidates merely because of a global hull allowance. The existing session-only upper/lower commands tune these endpoint extensions independently, with captured values reported in settings and hull-build diagnostics.

v0.5.0 [released]
* Improved: Clear debris now shows a one-shot tower notification when no debris is found or when no reachable debris can be queued; the latter suggests Ctrl-clicking to include unreachable debris.
* Improved: Debris cleanup diagnostics now report discovery, reachability totals, enqueue and skip counts at Debug level, with per-prop origins, rejection reasons, and manager coalescing details at Trace level.
* Fixed: The Clear debris reachability filter now recognizes a connected approach within vanilla's prop-containing mining tolerance for the selected excavator—rather than a one-tile ring around the designation. A vehicle-blocking prop occupying its nearby pathability samples no longer prevents its own removal request from being queued.
* Fixed: Debris cleanup now retains flat, equal-height mining candidates with zero terrain movement, so level-ground props are removed without an unnecessary landscaping cut or a false no-candidate result. **Landscape to remove debris** now blocks only candidates that actually alter terrain, not these no-op cleanup designations.
* Fixed: T3 accessways now select leveling handoffs only against a smooth, target-compatible ground face. An isolated level crossing on rough ground no longer hides the immediate mining or dumping crest handoff from a valid start frontage.
* Fixed: T3 mining and dumping handoff corridors now validate the complete vehicle mask against terrain after terminal work and keep their outside center spoke in V until the Mega mask reaches a real captured ground node. A mining ramp is no longer rejected merely because its first outside centers still overlap the terminal work.
* Fixed: When only one T3 lane reaches a mining or dumping crest, V2 now first accepts an immediate exit wherever the full vehicle mask is supported by the worked frontage and its next center reaches captured ground within the post-work half-level limit. The terminal test uses operation-aware terrain and lets its proven post-work mask supersede snapshot exclusions caused by underlying terrain or durability, while removable non-tree props are carried into materialization for the prop-removal manager instead of being assumed cleared by Mining or Leveling. If the companion's far corner still prevents that exit, V2 can extend only the unfinished lane with the same operation and retry the combined post-work corridor.
* Fixed: In AUTO with existing terrain work, a mine-tower ghost becomes a goal only when every terrain-work cluster was already connected to the active tower when the scan was requested. Disconnected clusters are repaired toward active-tower ground first and cannot be redirected to a ghost by accessways placed during that request.
* Fixed: A valid two-cell T3 source frontage now exempts both of its existing work cells from their own projected side-ray blockers on the first straight route step. The search no longer stalls at the source and reports the unrelated first-move strafe restriction.
* Added: A world-scoped prop-removal manager can temporarily replace and later restore existing terrain designations while debris is mined or buried, publishes completion or failure results to requesting workflows, and survives saves through ATD's removable JSON state. Terrain candidates are ranked by temporary movement plus restoration cost; each candidate is placed and checked with vanilla's non-amphibious mining or dumping readiness rule before the manager accepts it.
* Improved: Prop-removal requests now enqueue without synchronously searching or placing terrain candidates. Candidate generation and one-at-a-time placement attempts run round-robin under a five-millisecond manager budget on the simulation thread; a direct origin index also avoids quadratic request submission for large debris areas. Save preparation remains synchronous so every temporary designation is restored before serialization.
* Improved: The Clear debris button now immediately adds a tower-linked **debris cleanup queued** notification, including while paused. The transient notification remains while that tower has pending manual cleanup requests, clears automatically when they finish or are cancelled, and is purged before saves.
* Fixed: The mine-tower Clear debris button now submits excavation-only, prop-specific removal requests through the manager instead of placing obsolete surface-plus-one mining designations. It never spends Unity on Quick remove, follows **Landscape to remove debris**, and automatically suspends and restores existing player terrain or forestry designations.
* Fixed: Temporary debris-removal designations now remain in place until vanilla releases the assigned mining job, allowing harvested prop resources to leave the excavator bucket normally instead of being cleared by premature job cancellation. Cleanup prefers an occupied cell containing an existing designation when a prop spans multiple designation cells, ensuring that designation is suspended and restored.
* Changed: Accessways ignore already buried props and classify every remaining prop against the exact final height at its position. A sufficiently burying dumping designation owns the prop directly; otherwise the prop-removal manager assists while the intended accessway remains approved. Manager failure leaves the accessway in place for manual prop removal instead of rejecting or unregistering the route. Known limitation: preparatory landscaping can invalidate the planned path, and that path is not yet replanned or revalidated after cleanup.
* Added: The accessway-only **Quick remove debris** policy offers Always, Restrictive, and Never under Accessways. Restrictive uses Quick remove only when the default accessway operation cannot remove the prop; Always queues Quick remove for every accessway prop except one already buried or sufficiently buried by the planned dumping designation. Insufficient Unity leaves a persisted Quick request pending for a once-per-second retry instead of failing or silently falling back to terrain work.
* Changed: Mine-tower and global defaults now use an **Accessway mode** dropdown with OFF, AUTO, T1, T2, T3, and explicit legacy straight-only widths 3-5. Old numeric ramp-width values and the public ramp-width API preserve widths 2-5 through the corresponding T3 or legacy mode, while routed accessways remain primary for AUTO and T1-T3.
* Changed: **Ore quality** now uses a five-position stepped slider from Off through Max in both the mine-tower panel and global defaults, without presenting the internal filtering thresholds as expected ore yield.
* Changed: Minimum supported Captain of Industry version is now 0.8.5. Older versions may still work, but are unsupported and untested.
* Fixed: repeated **Create Designations** clicks are now a true no-op while the tower settings, relevant world inputs, and terrain-designation layout remain unchanged. The completed plan fingerprint is captured after accessway generation and cleanup, while manually adding or clearing terrain designations changes that fingerprint and enables a new scan.
* Added: AUTO scans can use an unstarted mine control tower ghost as a natural accessway marker. When no terrain work already exists, ATD combines exact-terrain vehicle-pathable approaches at the active tower into one multi-source start, uses eligible ghost entrances as the immutable ground goals, and immediately commits the route that reaches a ghost. A recognized ghost claims the AUTO request even if routing or placement fails, so the request cannot fall through into an unrelated useful-product scan. Ghosts remain vanilla-owned and are never created, modified, removed, or persisted by ATD.
* Fixed: When AUTO preserves existing terrain work and an eligible mine-tower ghost is present, the terrain-work clusters are now the pathfinding starts and the ghost entrance replaces the active tower's radial ground nodes as the terminal goal. Reaching the ghost therefore ends and accepts the search instead of continuing past it toward the active tower.
* Fixed: Ghost-target searches may use exact-natural pathable source profiles around a home tower outside or straddling its managed-area boundary. These source profiles are reused ground and never materialized; generated landscaping remains restricted to the managed area.
* Fixed: Width-two post-placement validation now preserves whether each recorded seam is V-to-ground or ground-to-V. Ground-to-V seams validate their live contact and crest against the reversed edge instead of being misread as V-to-ground and rolling back a successfully materialized route.
* Added: ATD diagnostics now use configurable Warning, Info, Debug, and Trace levels. `Default` selects Debug in Debug builds and Info in Release builds; `ATDsettings.json` configures startup behavior and `atd_diagnostic_level` provides a session-only override. Warnings, errors, and the startup version/DLL timestamp remain unconditional. Expensive access-search timings and detailed successor, handoff, path, plan-tile, and cleanup-route payloads are collected or formatted only at their corresponding levels.
* Fixed: a side-ray corner whose planned height matches captured terrain within epsilon now remains a true zero-length `None` ray. Leveling no longer manufactures a cut-oriented end buffer at an exact match, which had made otherwise identical downhill V2 starts work from mining designations but reject dumping starts as `SideRayOpposingDesignationWork`.
* Improved: V2 source expansions now report every immediate flat and ramp successor in the existing start-successor diagnostics, including the concrete lane profiles, exact rejection or dominance stage, accepted transition cost breakdown, cumulative cost, and forced-ground flag. This makes unexpectedly delayed ramps distinguishable from clearance, ray-envelope, height-envelope, cost, and handoff effects without changing route selection.
* Fixed: V2 source frontage discovery now requires two compatible fixed origins that both belong to the current work cluster. A generated companion beside one incomplete cluster origin is not treated as a Mega-width work face, preventing T3 ramps whose apparent source connection is only a transverse single-origin adjacency. The retained synthetic-start evaluator still validates exterior clearance on both lanes for replay and fixture safety.
* Improved: V2 V-space expansion now applies a traversal-cost label-dominance check before history and terrain evaluation, repeats exact dominance before committing history, uses an allocation-free geometry preflight with one history commit, and skips full straight-transition ray-history scans when the active ray tile set cannot overlap the candidate footprint. Search diagnostics report early and exact label prunes.
* Fixed: V2 side-ray clearance dilation now records the generated lane that owns each ray. A direct forward continuation and that lane's V-to-G handoff may supersede only their own newest fringe, preventing shallow dirt fill rays from blocking the route that creates them while retaining older and unrelated ray constraints.
* Fixed: the V1/V2 V-to-G smooth-face leveling shortcut requires both the captured ground edge to be internally level and every sample of the candidate designation landing to equal that ground level within epsilon. A merely traversable or vertically remote landing no longer becomes a false prop-agnostic leveling handoff.
* Fixed: useful-height hull pruning now permits generated V1 centers up to `0.5` and V2 centers up to `1.0` beyond either the upper or lower hull, leaving room for the flat landing needed to turn an ascending or descending ramp toward a high or low fixed goal. The session-only `atd_access_height_envelope_v1_upper_allowance`, `atd_access_height_envelope_v1_lower_allowance`, `atd_access_height_envelope_v2_upper_allowance`, and `atd_access_height_envelope_v2_lower_allowance` commands accept decimal values for independent live tuning, with captured values reported in settings and hull-build diagnostics.
* Improved: V1 and V2 V-to-G searches now apply cost dominance between sibling slope profiles after a complete handoff succeeds. For identical predecessor and exit geometry, mining tests rank up, flat, then down while dumping ranks down, flat, then up; a strictly more aggressive candidate is pruned only when the recorded route is also no more expensive. This does not infer pathability for an untested profile and does not cross different contacts, spans, escape corridors, or ground entries.
* Improved: V2 rough G-to-V handoffs no longer run the multi-row internal corridor BFS. Every reached G derives one residue-selected companion band and exactly six candidates—mining from `ceil(h(G))` and dumping from `floor(h(G))`, each flat/rising/falling—then proves the complete vehicle-width post-work path cardinally from the future V face back to G. Face compatibility uses the inclusive `0.25` profile-step tolerance and bridge steps use the inclusive `0.5` vehicle limit. Dumping rejects each non-tree prop unless the candidate target at that prop's exact captured position rises strictly beyond its scaled burial threshold; mining ignores it. Successful concrete paired V states are request-locally cached with first-success ownership; replay independently repeats the one-row proof. V-to-G retains its general corridor evaluator.
* Improved: V1 and V2 handoffs in both directions now share the exact dumping-burial rule for non-tree props. Each distinct prop is sampled at its precise within-tile position and may be ignored only when the candidate surface rises strictly more than its scaled buried threshold above its placement height; materialization likewise omits cleanup only for props proven buried by the selected profile.
* Improved: V1 and V2 V-to-G handoffs use leveling immediately when the entire ground-facing height signature is smooth. This symmetric level-face case ignores props, as leveling work removes them, while retaining the ordinary terrain, history, clearance, and replay checks.
* Improved: V1 and V2 reverse leveling handoffs now use one sign-symmetric shared-edge rule. A captured G center on a canonical four-tile edge selects either adjacent V cell by orientation; V1 retains only its two middle transverse lanes and V2 adds the required companion. Flat/rising/falling profiles are placed at the exact G level, then pass the ordinary tower-area, profile, history, ray, cleanup, cost, and label-dominance checks before skipping only the general handoff evaluator. V1 does not cache this cheap proof; V2 folds accepted paired states into its request-local G-to-V ownership cache. V1 A* also validates and materializes an exact descending tower-ground suffix immediately after entering a goal-connected G component, falling back to ordinary G/V expansion if any history, cleanup, cost, or materialization check fails.
* Fixed: repeated legacy access-provider and ramp-mouth reachability checks now share one tower BFS flood per pathability refresh when their targets stay inside the tower bounds. Farming and mining planning invalidate the cache at phase, frame, pathability-refresh, and world-reset boundaries; out-of-bounds targets retain the original per-query BFS so routing behavior is unchanged.
* Fixed: V2 G/V transitions now apply V1's complete workability rule to every lane. The ground-facing edge alone selects mining or dumping and must not mix the two operations; the route direction and opposite-edge corners do not affect that choice. The selected vanilla delegate rebuilds the prospective designation's full 25-bit fulfillment bitmap, requires the operation to remain incomplete, and emits G contacts only at fulfilled perimeter bits that are valid ground nodes. Perimeter corners remain eligible, as in vanilla, and the two lane contacts must be consecutive before either the quick or general seam test can accept them. Quick-test then checks vehicle pathability through that already-proven continuous landscaped mouth instead of accepting a pathable center beside unrelated fulfilled samples. After placement, terminal lanes are independently rechecked through the live vanilla designation's non-amphibious mining or dumping readiness API and the selected contact bit on the retained ground-facing edge; a provider is rejected if either real bitmap or recorded contact is not ready.
* Fixed: A fully exact-terrain width-two band inside a V2 route now becomes an explicit G passage instead of remaining virtual V geometry that disappears during no-op materialization. The route must prove a workable V-to-G seam before the passage and a symmetric workable G-to-V seam before generated work resumes. Exact-terrain synthetic source companions remain valid because their immutable source seed anchors the initial frontage.
* Fixed: V2 strafes now require a predecessor slice and copy the newly exposed lane across both that slice and the current slice instead of generating only the current origin. Each sideways step therefore adds two origins and preserves a complete 2x3 swept footprint for Mega vehicles; consecutive strafes extend that footprint without narrowing. Both new profiles, cleanup, work, and exposed side rays are validated, costed, replayed, and materialized. On a flat landing where the corresponding turn is available, the equivalent strafe successor is pruned so one materialized terrain plan cannot receive different cost or blockage through duplicate graph representations.
* Added: V2 can now leave ground and enter generated width-two terrain through a symmetric G-to-V seam at any suitable G center, allowing a ramp to begin before the natural-terrain boundary when that reduces later landscaping. The edge pays the same canonical-center spoke as V-to-G, carries immutable generated history and cleanup forward, and supports alternating V/G route replay and materialization. Search diagnostics report both `v2g` and `g2v` seam counts.
* Fixed: V2 now uses the same label-dominance principle as V1 for both G and V: only the cheapest route to a concrete ground center or band state survives, and that winning route's history is retained for subsequent validation. Full history identity previously created hundreds of thousands of copies of the same ground pocket and repeatedly regenerated identical V bands. The disconnected-G heuristic also includes the guaranteed fixed overhead of the first two generated origins, so viable V continuations compete promptly with alternative G routes.
* Improved: V1 ground traversal now supports conservative diagonal steps at `sqrt(2)` cost, matching V2. A diagonal requires both orthogonal side corridors to be ordinary, situation-valid ground and clear of generated-history disturbance, so it cannot cut a blocked corner or silently omit side cleanup. Its A* goal fields now use the same octile metric, preserving an admissible heuristic and accurate traversal-cost diagnostics; materialization revalidates the same static corridor rule instead of rejecting a successful diagonal goal path.
* Fixed: V2 G states in components without a tower goal no longer receive heuristic zero. A request-scoped component-aware escape field minimizes real octile G travel within the local component plus the relaxed V potential at the best reachable exit point. Distinct V histories can still retain legitimate local G states, but disconnected ground floods no longer outrank productive V progress. The V potential now covers the full captured G bounds so outside-area nodes cannot create another zero basin.
* Fixed: V1 A* now extends its goal-distance grid over every captured traversable and cleanup-qualified G tile, including the clear search margin outside the tower area's bounding rectangle. Outside-area G traversal therefore retains a real distance-to-goal estimate instead of dropping to heuristic zero and receiving misleading priority; V-generation and cleanup authority remain restricted to the tower area.
* Improved: V1 A* now breaks equal `g+h` priorities by preferring lower remaining `h`, matching V2. Long ground plateaus therefore advance toward the goal instead of prioritizing lower-`g` nodes near the start and spreading as a broad wavefront; Dijkstra ordering is unchanged because its heuristic is zero.
* Improved: V2's relaxed A* potential now models future V space with cardinal-only propagation at the proven minimum V rate `1 + generated-flat-cost / 4` per tile. Every G/V center spoke costs twice that same rate, so a handoff cannot undercut ordinary V traversal and the weighted heuristic remains admissible without a special handoff halo. Explicit G states and accepted-provider distance fields retain exact conservative octile ground distance.
* Improved: V2 ground traversal now admits conservative diagonal moves at cost `sqrt(2)`. Both orthogonal side corridors must be traversable, projected-history-valid, and cleanup-qualified, preventing corner cutting by the resolved Mega mask. Exact G distances, explicit G states, and fixed-provider downstream fees use the same octile metric; ground-travel diagnostics now report the resulting fractional cost. Once G has a heading, exact reversals and 45-degree backward diagonals are pruned as strictly dominated, while independent materialization replay revalidates every retained cardinal/diagonal edge and swept corridor.
* Improved: V2 A* breaks equal `g+h` priorities by preferring the state with lower remaining heuristic distance. Exact-cost ground plateaus therefore advance toward the tower goal instead of spreading broadly across every equally short route; Dijkstra retains FIFO ordering because its heuristic is zero.
* Changed: accepted V2 fixed-provider frontages now carry a downstream travel fee. Each cluster request rebuilds an optimistic provider-distance field over only the already accepted designation/accessway profiles, joins it to the exact tower-ground distance field, charges the final four-tile provider-entry slice, and uses the identical total as the fixed-goal A* potential seed. The fee is a real queued terminal edge, so Dijkstra and A* can both reject a nearby frontage whose established route to the tower is more expensive.
* Added: V2 A* now uses a request-scoped multi-source potential field for V states. Exact tower-ground G distances and concrete fixed-frontage match centers seed a relaxed cardinal field over the request bounds, so combined and fixed-provider searches no longer fall back to Dijkstra. Explicit G states retain their exact ground-graph distance until G-to-V transitions exist. Fixed-frontage A* fixtures reproduce the Dijkstra route and exact cost.
* Fixed: V2 V/G handoffs now enter explicit ground-search states instead of collapsing the entire G suffix into a terminal precomputed-distance macro-edge. Quick handoff acceptance is local and no longer requires ordinary ground or finite tower-goal distance; G expands cardinally with the candidate history, unit travel cost, projected blockers, and deduplicated cleanup until it reaches a real goal. Route replay retains the complete traversed G suffix and all two-lane seam cleanup.
* Documented: a future V1 quick-handoff optimization will test the two center vehicle lanes through the terminal designation and one step onto situation-qualified outside ground, while retaining the complete V1 handoff evaluator as fallback.
* Added: `cursorOverlayEnabled` in `ATDsettings.json` controls whether the bottom-left `(x, y, z)` cursor-coordinate overlay is enabled at game start. It defaults off; `atd_cursor_overlay` remains a session toggle and `atd_save_settings` persists its current value.
* Fixed: V2 G/V handoffs now validate projected post-landscaping steepness over every tile in the full resolved vehicle footprint at every real contact and escape-corridor center. Escape continues until the entire vehicle mask, rather than merely its center, no longer overlaps projected terrain work. A currently pathable ground center can no longer qualify when the finished ramp edge and neighboring natural terrain would form a Mega-inaccessible waist; the canonical-center spoke remains only the accepted cost/heuristic abstraction.
* Added: V2 tower-ground searches now use A* with an admissible canonical-center Manhattan lower bound to the sparse tower goals. Exact shortest traversable G distance is restored to the primary traversal and total cost, so T3 routes pay one cost per ground tile as well as four per full V move. Fixed-provider searches retain the Dijkstra fallback until they have a proven multi-source heuristic.
* Added: V2 now short-circuits the common forward handoff when any pre-approved center lane is locally situation-pathable and remains clear of candidate-history profiles and rays in the outward vehicle footprint. Snapshot designations, projected work, buildings, ocean, props, trees, terrain, and full vehicle clearance are represented by the ground graph and projected validator. History-affected, lateral, diagonal, and multi-row cases retain the general seam solver. Search summaries report quick versus general handoff counts.
* Fixed: the full-mask V2 handoff escape proof is a bounded continuation of the already-selected physical exit corridor rather than a nested BFS. This retains post-landscaping Mega-width validation without collapsing search throughput.
* Fixed: projected V2 handoff footprint checks flatten each immutable route history once per evaluated state and reuse direct profile lookups, instead of repeatedly walking the history chain for every terrain sample.
* Fixed: a level outer handoff boundary no longer forces a leveling terminal before operation inference. Uniformly excavated or filled ramp-side edges now retain mining or dumping terminals; leveling remains the fallback for genuinely mixed or entirely level seams.
* Fixed: removable props and trees no longer make intrinsically unpathable terrain appear as cleanup-eligible ground. Cleanup candidates now reuse the resolved vehicle's vanilla clearance mask with only the generic object-blocking bit removed, retaining slope, height-clearance, ocean, building, designation, and durability checks. This applies to both V1 and V2 handoffs; generated V terrain work may still repair the underlying terrain.
* Fixed: V2 placement no longer creates a relocated dense-debris cleanup designation on an origin already occupied by the accepted generated accessway. The accessway's terrain work remains the prop-removal mechanism, avoiding a self-inflicted `DesignationAppeared` rollback when connecting full generated mining bodies.
* Added: V2 Stage 6 now replays successful width-two routes against the unchanged snapshot, materializes unique generated profiles, omits exact-terrain no-ops while retaining cleanup, assigns terminal prototypes independently by lane/span, and uses the existing transactional terrain/cleanup/tree placement and primitive ownership paths. Post-placement validation checks every emitted profile/prototype and the retained Mega seam before accepting the provider.
* Added: V2 Stage 5 dry runs can now terminate through a width-two G/V seam. Both lane contacts use vanilla prospective workability, forward handoffs share a one-to-three-row span, aligned bands can exit laterally, both escape paths must join one tower-reachable Mega ground component, cleanup is deduplicated, and the graph charges the canonical-center spoke. The selected seam remains diagnostic-only; V2 placement is still disabled pending Stage 6.
* Changed: ATD decimal log values now use invariant formatting with at most two fractional digits, reducing ambiguity between decimal and thousands separators in diagnostics.
* Added: V2 route summaries now split generated-work cost into direct terrain shaping, per-origin designation overhead, and exterior durability rays, making the primary cost drivers visible in live-test diagnostics.
* Fixed: experimental access graph width now derives from the resolved vehicle mask rather than the obsolete legacy ramp-width value. AUTO resolving to a Mega/T3 therefore dispatches width-two V2 even when the migrated legacy width remains one, preventing an unintended single-lane V1 accessway.
* Added: V2 Stage 4 now runs a separate incremental Dijkstra dry-run graph for width-two fixed-frontage routes. It expands uniform straight/ramp bands, retained-lane strafes, and explicit flat 2x2 turns; immutable delta history carries generated profiles, deduplicated cleanup keys, and elevation-aware exterior-ray constraints. Production costing uses four-corner work, generated-origin overhead, dense-debris cleanup, two exterior band rays, and all three old-direction turn rays. Found routes report `V2DryRunRouteFound` and remain unable to materialize or place designations pending the Stage 5 G/V seam.
* Fixed: external one-origin terrain-work requests now reach V2 frontage discovery under explicit T3 clearance. The repair path no longer silently rejects width two through its stale V1-only width gate.
* Fixed: projected disturbance from the fixed designation that directly seeds a generated accessway successor is now source-attributed and exempted only for that connected predecessor. Unrelated projected work on the same tile still blocks, while trivial ramps no longer need a flat lead-in merely to avoid their own seed's disturbance rays.
* Added: candidate-level V1 start diagnostics record every immediate fixed-seed successor profile and rejection stage, plus raw and accepted handoffs from first-generation V cells, to diagnose apparently redundant flat lead-ins.
* Changed: the World settings UI now presents one **Safety policy** control (`MIN`, `LOW`, `MED`, `HIGH`, or `MAX`) instead of separate landslide slope and buffer parameters. The underlying expert values remain editable in `ATDsettings.json` and through console commands.
* Added: V2 Stage 3 now converts one-origin work endpoints into concrete width-two start frontages, using compatible fixed companions where present and otherwise retaining a side-effect-free synthetic companion candidate. Adjacent fixed goals are admitted only with an exposed two-origin outer edge; categorized `[ATD V2 Frontages]` diagnostics precede the still-disabled V2 graph.
* Fixed: width-two experimental requests now pass the ramp-generation orchestration gate and reach the shared V2 snapshot/dispatch path instead of being returned early by the obsolete V1-only width guard.
* Fixed: the live access snapshot transition fixture now recognizes width-two requests as V2 dispatches instead of failing them under the obsolete V1 `UnsupportedWidth` expectation.
* Added: V2 Stages 0–1 now implement the accepted two-origin band model, concrete enabled/deferred profile-pair classification, straight and retained-lane strafe deltas, flat 2x2 turn landings with three old-direction frontage rays, delta-owned history, nonlocal-contact rules, bounds, and an out-of-process pure fixture runner. Stage 2 now captures a concrete width-five Mega ground graph and per-center cleanup overlay with cleanup-object deduplication, emits `[ATD V2 Ground Graph]` diagnostics, and deliberately returns `V2GraphNotEnabled`; V2 search and placement remain disabled until the live snapshot gate is validated.
* Changed: V1 access search now rejects generated origins that make nonlocal cardinal edge contact with earlier generated history before expensive ray costing. The immediate generated predecessor remains legal, as does diagonal corner contact; the V2 design carries the same rule with explicit exceptions for retained strafe lanes, same-delta lanes, turn landings, and bounded handoff spans.
* Fixed: **Avoid buildings** now refreshes static-building occupancy for every explicit accessway request and captures nearby footprints beyond the tower boundary, including the outside portion of buildings that straddle it. Building snapshots now log entity/tile counts and capture bounds for field diagnostics.
* Changed: accessway pathfinding now treats trees as cost-free removable obstacles. Tree metadata is retained only to mark required or disrupted trees after the route is selected, reducing forest-driven route bias; the now-unused **Tree harvest cost** setting and JSON output were removed, while legacy JSON values are harmlessly ignored.
* Documented: reviewed V2 requirements against the current V1 implementation and added a separate staged implementation plan. Accepted decisions now include the concrete two-origin band state, direct strafe with one immutable uncharged retained lane and one newly generated lane, 2x2 flat turn landings and their three forward rays, synthetic companions for one-origin targets, local two-origin fixed frontages, canonical-center G/V costing and A*, common handoff spans, full perimeter scoring, and V2-only fixture isolation. No semantic design blocker remains before Stage 0. Rare mining-body waists are tracked as a post-rollout refinement rather than a V2 release gate; core Mining Designations integration for Avoid ocean, Avoid buildings, and Harvest disrupted trees remains in the refinement roadmap.
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
