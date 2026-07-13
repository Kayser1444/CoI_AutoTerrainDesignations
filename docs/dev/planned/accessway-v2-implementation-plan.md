# Accessway V2 review and staged implementation plan

Status: design review and implementation plan, not yet implemented  
Reviewed: 2026-07-13

## Purpose

V2 is the width-two access search used for five-tile-clearance Mega/T3 vehicles. It remains in the vanilla designation set: flat and axis-aligned slope profiles only. Corner and saddle designation search spaces (`V'` and `V''`) are separate future work.

This review reconciles the earlier V2 notes with the production V1 implementation and the decisions made while testing V1. It identifies one blocking representation problem, records the current requirements, and breaks implementation into independently testable stages.

## Sources reviewed

* `docs/dev/planned/accessway-pathfinding.md`
* `docs/dev/planned/accessway-implementation-sequence.md`
* `docs/dev/planned/accessway-pathfinding-side-ray-cost.md`
* `docs/dev/planned/accessway-pathfinding-debris.md`
* `docs/dev/in-progress/access-framework.md`
* `docs/dev/in-progress/access-prop-cleanup-handoff.md`
* Current `Access/` search, snapshot, materialization, and placement code

## Current baseline that V2 must preserve

V2 is an extension of the current V1 behavior, not of the earlier MVP described in parts of the planning notes. In particular:

* T1 and T2 use width one with their concrete vanilla pathability masks. T3/Mega resolves to five-tile horizontal clearance and is currently rejected before V1 snapshot completion with `ExperimentalAccesswayWidthInsufficient`.
* `AccessPathRequest.RequiredWidth` exists, but `AccessPathSearch.CreateSession` accepts only width one.
* AUTO vehicle selection already pools assigned, pre-assigned, idle/released, and map vehicles and selects the widest concrete excavator profile. No vehicles means OFF.
* G traversal uses the selected vanilla vehicle mask and projected disturbance blockers. Generated V traversal uses concrete target elevation, material-aware candidate rays, and elevation-aware prior-ray envelopes.
* Candidate work cost is no longer the old center-height estimate. V1 uses operation-aware four-corner direct work, exterior and turn rays, unresolved penalties, cleanup cost, traversal cost, and a generated-V overhead.
* Ocean avoidance applies to cutting; dumping into ocean is allowed. Cutting rays must reach terrain and dry `+1`, then apply the shared end buffer.
* Buildings block direct occupancy/clearance and candidate terrain disturbance, but do not generate G hourglasses.
* V/G handoffs support lateral exits, a usable single-corner crest in the eligible corridor, vanilla prospective workability, and bounded multi-cell handoff spans. The current maximum is `1 + ceil(vehicle width / 4)`, which is three cells for T3.
* Exact-terrain V cells are valid search geometry but are omitted during materialization. Required prop cleanup is emitted separately.
* Tree and dense-debris cleanup are separate from terrain profiles. **Harvest disrupted trees** can additionally select every tree in the finalized accessway footprint and disturbance rays.
* Placement is transactional and tower ownership persists as primitive coordinates. Both trashcan actions remove the tower's generated terrain and harvest markers without removing unrelated player harvest orders.
* A* and Dijkstra share the same graph and cost model; Dijkstra remains the optimality reference. Search is asynchronous and single-flight.

Any V2 implementation that falls back to center-only work, boolean V-ray blocking, V1 G masks, strict forward-only handoffs, or unconditional no-op terrain designations would be a regression.

## Confirmed V2 requirements

### Geometry and clearance

* V2 provides a two-origin-wide terrain band: eight designated tiles, giving margin over the five-tile Mega footprint.
* Generated movement remains axis-aligned in four-tile origin steps.
* Generated slopes remain vanilla flat/slope profiles with a maximum constructed grade of one height level per four tiles.
* Turns require a full flat 2x2-origin landing. A slope-to-slope axis change without that landing is illegal.
* The whole width-two footprint, including a turn landing, must remain inside the tower's designation area.
* Straight travel, lateral strafe, and turns must preserve shared origin profiles and shared corner heights exactly.
* Mixed-axis origin shapes, corner designations, and saddles are out of scope.

### G traversal and endpoints

* Width-two G occupancy, G adjacency, tower-reachable flood, goals, cleanup overlay, and post-placement verification must all use the concrete Mega/T3 pathability parameters.
* A V2/G seam must expose two consecutive workable lanes connected to the same clearance-two G component.
* G-to-V2 and V2-to-G are symmetric graph operations, even if the mine-tower caller usually starts at fixed V work and ends at tower ground.
* Fixed providers are reusable only when two consecutive compatible profiles form a Mega-reachable provider frontage.
* A width-one work endpoint is a valid V2 **seed** when the search can synthesize a compatible adjacent companion lane. It does not become a reusable fixed provider unless a real width-two frontage and Mega-reachable continuation exist.

### Terrain work, hazards, and props

* Every newly introduced origin is checked with the current four-corner work and material-aware ray scorer.
* Rays from internal lane seams are suppressed; only the exterior perimeter and exposed turn corners create side disturbance.
* Existing-designation disturbance remains exact for G and elevation-aware for V.
* Avoid ocean, Avoid buildings, designation conflicts, prior generated history, candidate ray limits, and the shared ray buffer apply at width two.
* Cleanup G validates the whole Mega footprint. Cleanup cost is charged once per cleanup object/origin, not once per lane or overlapping footprint sample.
* A non-cleanup blocker in any required footprint tile rejects the state.
* Harvest-disrupted-tree selection covers the finalized width-two terrain footprint and exterior disturbance rays.

### Cost, materialization, and ownership

* Search charges only work newly introduced by a transition. State context may reference the current frontier, but a transition may not reintroduce an origin already present in that candidate history.
* The final plan is a unique mapping from origin to concrete `AccessHeightProfile`, independent of how many search states touched that origin.
* Any generated-origin revisit is illegal and pruned before costing, whether its profile is identical or conflicting. Returning to an identical origin is strictly dominated by the earlier occurrence and must not be explored. This is the same invariant already enforced by V1 `GeneratedPathHistory.ContainsOrigin()`.
* Materialization omits exact-terrain no-op profiles and retains required cleanup metadata.
* Terminal lanes may use their required mining or dumping proto independently, provided shared corners and the combined seam remain valid.
* Rollback and persisted ownership cover all generated lane designations, cleanup designations, and newly selected tree markers.

## Blocking design gap: the current node definition is under-specified

The existing notes alternately describe V2 as:

* a 2x2-origin brush containing four origins;
* a node whose cost footprint contains two origins, one per lane; and
* `(brushVertex, h, axis, profile-string)` with only two profile tokens such as `FX+`.

Two tokens and one reference height do not uniquely define four origin profiles. The ambiguity affects every downstream operation: shared-strip compatibility, lane heights, work cost, ray ownership, turn geometry, duplicate-origin handling, and materialization.

Several listed profiles are also not automatically legal. For example, `FX+` or `X+X-` may fight across the lane seam depending on travel axis and relative lane heights. A single shared `h` cannot express all compatible relative offsets. The legal set must be derived from concrete corner profiles, not accepted by token name alone.

### Recommended resolution: width-two band slices

Represent a V2 generated state as a two-origin cross-section:

```text
V2BandState = (anchor, travelAxis, lane0Profile, lane1Profile, entryDirection)
```

This state is the current two-origin **frontier slice**, not the complete accessway. The immutable search history owns all previously introduced origins behind it.

* `anchor` is the 4-tile-grid-aligned `(x, y)` designation origin of lane 0 (`Tile2i`). It is always the lower coordinate on the transverse axis, giving the same physical slice one canonical representation.
* `travelAxis` is unsigned: `X` or `Y`. It says which axis a straight successor advances along, not whether travel is positive or negative.
* `entryDirection` is the signed cardinal step by which the frontier was entered: `(+4, 0)`, `(-4, 0)`, `(0, +4)`, or `(0, -4)`. It retains approach direction for turn geometry and direction-dependent ray cost.
* For `travelAxis = X`, lane 0 begins at `anchor` and lane 1 at `anchor + (0, 4)`. For `travelAxis = Y`, lane 1 is at `anchor + (4, 0)`. Thus the lane profiles always describe the two adjacent origins transverse to travel.
* `anchor` alone is neither the search-state identity nor the used-origin key. The queue key is the complete `V2BandState`; separately, both concrete lane origins are checked against candidate history. A route may geometrically return to the same anchor coordinate with a different axis, but it is rejected if either derived lane origin was already introduced. For example, an `X` frontier and a later `Y` frontier with the same lane-0 anchor both contain that anchor as a designation origin, so the latter is an illegal revisit.

`lane0Profile` and `lane1Profile` are concrete `AccessHeightProfile` values: the complete `(NW, NE, SE, SW)` target-elevation tuple for that lane's 4x4 designation origin. The stored fields are `(Nw2, Ne2, Se2, Sw2)` in half-level units, so the physical corner elevations are those integers divided by two. The tuple determines every shared-edge height and the bilinearly interpolated target surface; it is not merely a mode name such as `F`, `X+`, or `Y-` plus one shared height. For example, an east-rising designation from level 0 to level 1 is `(0, 2, 2, 0)` in stored units. The anchor has one canonical definition per travel axis and lane order.

* A straight transition advances one origin step and introduces one new two-origin slice.
* A strafe shifts the slice laterally and validates the changed band footprint.
* A turn is a special transition available only when the current and immediately preceding slices are both completely flat. Those two slices form the required 2x2-origin landing. The turn validates the appropriate perpendicular edge of that landing and advances directly to the first new outgoing slice; it does not enqueue an orientation-only frontier made from already introduced landing origins.
* Search history owns a unique origin/profile map. Every transition adds previously unused origins; costs and materialization are computed only for that addition.

Worked clockwise example, using `+X = east` and `+Y = south`: let the current eastbound frontier anchor be `A`, with the preceding anchor at `A + (-4, 0)`. Their four flat origins form the 2x2 landing. The landing's implicit south-facing frontier is anchored at `A + (-4, 4)`, but both of its origins already belong to the landing and are therefore not enqueued. The first new `Y`, `entryDirection = (0, +4)` frontier is anchored at `A + (-4, 8)`, with origins `A + (-4, 8)` and `A + (0, 8)`. Therefore `A + (4, 4)` is not the successor anchor under this convention; it would attach an outgoing slice to only the landing's east side rather than continue from its full south edge.

The turn must also retain the incoming frontier's forward disturbance. Cast three material-aware durability rays in the **old** travel direction from the three vertices across the predecessor frontier's two-origin forward face: both endpoints and the shared-lane midpoint. In the eastbound example these start at `A + (4, 0)`, `A + (4, 4)`, and `A + (4, 8)` and all travel in `+X`. This is the V1 turn-forward/outer-corner ray extended with the other full-width endpoint (the true outer corner for the second lane) and the predecessor-frontier midpoint. All three participate in feasibility, cost, elevation-aware generated history, finalized disturbance, and disrupted-tree harvesting; vehicle-clearance thickening does not replace any of the three source rays.

A geometric center anchor was considered but is not recommended for the band-slice model. The center of a two-origin slice has different lattice offsets for `X` and `Y` orientation, while the convenient grid-aligned center exists only for the temporary 2x2 turn landing. It would make some coordinate traces more symmetric but would not prevent loops or replace concrete-origin history checks. Using lane 0 keeps origin derivation and materialization direct.

This model satisfies the physical width and turn requirement without a four-origin sliding state. It also avoids carrying and reconsidering already introduced origins as part of every step.

The alternative is a true four-origin brush state. If retained, it must store all four concrete profiles (or two full cross-sections) while distinguishing frontier context from newly introduced origins. A successor that would reintroduce any origin from its candidate history is pruned, even when the profile matches. The larger state is more error-prone and should be chosen only if band-slice fixtures expose geometry that cannot be represented safely.

## Other gaps, conflicts, and ambiguities

### 1. Initial profile set

The prose whitelist (`FF`, `FX+`, uniform ramps, and opposed pairs) is not sufficient as a correctness definition. The physical lane order, travel axis, independent center heights, shared seam, and transverse drivability decide legality.

Recommendation: enumerate pairs of concrete V1 profiles within a small relative-height range, retain only pairs whose shared edge matches and whose five-tile center corridor is drivable, then classify them for diagnostics. Start production search with flat pairs and uniform same-sign ramps. Enable asymmetric and opposed pairs only after dedicated fixtures prove they add valid routes.

### 2. Meaning of axis at a flat state

An `FF` slice is geometrically axis-neutral, but route state needs an orientation to define lane order and the next footprint. It is unclear whether changing orientation is a zero-cost state change or part of a turn transition.

Recommendation: keep orientation in the state key and permit an axis change only through the explicit flat-turn transition above. It validates the already charged 2x2 landing, then costs only the first new outgoing slice and newly exposed exterior rays. Do not add free in-place orientation edges or enqueue a frontier composed of already introduced landing origins.

### 3. Start frontage and narrow mining bodies

Current clusters and fixed endpoints are origin-based. Requiring players to paint a two-origin target merely to request a Mega accessway would be poor UX. A single existing designation cannot by itself expose a reusable width-two frontage, but it can anchor one lane of a newly generated V2 start. Generated mining bodies may also contain waists or one-origin corridors that a Mega excavator cannot traverse.

Recommendation:

* Treat a single external/player designation as an immutable fixed seed profile. Enumerate all transverse companion positions and both travel signs. An already existing compatible neighbor may supply the second lane; otherwise solve and generate a companion `AccessHeightProfile` whose shared edge exactly matches the seed and whose shape belongs to the enabled V2 profile set.
* The fixed seed and its companion form the initial two-origin frontier. Validate the full Mega footprint, shared corners, bounds, props, buildings, designation conflicts, and exterior rays before admitting it. Existing-work disturbance from the seed remains authoritative, with only the connected shared seam exempted from self-conflict.
* Never alter or take ownership of the player's seed. A synthetic companion is costed like ordinary generated V work and is materialized and owned by ATD only if its route wins. Failed candidates leave no designation behind.
* If every companion candidate is blocked or profile-incompatible, fail with a specific start reason such as `NoWidth2StartCompanion`, including orientation/rejection diagnostics. Do not ask the player to widen the target manually and do not fall back to V1 for a T3 request.
* A single-origin seed is start-only. It must not be advertised as a fixed provider that later clusters can reuse; provider reuse still requires a genuine paired frontage and Mega-reachable continuation behind it.
* Rare one-origin waists inside ATD-generated mining bodies are accepted as an initial V2 limitation. A later mining-body clearance pass should widen or remove them, but it is not a V2 rollout blocker.

### 4. Width-two handoff rule

The older design says each lane runs a V1 handoff and mixed mining/dumping lanes are allowed. Recent V1 behavior permits a diagonal-style handoff after one usable corner crests, plus a bounded longitudinal extension. It is not defined whether V2 needs one crest across the entire seam or one per lane.

Recommendation: require at least one clearance-eligible, vanilla-workable crest/contact in each lane and prove a continuous five-tile Mega exit corridor beyond the combined seam. Allow mixed lane operations because the lanes materialize as separate origins. Allow any brush side as an exit, not only forward. Generalize the bounded extension to a two-lane strip of at most three rows for T3.

Open detail: whether a two-lane extension may use different span lengths per lane. Start with one common span length; asymmetric spans complicate escape validation and visual output.

### 5. Search-history representation

V1 history is a linked sequence of single generated origins plus ray envelopes and categorically rejects an origin already present in the path. V2 keeps that invariant. A multi-origin band/brush may retain the current frontier as state context for seam validation, but this is not a new visit: a successor's generated delta must contain only origins absent from the complete candidate history.

Recommendation: V2 history stores or structurally shares:

* `origin -> concrete profile`;
* the set of previously introduced generated origins, used to reject revisits before scoring;
* distinct charged cleanup object/origin keys;
* exterior disturbed tiles and elevation envelopes; and
* current exposed band frontier.

Any origin already in the history rejects the transition before profile comparison or costing. Persistent immutable maps or parent-plus-delta nodes should be benchmarked before choosing a representation.

### 6. Exterior rays at straight and turning bands

Calling the V1 scorer independently for both lanes would cast rays from the internal seam and exaggerate cost/blocking. Turns also have an inside corner, outside corner, and potentially newly exposed flat landing edges.

Recommendation: build a band perimeter first, classify each perimeter edge by work operation and exposure, and score only exterior rays. Keep direct work per newly introduced origin. A turn transition reuses the already scored flat landing only as validation context, then scores the first new outgoing slice, the exterior corners newly exposed by the changed direction, and the three old-direction forward rays across the incoming frontier described above.

Open detail: define whether safety-buffer tiles count as “disrupted” for harvesting. Current V1 `DisturbedRayTiles` includes the end buffer, so preserving current behavior means yes.

### 7. Fixed providers and provider goals

Current fixed-profile goals are individual origins. V2 needs paired exposed edges and Mega-reachable continuation. A pair of compatible profiles is not sufficient if the provider narrows immediately behind it.

Recommendation: precompute width-two fixed-provider frontage states from the same Mega reachability graph used for G, and use those frontage states as fixed goals. Do not pair arbitrary neighboring fixed origins during expansion.

### 8. Goal seeding and A* lower bounds

Current tower goals are sparse radial G goals. For V2 these can remain center positions only if they belong to the same clearance-two G graph and are not isolated docking pockets. The V1 paired height/distance heuristic is not automatically admissible for a two-origin footprint or fixed-provider band.

Recommendation: run V2 Dijkstra first. Add A* only after it reproduces Dijkstra. Compute horizontal distance from the nearest occupied point of the band to a concrete goal and pair it with a travel-safe height lower bound for that same goal. Fixed-provider goals need a multi-source lower-bound index or Dijkstra fallback.

### 9. Cost note is stale

The older V2 text permits a center-point landscaping estimate. Production V1 already has more accurate four-corner and ray costing. Reintroducing center-only cost would repeat the route-quality failures already corrected in V1.

Recommendation: sum direct work for newly introduced concrete origins and calculate rays from the external band perimeter from the first V2 search. A simpler scorer may be used only in pure geometry fixtures, never in production candidate comparison.

### 10. Frame budget and state growth

The historical notes mention Dijkstra-first validation and asynchronous search. The current configurable default is 30 ms per frame, while an earlier design discussion proposed a hard-wired 20 ms budget. V2 has materially more states and larger histories.

Recommendation: keep the existing configurable asynchronous budget and single-flight cancellation; do not hard-wire a second V2 budget. Decide separately whether the public default should return to 20 ms. Add V2-specific counters for band states, profile-pair candidates, history deltas, and memory/high-water estimates.

### 11. Fatal startup self-tests

The current transition self-test aborts every production snapshot if a fixture becomes stale. Recent no-op materialization changes exposed this failure mode twice. V2 will add many more geometry fixtures.

Recommendation: keep a very small fatal invariant test, but move the full V2 fixture matrix to an explicit development/test entry point or make failures disable only V2 with one precise diagnostic. A V2 fixture failure must not disable V1.

### 12. Legacy comparison and unsupported widths

Generated-mining clusters can currently compare V1 with the legacy generator, while external terrain-work endpoints rely on the generic path and cannot safely use the mining-specific legacy fallback.

Recommendation: while V2 is experimental, generated-mining clusters may compare V2 with a genuine width-two legacy candidate if one exists. External endpoints report the V2 reason directly. Never silently run V1 for a T3 request.

### 13. Documentation drift

Some older notes still describe center classification for handoffs, hourglasses as the authoritative generated-V blocker, ocean avoidance for both cut and fill, and AUTO fallback behavior that predates the current vehicle pool. Those statements should be updated after the V2 representation decision so implementation is not guided by obsolete V1 behavior.

## Decisions required before production graph work

| Decision | Recommendation | Blocking? |
|---|---|---|
| State footprint | Two-origin band slice; 2x2 union only for turns | Yes |
| Lane height representation | Two concrete `AccessHeightProfile`s; no single shared `h` | Yes |
| Initial profile pairs | Flat + uniform same-sign first; gate mixed/opposed pairs behind fixtures | Yes |
| V2 seam crest | At least one workable contact per lane plus continuous Mega corridor | Yes |
| Terminal operation | Per-lane mining/dumping allowed | Yes |
| Handoff extension | Common span, two lanes wide, maximum three rows for T3 | Yes |
| One-wide external start | Keep target immutable; synthesize and cost a compatible companion lane | Yes |
| Mining-body waists | Accept as a rare limitation; add a generated-mining clearance pass later | Future refinement |
| Ray-buffer tree harvesting | Preserve current behavior: buffer is included | No |
| V2 A* | Dijkstra oracle first, A* later | No |
| Centerline-thicken | Diagnostic comparator only, if useful | No |
| V2 fixture failure | Disable V2 only; V1 remains available | Strong recommendation |

## Staged implementation plan

Each stage must build and be reviewable independently. Width two remains explicitly unsupported in production until the stage's exit gate says otherwise.

### Stage 0 — Close and codify the geometry design

* Adopt either the recommended band-slice state or a fully specified four-profile brush.
* Define canonical anchor, lane order, travel axis, entry direction, and turn footprint with coordinate examples for X+/X-/Y+/Y- travel.
* Define profile-pair generation from concrete corner profiles and remove token names as the source of legality.
* Define common-span width-two handoff semantics and per-lane operation rules.
* Update the older pathfinding/framework notes to reference this decision.

Exit gate: four worked examples (flat straight, uniform ramp, strafe, 90-degree flat turn) map unambiguously to origin coordinates and corner heights in both positive and negative directions.

### Stage 1 — Pure V2 geometry and fixture surface

* Add V2 geometry types separate from `AccessSearchNode` and `AccessHeightProfile` state identity.
* Enumerate mechanically valid lane-profile pairs.
* Implement shared-seam compatibility, straight advance, strafe, flat-turn union, bounds, and origin/profile addition with an early used-origin rejection.
* Add fixtures for direction symmetry, relative lane heights, illegal fights, opposed pairs, turn landing size, all three old-direction turn rays under four rotational symmetries, identical-origin revisit, and conflicting-origin revisit.
* Make full V2 fixture failure disable V2 only.

Exit gate: pure fixtures prove every emitted origin profile has integer corners, all shared corners agree, every transition introduces a deterministic origin/profile delta, and no transition double-owns an origin.

### Stage 2 — Clearance-two G graph and cleanup overlay

* Refactor snapshot construction so the resolved vehicle parameters drive G occupancy and adjacency without the current `vehicleClearance > 4` early exit.
* Build the Mega/T3 G graph, tower flood, sparse radial goals, and exclusion diagnostics.
* Lift cleanup G from origin metadata to full Mega footprint validation.
* Deduplicate cleanup cost keys across footprint tiles.
* Keep `AccessPathRequest` width two returning `V2GraphNotEnabled` after snapshot validation.

Exit gate: fixtures and a dry-run diagnostic distinguish pathable Mega ground, T1-only ground, cleanup-eligible Mega ground, isolated docking pockets, and non-cleanup blockers.

### Stage 3 — Width-two starts and fixed-provider frontages

* Convert clusters into candidate width-two start frontages.
* For each single-origin external target, enumerate fixed or synthetic companion-lane starts in every orientation without modifying the target.
* Precompute compatible fixed-provider frontage states and their Mega-reachable continuation.
* Emit explicit reasons for missing/blocked companion profiles, narrow generated clusters, provider pinch points, and out-of-area frontages.
* Do not rewrite external/player terrain designations.

Exit gate: paired flat/ramp starts and fixed providers are recognized in all orientations; a one-wide player endpoint produces valid fixed-plus-synthetic start candidates in every feasible orientation, owns only the winning companion, and fails diagnostically only when no companion is feasible, without falling back to V1.

### Stage 4 — V2 Dijkstra graph, history, and production cost

* Add a V2 search session/adapter selected by `RequiredWidth == 2`.
* Implement immutable origin/profile history with early origin-revisit rejection and cleanup-key deduplication.
* Expand straight, strafe, and flat-turn transitions.
* Apply current profile feasibility, bounds, designation fights, elevation-aware prior rays, ocean/building rules, and candidate ray limits to every newly introduced origin.
* Score direct work for newly introduced origins and rays only on the external band perimeter.
* Run Dijkstra only and return a V2-specific in-memory result; do not place it.

Exit gate: deterministic routes include flat straight, uniform ramp, strafe, switchback with 8x8 landing, no-path narrow area, durability block, and cheaper-G reuse. Costs contain no overlap double-counting.

### Stage 5 — Width-two G/V handoffs and bounded spans

* Implement two-lane prospective workability with concrete Mega pathing.
* Require a usable crest/contact per lane and a continuous five-tile escape corridor in one clearance-two G component.
* Support lateral as well as forward exits.
* Support per-lane mining/dumping and a common two-lane span up to three rows.
* Revalidate the seam during goal acceptance and materialization replay.

Exit gate: fixtures cover diagonal terrain across the seam, mixed terminal operations, blocked far lane, lateral exit, two- and three-row spans, prop cleanup at the seam, and rejection of a visually wide but pinched exit.

### Stage 6 — V2 materialization, placement, and ownership

* Flatten the accepted origin/profile map into unique planned designations.
* Omit exact-terrain no-op origins without breaking provider validation.
* Materialize dense-debris cleanup and required/minimum tree cleanup.
* Apply **Harvest disrupted trees** to the full width-two footprint and exterior disturbed rays.
* Place per-lane terminal protos, register primitive ownership, and roll back every terrain/cleanup/tree mutation on failure.
* Rebuild the snapshot and prove Mega reachability to the intended cluster after placement.

Exit gate: successful placement produces a Mega-reachable provider; injected conflicts and placement failures leave no terrain designations, cleanup designations, harvest markers, or ownership residue.

### Stage 7 — A*, responsiveness, and diagnostics

* Add the paired-goal width-two heuristic only after Dijkstra results are stable.
* Fall back to Dijkstra for fixed-provider goals until a valid multi-source lower bound exists.
* Preserve asynchronous stepping, cancellation, single-flight behavior, timeout, and configurable frame budget.
* Add V2 rejection categories and compact path/profile diagnostics.
* Compare Dijkstra and A* route, cost, visited states, elapsed time, and peak history size.

Exit gate: A* and Dijkstra return the same accepted plan and cost on deterministic fixtures and representative saves. A failed or cancelled V2 search does not leak state or block a subsequent request.

### Stage 8 — Experimental integration and rollout

* Route T3/AUTO-to-Mega requests to V2; T1/T2 remain V1; OFF remains off.
* Update the experimental tooltip and logs so V2 is no longer described as unsupported.
* Compare with a width-two legacy candidate only where the legacy generator genuinely meets the same Mega requirement.
* Preserve direct V2 failure reporting for external work endpoints.
* Run the full regression matrix and retain V1 as an independent path.

Exit gate: V2 is no worse than the valid width-two control on representative saves, all failures are diagnosable, V1 behavior is unchanged, and disabling the feature preserves the existing fallback behavior.

## Future refinements after V2 rollout

### Generated mining-body vehicle-clearance pass

One-origin waists inside generated mining bodies appear to be extremely rare and do not block V2. Eventually add a clearance-aware post-pass that:

* detects one-origin waists, internal islands, narrow turns, and width-one terminal frontages;
* widens or removes generated mining origins according to a documented minimal-change rule while preserving ore/body intent and corner consistency;
* re-runs projected disturbance and hazard checks after widening; and
* leaves player-authored external designations unchanged.

### Core Mining Designations hazard and tree integration

Extend the existing per-world pathfinder options to the core Mining Designations body generator:

* **Avoid ocean** rejects mining-body candidates whose projected **cutting** disturbance reaches ocean. Dumping/fill into ocean remains allowed.
* **Avoid buildings** rejects candidates whose projected cut/fill disturbance reaches the safety footprint of an existing, planned, or ghost building, including the mine control tower. Direct building footprint and vehicle-clearance checks remain separate from terrain-disturbance rays.
* **Harvest disrupted trees** marks trees across the finalized mining body and its projected disturbance zones, using the same tower ownership and selective Clear behavior as accessway-created harvest markers. When disabled, only trees required to execute the accepted work are marked.

Use the same material-aware ray tracer as accessway generation. When an avoidance option is disabled, allow the plan but retain a concise hazard diagnostic. Apply these checks after any future mining-body clearance widening as well.

## Required regression matrix

At minimum, record route, plan, cost breakdown, visited states, runtime, placement result, and post-placement Mega reachability for:

* flat two-lane cut and fill;
* straight up-ramp and down-ramp;
* 90-degree turn with the minimum flat landing;
* switchback;
* lateral strafe on a uniform slope;
* mixed natural ground and generated V;
* early lateral V/G handoff on diagonal terrain;
* two- and three-row handoff spans;
* mixed lane mining/dumping terminal;
* one blocked seam lane;
* existing width-two provider reuse and immediate pinch behind provider;
* sparse forest, dense debris, mixed cleanup, and non-removable prop;
* harvest-disrupted-trees on and off;
* shoreline cutting, ocean dumping, and Avoid ocean on/off;
* building-adjacent route and Avoid buildings on/off;
* prior cut/fill ray elevation envelope interaction;
* brush/slice frontier context without generated-origin revisits;
* exact-terrain no-op lanes;
* tower area too small for a turn or full footprint;
* one-wide external terrain endpoint with compatible companion, blocked companion, existing fixed companion, and multiple feasible orientations;
* generated mining body with an internal waist, recorded as a known limitation until the future clearance pass;
* timeout, cancellation, replacement request, and rollback;
* save/load followed by tower trashcan cleanup.

## Explicit non-goals for the first V2 release

* Corner-designation (`V'`) and saddle (`V''`) search.
* Mixed-axis lane profiles.
* Arbitrary per-lane handoff span lengths.
* Clearance three or a generic N-wide band search.
* Centerline-thicken as production routing.
* Full material simulation or volumetric terrain.
* Rewriting narrow player-authored terrain designations.
* A generic public A-to-B API beyond the existing rooted request.

## Suggested code boundaries

Keep V2-specific state out of the already dense V1 node type:

* `Access/V2/AccessV2BandProfile.cs` — canonical lane profiles and geometry.
* `Access/V2/AccessV2SearchNode.cs` — state identity.
* `Access/V2/AccessV2History.cs` — origin/profile and charged-work deltas.
* `Access/V2/AccessV2GroundGraph.cs` — clearance-two G and cleanup overlay.
* `Access/V2/AccessV2Handoffs.cs` — width-two seams and spans.
* `Access/V2/AccessV2PathSearch.cs` — asynchronous Dijkstra/A* adapter.
* `Access/V2/AccessV2Materializer.cs` — unique origin plan and replay.

Share snapshot data, V1 concrete profile math, terrain sampling, material/ray scoring primitives, cleanup policy, placement transaction helpers, and diagnostics contracts. Do not copy the large V1 search class and modify it independently.

## Recommended first action

Do Stage 0 and Stage 1 only. The band-slice representation and mechanically valid profile pairs must be proven before changing snapshot dispatch or allowing a T3 request past the current width guard. Once those fixtures are stable, the clearance-two G graph can be developed without guessing what generated state it must connect to.
