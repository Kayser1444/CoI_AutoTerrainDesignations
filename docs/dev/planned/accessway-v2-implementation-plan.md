# Accessway V2 review and staged implementation plan

Status: Stages 0–6 implemented; Stage 7 A* is implemented and awaiting live verification alongside the remaining Stage 6 Mega-pathability cases

Reviewed: 2026-07-13

## Purpose

V2 is the width-two access search used for five-tile-clearance Mega/T3 vehicles. It remains in the vanilla designation set: flat and axis-aligned slope profiles only. Corner and saddle designation search spaces (`V'` and `V''`) are separate future work.

This review reconciles the earlier V2 notes with the production V1 implementation and the decisions made while testing V1. It resolves the former representation ambiguity, records the accepted requirements, and breaks implementation into independently testable stages.

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
* Straight travel, turns, and any accepted direct-strafe transition must preserve shared origin profiles and shared corner heights exactly.
* Mixed-axis origin shapes, corner designations, and saddles are out of scope.

### G traversal and endpoints

* Width-two G occupancy, G adjacency, tower-reachable flood, goals, cleanup overlay, and post-placement verification must all use the concrete Mega/T3 pathability parameters.
* A V2/G seam must expose two consecutive workable lanes connected to the same clearance-two G component.
* G-to-V2 and V2-to-G are symmetric graph operations, even if the mine-tower caller usually starts at fixed V work and ends at tower ground.
* A fixed-provider goal initially requires only two adjacent fixed origins with an exposed two-origin frontage. Internal fixed-network shape and Mega clearance behind that frontage are deferred.
* A width-one work endpoint is a valid V2 **seed** when the search can synthesize a compatible adjacent companion lane. It becomes a reusable fixed provider only when another fixed origin supplies the required exposed two-origin frontage.

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
* Any generated origin with cardinal edge contact to earlier generated history is likewise rejected unless that earlier origin belongs to the transition's explicit local context. V2 local-context exceptions are the retained strafe lane, the other lane introduced by the same delta, an explicit 2x2 turn landing, and the active bounded handoff span. Diagonal corner contact remains legal. Existing/player designations and G nodes are not generated-history contacts.
* Materialization omits exact-terrain no-op profiles and retains required cleanup metadata.
* Terminal lanes may use their required mining or dumping proto independently, provided shared corners and the combined seam remain valid.
* Rollback and persisted ownership cover all generated lane designations, cleanup designations, and newly selected tree markers.

## Accepted state representation

The existing notes alternately describe V2 as:

* a 2x2-origin brush containing four origins;
* a node whose cost footprint contains two origins, one per lane; and
* `(brushVertex, h, axis, profile-string)` with only two profile tokens such as `FX+`.

Two tokens and one reference height do not uniquely define four origin profiles. The ambiguity affects every downstream operation: shared-strip compatibility, lane heights, work cost, ray ownership, turn geometry, duplicate-origin handling, and materialization.

Several listed profiles are also not automatically legal. For example, `FX+` or `X+X-` may fight across the lane seam depending on travel axis and relative lane heights. A single shared `h` cannot express all compatible relative offsets. The legal set must be derived from concrete corner profiles, not accepted by token name alone.

### Width-two band slices

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
* A strafe shifts the slice laterally by one origin while preserving travel axis and profile family. The overlapping lane is retained as immutable frontier context; only the newly exposed lane belongs to the transition delta.
* A turn is a special transition available only when the current and immediately preceding slices are both completely flat. Those two slices form the required 2x2-origin landing. The turn validates the appropriate perpendicular edge of that landing and advances directly to the first new outgoing slice; it does not enqueue an orientation-only frontier made from already introduced landing origins.
* Search history owns a unique origin/profile map. Every transition delta adds previously unused origins; costs and materialization are computed only for that addition. A strafe may retain one already-owned lane as frontier context without re-adding, re-costing, or rematerializing it.

Worked clockwise example, using `+X = east` and `+Y = south`: let the current eastbound frontier anchor be `A`, with the preceding anchor at `A + (-4, 0)`. Their four flat origins form the 2x2 landing. The landing's implicit south-facing frontier is anchored at `A + (-4, 4)`, but both of its origins already belong to the landing and are therefore not enqueued. The first new `Y`, `entryDirection = (0, +4)` frontier is anchored at `A + (-4, 8)`, with origins `A + (-4, 8)` and `A + (0, 8)`. Therefore `A + (4, 4)` is not the successor anchor under this convention; it would attach an outgoing slice to only the landing's east side rather than continue from its full south edge.

The turn must also retain the incoming frontier's forward disturbance. Cast three material-aware durability rays in the **old** travel direction from the three vertices across the predecessor frontier's two-origin forward face: both endpoints and the shared-lane midpoint. In the eastbound example these start at `A + (4, 0)`, `A + (4, 4)`, and `A + (4, 8)` and all travel in `+X`. This is the V1 turn-forward/outer-corner ray extended with the other full-width endpoint (the true outer corner for the second lane) and the predecessor-frontier midpoint. All three participate in feasibility, cost, elevation-aware generated history, finalized disturbance, and disrupted-tree harvesting; vehicle-clearance thickening does not replace any of the three source rays.

A geometric center anchor was considered and rejected for the band-slice model. The center of a two-origin slice has different lattice offsets for `X` and `Y` orientation, while the convenient grid-aligned center exists only for the temporary 2x2 turn landing. It would make some coordinate traces more symmetric but would not prevent loops or replace concrete-origin history checks. The accepted lane-0 anchor keeps origin derivation and materialization direct.

This model satisfies the physical width and turn requirement without a four-origin sliding state. It also avoids carrying and reconsidering already introduced origins as part of every step.

The earlier true four-origin sliding-brush state is not part of the implementation plan. It would require four concrete profiles, explicit frontier-vs-delta ownership, and canonicalization of overlapping windows while adding no demonstrated geometry unavailable to band slices. Revisit it only if future fixtures prove the accepted band-slice model cannot represent a required route.

## Folded-in design decisions

### 1. Profile-pair legality and initial set

The prose whitelist (`FF`, `FX+`, uniform ramps, and opposed pairs) is not sufficient as a correctness definition. The physical lane order, travel axis, independent center heights, shared seam, and transverse drivability decide legality.

Enumerate pairs of concrete V1 profiles within the construction-reachable relative-height range, retain only pairs whose shared edge matches and whose five-tile center corridor is drivable, then classify them for diagnostics. The initial production search enables flat pairs and uniform same-sign ramps. Asymmetric and opposed pairs remain disabled unless dedicated fixtures later prove that they add valid routes; they are an optional search-space expansion, not an unresolved V2 requirement.

### 2. Orientation at a flat state

An `FF` slice is geometrically axis-neutral, but route state retains orientation to define lane order and the next footprint.

Keep orientation in the state key and permit an axis change only through the explicit flat-turn transition above. It validates the already charged 2x2 landing, then costs only the first new outgoing slice and newly exposed exterior rays. Do not add free in-place orientation edges or enqueue a frontier composed of already introduced landing origins.

### 3. Start frontage and narrow mining bodies

Current clusters and fixed endpoints are origin-based. Requiring players to paint a two-origin target merely to request a Mega accessway would be poor UX. A single existing designation cannot by itself expose a reusable width-two frontage, but it can anchor one lane of a newly generated V2 start. Generated mining bodies may also contain waists or one-origin corridors that a Mega excavator cannot traverse.

* Treat a single external/player designation as an immutable fixed seed profile. Enumerate all transverse companion positions and both travel signs. An already existing compatible neighbor may supply the second lane; otherwise solve and generate a companion `AccessHeightProfile` whose shared edge exactly matches the seed and whose shape belongs to the enabled V2 profile set.
* The fixed seed and its companion form the initial two-origin frontier. Validate the full Mega footprint, shared corners, bounds, props, buildings, designation conflicts, and exterior rays before admitting it. Existing-work disturbance from the seed remains authoritative, with only the connected shared seam exempted from self-conflict.
* Never alter or take ownership of the player's seed. A synthetic companion is costed like ordinary generated V work and is materialized and owned by ATD only if its route wins. Failed candidates leave no designation behind.
* If every companion candidate is blocked or profile-incompatible, fail with a specific start reason such as `NoWidth2StartCompanion`, including orientation/rejection diagnostics. Do not ask the player to widen the target manually and do not fall back to V1 for a T3 request.
* A single-origin seed is start-only. It must not be advertised as a fixed provider that later clusters can reuse; provider reuse still requires a genuine exposed frontage made from two adjacent fixed origins.
* Rare one-origin waists inside ATD-generated mining bodies are accepted as an initial V2 limitation. A later mining-body clearance pass should widen or remove them, but it is not a V2 rollout blocker.

### 4. Width-two handoff rule

Require at least one clearance-eligible, vanilla-workable crest/contact in each lane and prove a continuous five-tile Mega exit corridor beyond the combined seam. Allow mixed lane operations because the lanes materialize as separate origins. Allow any band side as an exit, not only forward. Generalize the bounded extension to a two-lane strip of at most three rows for T3. Both lanes use one common span length; asymmetric per-lane spans are outside the initial V2 search space.

### 5. Search-history representation

V1 history is a linked sequence of single generated origins plus ray envelopes and categorically rejects an origin already present in the path. V2 keeps that invariant. A multi-origin band/brush may retain the current frontier as state context for seam validation, but this is not a new visit: a successor's generated delta must contain only origins absent from the complete candidate history.

V2 history stores or structurally shares:

* `origin -> concrete profile`;
* the set of previously introduced generated origins, used to reject revisits before scoring;
* distinct charged cleanup object/origin keys;
* exterior disturbed tiles and elevation envelopes; and
* current exposed band frontier.

Any origin reintroduced by a transition delta rejects the transition before profile comparison or costing. An origin merely retained as the unchanged lane of a direct strafe is not part of that delta and remains legal immutable frontier context. Whether history is implemented with persistent immutable maps or parent-plus-delta nodes is a performance/engineering choice to benchmark during Stage 1, not a semantic design question.

### 6. Exterior rays at straight and turning bands

Calling the V1 scorer independently for both lanes would cast rays from the internal seam and exaggerate cost/blocking. Turns also have an inside corner, outside corner, and potentially newly exposed flat landing edges.

Build a band perimeter first, classify each perimeter edge by work operation and exposure, and score only exterior rays. Keep direct work per newly introduced origin. A turn transition reuses the already scored flat landing only as validation context, then scores the first new outgoing slice, the exterior corners newly exposed by the changed direction, and the three old-direction forward rays across the incoming frontier described above. Safety-buffer tiles count as disrupted for harvesting, preserving V1 `DisturbedRayTiles` behavior.

### 7. Fixed providers and provider goals

Current fixed-profile goals are individual origins. V2 initially needs only a local width-two goal frontage. Do not make first-pass goal discovery responsible for proving the internal shape or Mega traversability of the fixed designation network behind that frontage.

Precompute every pair of adjacent accepted fixed origins that exposes two collinear outer edges on the same side, and use that exposed pair as a fixed goal frontage. The concrete fixed profiles still supply the seam heights that a generated terminal must match, but their inward shape is not part of initial goal eligibility. Do not form arbitrary pairs during expansion. Each cluster request rebuilds an optimistic provider-distance field over only accepted fixed-provider and generated-accessway profiles; frontages without a finite continuation through that field to tower ground are omitted.

### 8. Goal seeding and A* lower bounds

Current tower goals are sparse radial G goals. For V2 these can remain center positions only if they belong to the same clearance-two G graph and are not isolated docking pockets. The V1 paired height/distance heuristic is not automatically admissible for a two-origin footprint or fixed-provider band.

Under the lane-0 anchor convention, an `X`-axis frontier has eligible vehicle-center samples `anchor + (2, y)` for `y = 2..6`, with canonical band center `anchor + (2, 4)`. (Thus the eastbound range is `(2,2)..(2,6)`, not `(2,2)..(6,2)`; the latter is the rotated `Y`-axis form.) A `Y`-axis frontier correspondingly uses `anchor + (x, 2)` for `x = 2..6`, centered at `anchor + (4, 2)`.

Represent every V2-to-G and G-to-V2 transition as passing through that canonical band center. Let `F` be the generated-origin fixed cost. The proven cheapest ordinary V traversal rate is `1 + F/4` per canonical horizontal tile, so charge a **`2 * (1 + F/4)` travel-cost center spoke** on the real graph edge, in addition to cleanup and other seam costs. This covers the maximum two-tile Manhattan offset and prevents a handoff from undercutting ordinary V. A validated seam enters an explicit G state and carries the same immutable candidate history until an actual ground goal is reached. Continue validating the actual set of eligible handoff samples and the complete Mega escape corridor; the center is a cost/heuristic abstraction, not a substitute for physical seam validation.

The A* lower bound for a V2 state may then start from the canonical center. Every eligible centerline handoff sample is at most two Manhattan steps from that point, and the paid spoke is exactly two minimum-rate V steps. The relaxed V field therefore propagates cardinally at `1 + F/4`; explicit G states retain exact octile ground distance. Add a fixture that keeps the V rate and spoke coupled. If a later transition family can travel more cheaply per tile, lower both through the shared cost model or fall back to Dijkstra.

Run V2 Dijkstra first and add A* only after it reproduces Dijkstra. The implemented request-scoped potential seeds every goal-connected G center with exact remaining G distance. Each concrete fixed-frontage matching center is seeded with its real terminal fee: the final four-tile entry slice plus the accepted provider network's optimistic downstream travel distance to tower ground. Relaxed cardinal propagation at `1 + F/4` then fills the request bounds. V states query that field; G states retain exact G distance until G-to-V exists.

### 9. Cost note is stale

The older V2 text permits a center-point landscaping estimate. Production V1 already has more accurate four-corner and ray costing. Reintroducing center-only cost would repeat the route-quality failures already corrected in V1.

Sum direct work for newly introduced concrete origins and calculate rays from the external band perimeter from the first V2 search. A simpler scorer may be used only in pure geometry fixtures, never in production candidate comparison.

### 10. Frame budget and state growth

The historical notes mention Dijkstra-first validation and asynchronous search. The current configurable default is 30 ms per frame, while an earlier design discussion proposed a hard-wired 20 ms budget. V2 has materially more states and larger histories.

Keep the existing configurable asynchronous budget and 30 ms default, plus single-flight cancellation; do not hard-wire a second V2 budget. Add V2-specific counters for band states, profile-pair candidates, history deltas, and memory/high-water estimates.

### 11. Fatal startup self-tests

The current transition self-test aborts every production snapshot if a fixture becomes stale. Recent no-op materialization changes exposed this failure mode twice. V2 will add many more geometry fixtures.

Keep a very small fatal invariant test. Move the full V2 fixture matrix to an explicit development/test entry point; if any production-gating V2 fixture is retained at startup, its failure disables only V2 with one precise diagnostic. A V2 fixture failure must not disable V1.

### 12. Legacy comparison and unsupported widths

Generated-mining clusters can currently compare V1 with the legacy generator, while external terrain-work endpoints rely on the generic path and cannot safely use the mining-specific legacy fallback.

While V2 is experimental, generated-mining clusters may compare V2 with a genuine width-two legacy candidate if one exists. External endpoints report the V2 reason directly. Never silently run V1 for a T3 request.

### 13. Documentation drift

Some older notes still describe center classification for handoffs, hourglasses as the authoritative generated-V blocker, ocean avoidance for both cut and fill, and AUTO fallback behavior that predates the current vehicle pool. Stage 0 updates those notes to reference this accepted design so implementation is not guided by obsolete V1 behavior.

## Accepted decisions

All reviewed recommendations have been accepted and folded in. No semantic design question remains before production graph work.

| Area | Accepted decision | Status |
|---|---|---|
| State footprint | Canonical two-origin band frontier; no sliding four-origin brush | Accepted |
| Anchor and orientation | Lane-0 origin anchor, unsigned axis, signed entry direction | Accepted |
| Lane heights | Two concrete `AccessHeightProfile`s; no shared `h` or token-only legality | Accepted |
| Initial profile pairs | Flat and uniform same-sign ramps; asymmetric/opposed pairs disabled initially | Accepted |
| Generated history | Every transition delta introduces unused origins; any origin reintroduced by a delta is rejected before costing | Accepted |
| Generated self-contact | Reject nonlocal cardinal edge contact; allow only explicit transition context and diagonal corner contact | Accepted |
| Lateral strafe | Shift by one origin; retain the overlapping lane as immutable, uncharged frontier context and generate only the newly exposed lane | Accepted |
| Turns | Current and predecessor flat slices form the 2x2 landing; jump to the first new outgoing slice | Accepted |
| Turn disturbance | Three old-direction forward rays across the complete incoming frontage plus newly exposed perimeter rays | Accepted |
| G graph | Concrete Mega/T3 mask, cleanup footprint, tower component, and sparse goals | Accepted |
| V2/G seam | Workable contact per lane and one continuous five-tile Mega corridor | Accepted |
| Terminal operation | Per-lane mining/dumping allowed | Accepted |
| Handoff extension | Common two-lane span, maximum three rows for T3 | Accepted |
| V2/G cost point | Canonical band center with a two-tile spoke charged at `2 * (1 + F/4)` to/from validated handoff samples | Accepted |
| One-wide external start | Keep the target immutable and synthesize/cost a compatible companion lane | Accepted |
| Fixed-provider goal | Two adjacent fixed origins with one exposed width-two edge; ignore inner-network shape initially | Accepted |
| Work and rays | Four-corner direct work for new origins; material-aware rays only from the exposed band perimeter | Accepted |
| Ray-buffer harvesting | Buffer tiles remain part of disrupted-tree harvesting | Accepted |
| A* | Dijkstra oracle first; canonical-center heuristic for ground goals after equivalence proof | Accepted |
| Fixed-goal A* | Request-scoped potential seeded at concrete fixed-frontage match centers with charged downstream provider travel | Implemented |
| Responsiveness | Existing configurable asynchronous budget with 30 ms default and single-flight cancellation | Accepted |
| Fixture failures | Tiny fatal core only; full V2 suite is explicit, and any startup V2 gate can disable only V2 | Accepted |
| T3 fallback | Never silently run V1; compare only with a genuine width-two legacy candidate where available | Accepted |
| Mining-body waists | Known rare limitation; clearance pass deferred until after V2 rollout | Deferred refinement |
| Centerline-thicken | Not a production route; optional diagnostic comparator only | Optional |

### Accepted lateral-strafe semantics

A direct one-origin strafe requires a V predecessor slice, carries one old lane into the successor frontier, and introduces one new lane beside both the current and predecessor longitudinal slices. Those two new origins form the complete generated delta, producing a 2x3 swept footprint that preserves a width-two brush through the sideways move. The retained lane is immutable context: it is not added to the transition delta, re-added to history, re-costed, revalidated as new work, or rematerialized. Both new origins must agree with every shared corner and profile constraint. A start frontage or fresh G-to-V entry must advance once before strafing because it does not yet own that predecessor slice.

On a flat landing where the corresponding turn is legal, the finder suppresses the strafe successor. A flat strafe and the canonical turn path can materialize the same terrain profiles with different incremental ray and traversal accounting, so admitting both would make cost and blockage depend on graph representation. Uniform-slope strafes remain available because turns are illegal there.

Consequently, “origin revisit” means that a transition attempts to reintroduce an origin in its generated delta. Merely retaining the unchanged lane in a successor frontier is not a revisit. This preserves direct uniform-slope strafing for cost-efficient mountainside routes without permitting duplicate work or exploration of an identical full state.

### Non-blocking implementation choices

The following choices remain deliberately implementation-level:

* benchmark persistent immutable maps against parent-plus-delta history nodes and choose the lower-overhead representation without changing history semantics;
* retain Dijkstra as the reference and compare it with the multi-source request-potential A*;
* enable asymmetric or opposed profile pairs only if dedicated fixtures demonstrate useful valid routes;
* use a width-two legacy comparator only if the existing generator can actually produce one; its absence does not block V2; and
* complete the explicitly deferred mining-body clearance, core Mining Designations hazard/tree integration, and fixed-provider interior-clearance refinements after V2 rollout.

## Staged implementation plan

Each stage must build and be reviewable independently. Width two remains explicitly unsupported in production until the stage's exit gate says otherwise.

### Stage 0 — Codify the accepted geometry design (implemented)

* Encode the accepted band-slice state specification; do not implement the rejected sliding four-origin brush.
* Codify canonical anchor, lane order, travel axis, entry direction, and turn footprint with coordinate examples for X+/X-/Y+/Y- travel.
* Codify direct strafe as a one-origin lateral shift with one immutable retained lane and a two-origin copied outer lane across the current and predecessor slices, and make the delta-based history invariant explicit.
* Codify profile-pair generation from concrete corner profiles, initially exposing only flat and uniform same-sign pairs.
* Codify common-span width-two handoff semantics, per-lane operation rules, and the canonical-center spoke cost.
* Update the older pathfinding/framework notes to reference these accepted decisions.

Exit gate: four worked examples (flat straight, uniform ramp, direct strafe, and 90-degree flat turn) map unambiguously to origin coordinates and corner heights in both positive and negative directions, including the retained-lane and generated-delta sets for strafe.

### Stage 1 — Pure V2 geometry and fixture surface (implemented)

* Add V2 geometry types separate from `AccessSearchNode` and `AccessHeightProfile` state identity.
* Enumerate mechanically valid lane-profile pairs.
* Implement shared-seam compatibility, straight advance, the Stage-0 accepted lateral behavior, flat-turn union, bounds, and origin/profile addition with the accepted history rule.
* Reject every new delta origin with cardinal edge contact to older generated history outside the explicit local transition context; keep diagonal corner contact legal.
* Add fixtures for direction symmetry, relative lane heights, illegal fights, opposed pairs, turn landing size, all three old-direction turn rays under four rotational symmetries, identical-origin revisit, and conflicting-origin revisit.
* Put the full V2 matrix behind an explicit development/test entry point; any retained startup V2 gate disables V2 only.

Exit gate: pure fixtures prove every emitted origin profile has integer corners, all shared corners agree, every transition introduces a deterministic origin/profile delta, no transition double-owns an origin, nonlocal edge contact is rejected, and straight, strafe, turn-landing, and handoff-span local contacts remain legal.

### Stage 2 — Clearance-two G graph and cleanup overlay (implemented and live-verified)

* Refactor snapshot construction so the resolved vehicle parameters drive G occupancy and adjacency without the current `vehicleClearance > 4` early exit.
* Build the Mega/T3 G graph, tower flood, sparse radial goals, and exclusion diagnostics.
* Lift cleanup G from origin metadata to full Mega footprint validation.
* Deduplicate cleanup cost keys across footprint tiles.
* Keep `AccessPathRequest` width two returning `V2GraphNotEnabled` after snapshot validation.

Implementation note: Mega requests now pass the former width guard, run the pure V2 fixture gate, capture ordinary ground with the concrete width-five pathability mask, build cleanup eligibility per vehicle-center tile rather than inheriting a whole origin's blocker state, deduplicate cleanup objects across footprint centers, build an immutable V2 ground view, and emit `[ATD V2 Ground Graph]` diagnostics. Search still returns `V2GraphNotEnabled`, so this stage cannot place terrain or cleanup designations.

Exit gate: fixtures and a dry-run diagnostic distinguish pathable Mega ground, T1-only ground, cleanup-eligible Mega ground, isolated docking pockets, and non-cleanup blockers.

Live result: the Mega request reached `[ATD V2 Ground Graph]`, emitted plausible width-five ground/goal/cleanup counts, stopped deliberately at `V2GraphNotEnabled`, and produced no accessway mutations.

### Stage 3 — Width-two starts and fixed-provider frontages (implemented and live-verified)

* Convert clusters into candidate width-two start frontages.
* For each single-origin external target, enumerate fixed or synthetic companion-lane starts in every orientation without modifying the target.
* Precompute fixed-provider goal states from adjacent origin pairs with an exposed two-origin outer edge; retain their concrete seam profiles but do not inspect the fixed network behind them.
* Emit explicit reasons for missing/blocked companion profiles, narrow generated clusters, non-exposed fixed pairs, and out-of-area frontages.
* Do not rewrite external/player terrain designations.

Implementation note: V2 now enumerates all enabled orientations for every one-origin seed, accepts compatible existing companion lanes, otherwise records a side-effect-free synthetic companion delta, rejects blocked/out-of-area/non-exposed frontages with categorized diagnostics, and discovers fixed goals only from adjacent allowed origins with an open two-origin outer edge. The request carries these concrete frontage states into the V2 dispatch boundary and emits `[ATD V2 Frontages]`; search and placement remain disabled.

Exit gate: paired flat/ramp starts and exposed two-origin fixed goals are recognized in all orientations without inspecting fixed-network interiors; a one-wide player endpoint produces valid fixed-plus-synthetic start candidates in every feasible orientation, owns only the winning companion, and fails diagnostically only when no companion is feasible, without falling back to V1.

Live result: an exposed one-origin endpoint produced the expected synthetic frontage candidates without mutation; surrounding the same endpoint with blockers rejected every companion and concluded with `NoWidth2StartCompanion`. Stage 3 is accepted.

### Stage 4 — V2 Dijkstra graph, history, and production cost (implemented and live-verified)

* Add a V2 search session/adapter selected by `RequiredWidth == 2`.
* Implement immutable origin/profile history with early origin-revisit rejection and cleanup-key deduplication.
* Expand straight, the accepted lateral behavior, and flat-turn transitions.
* Apply current profile feasibility, bounds, designation fights, elevation-aware prior rays, ocean/building rules, and candidate ray limits to every newly introduced origin.
* Score direct work for newly introduced origins and rays only on the external band perimeter.
* Run Dijkstra only and return a V2-specific in-memory result; do not place it.

Implementation note: width-two requests with fixed-provider frontages use the same incremental V2 session. The graph expands flat/ramp straight successors, two-origin swept strafes on slopes or where no equivalent turn exists, and explicit flat 2x2 turns; generated history owns only transition deltas and carries source profiles, charged cleanup keys, and elevation-aware exterior-ray constraints. Production evaluation applies snapshot profile feasibility, projected work, prior ray envelopes, four-corner direct work, generated-origin overhead, deduplicated dense-debris cleanup, exterior band rays, and all three old-direction turn rays. Dijkstra remains available as the optimality reference; production A* uses the request-scoped potential.

Exit gate: deterministic routes include flat straight, uniform ramp, the accepted lateral behavior, switchback with 8x8 landing, no-path narrow area, durability block, and cheaper-G reuse. Costs contain no overlap double-counting.

Fixture result: flat straight, uniform ramp, two-origin swept strafe, consecutive strafe, flat-strafe dominance, 2x2 flat turn, injected durability/no-path, and cleanup-key deduplication pass in the explicit V2 runner. The already verified Mega ground graph remains the cheaper traversal substrate; connecting it to V2 is intentionally the Stage 5 seam rather than an implicit Stage 4 edge.

Live result: AUTO resolved a fleet Mega to vehicle width five and `requiredWidth=2`; frontage discovery found six starts and ten fixed frontages. Dijkstra found a 16-state route with 23 generated origins, seven straight and eight strafe transitions, bounded history/ray high-water counts, and zero terrain-designation mutations. Delta ownership reconciled exactly as 14 straight origins plus eight strafe origins plus one synthetic start companion.

### Stage 5 — Width-two G/V handoffs and bounded spans (implemented and live-verified)

* Implement two-lane prospective workability with concrete Mega pathing.
* Require a usable crest/contact per lane and a continuous five-tile escape corridor in one clearance-two G component.
* Support lateral as well as forward exits.
* Support per-lane mining/dumping and a common two-lane span up to three rows.
* Charge the shared-cost-model center spoke, `2 * (1 + F/4)`, on every G-to-V2 and V2-to-G edge while retaining the concrete handoff/escape geometry.
* Revalidate the seam during goal acceptance and materialization replay.

Implementation note: V2 search admits tower-ground routes through a separately costed V-to-G edge followed by explicit G states. The seam evaluator checks both constituent lane designations with vanilla prospective workability, supports mixed per-lane mining/dumping operations, evaluates common forward spans of one through three rows, and derives lateral frontages from the two latest aligned rows. Both lane contacts and their local escape centers must belong to one clearance-two ground component, but local seam acceptance does not require that component already to contain a tower goal. Seam cleanup keys are deduplicated against generated history; the accepted edge charges the shared-cost-model canonical-center spoke. G then admits cardinal moves at cost one and diagonal moves at `sqrt(2)`. Every diagonal requires both orthogonal side corridors to pass ground topology, projected-history, and cleanup validation, preventing corner cutting. After the first G edge, moves with a negative dot product against the incoming direction—exact reversal and both 45-degree backward diagonals—are strictly dominated and omitted. The selected seam and traversed G suffix are retained on the typed V2 result; Stage 6 replay independently revalidates every edge, diagonal swept corridor, projected-history center, cleanup corridor, and final tower goal before materialization.

Exit gate: fixtures cover diagonal terrain across the seam, mixed terminal operations, blocked far lane, lateral exit, two- and three-row spans, prop cleanup at the seam, center-spoke cost in both directions, and rejection of a visually wide but pinched exit.

Fixture result: mixed-operation lane contacts, seam cleanup deduplication/cost, fixed center-spoke cost, lateral exits, two- and three-row common spans, split-component rejection, and ground-terminal Dijkstra retention pass in the explicit V2 runner.

Historical live result before the weighted-spoke change: an explicit T3 request with one inaccessible external work origin and no fixed frontage reached tower ground in 22 visited states. Dijkstra selected a three-state width-two route with one straight and one strafe transition, then a forward one-row Leveling/Leveling seam. Travel reconciled as four plus four for the generated transitions and the then-current two-cost center spoke; the four generated origins reconciled as one synthetic companion, two straight origins, and one strafe origin. Both contacts joined the tower-reachable Mega component, and the mutation audit remained exactly unchanged.

### Stage 6 — V2 materialization, placement, and ownership (implemented; live verification pending)

* Flatten the accepted origin/profile map into unique planned designations.
* Omit exact-terrain no-op origins without breaking provider validation.
* Materialize dense-debris cleanup and required/minimum tree cleanup.
* Apply **Harvest disrupted trees** to the full width-two footprint and exterior disturbed rays.
* Place per-lane terminal protos, register primitive ownership, and roll back every terrain/cleanup/tree mutation on failure.
* Rebuild the snapshot and prove Mega reachability to the intended cluster after placement.

Implementation note: successful V2 search results now retain their band states, unique generated origin/profile map, and selected two-lane seam as typed route data. Before placement, a separate replay reconstructs every synthetic-start, straight, strafe, and turn delta against the unchanged snapshot, re-applies production work/ray/cleanup evaluation, proves the flattened profile map identical, and revalidates the selected Mega seam. Materialization emits each unique terrain-work origin once, omits exact-terrain profiles, retains explicit cleanup, and maps the selected operation independently across both terminal lanes and common spans. The existing placement transaction owns and rolls back terrain, debris cleanup, and tree selections; V2-specific post-placement validation confirms every emitted profile/prototype and the retained tower-ground seam before the provider is accepted. Exact-terrain route origins remain provider geometry without acquiring terrain-designation ownership.

Exit gate: successful placement produces a Mega-reachable provider; injected conflicts and placement failures leave no terrain designations, cleanup designations, harvest markers, or ownership residue.

Fixture result: V2 replay and materialization reproduce the searched route, reject seam drift, omit an exact-terrain synthetic companion, retain its required cleanup, and preserve per-origin terminal-operation metadata. The existing V1 fixture suite continues to cover transactional terrain/cleanup materialization invariants. Live placement and rollback are the remaining Stage 6 gate.

### Stage 7 — A*, responsiveness, and diagnostics (implemented; live verification pending)

* Add the paired-goal width-two heuristic only after Dijkstra results are stable.
* Keep the canonical-center spoke equal to two minimum-rate V tiles, covering every eligible centerline handoff sample without making the seam cheaper than ordinary V.
* Seed fixed-provider matching centers in the same admissible request-potential used by combined-goal A*.
* Preserve asynchronous stepping, cancellation, single-flight behavior, timeout, and configurable frame budget.
* Add V2 rejection categories and compact path/profile diagnostics.
* Compare Dijkstra and A* route, cost, visited states, elapsed time, and peak history size.

Implementation note: V2 requests queue states by `g + h`. V states use one request-scoped field seeded by exact goal-connected G suffix costs and concrete fixed-frontage match centers, with cardinal propagation at the minimum V rate `1 + F/4` over the relaxed request bounds. Goal-connected G states use exact remaining octile ground distance; disconnected G states use the component-aware escape field and may re-enter generated V through a reverse-qualified two-lane seam at any suitable center. Every V transition pays its canonical-center Manhattan displacement and generated-origin overhead; both seam directions separately charge `2 * (1 + F/4)`. Landscaping above that unavoidable overhead, cleanup, history, disturbance, and feasibility are nonnegative constraints omitted from the field, so it cannot overestimate. The compact V2 summary and generic `[ATD V2 Search]` tags report the actual algorithm and `v2g`/`g2v` counts.

Disconnected explicit G components use a component-aware relaxed escape field rather than heuristic zero: minimize octile G travel within the component plus the V potential at the best reachable center. This preserves the decision to admit local handoffs without finite tower-ground distance, prevents repeated history-qualified ground floods from dominating the queue, and provides the correct lower-bound shape for the forthcoming G-to-V transition.

With G-to-V implemented, that field additionally includes the unavoidable fixed overhead of its two newly generated band origins; direct work, cleanup, feasibility, and the center spoke remain omitted. Label dominance follows V1: one cheapest route is retained per concrete G center or V band state, together with its winning history. This bounds repeated G exploration and identical V regeneration without forbidding a shortcut that crosses a static G component more cheaply than its available G route.

Common forward seams first test the finite set of vehicle-center lanes across the width-two band against the situation-qualified ground graph. Candidate-history profile footprints and ray tiles are overlaid on the outward half of the vehicle mask. A locally traversable lane with valid forward operations on both terminal origins is accepted immediately and enters a real G state; neither ordinary-ground classification nor finite tower-goal distance is a quick-handoff criterion. History intersections and non-forward geometries fall through to the complete seam solver. This is a local pathability-mask optimization, not a height-tolerance approximation or a replacement for G traversal.

A* queue ordering is lexicographic: lowest `g+h` first, then lowest `h`, then FIFO. The lower-`h` tie-break preserves optimality while making exact-distance G plateaus advance toward the goal instead of filling the full set of equally short alternatives. Dijkstra remains FIFO because every `h` is zero.

Fixture result: the explicit centerline samples in both orientations remain within the paid two-tile center spoke. A focused cost-model fixture proves that `F=5` yields a minimum V rate of `2.25`, a spoke of `4.50`, and weighted cardinal potential propagation. A* returns the same accepted ground-terminal state sequence and exact total/traversal cost as Dijkstra. The full V1 and V2 fixture suites pass. Live comparison of visited states, elapsed time, and cancellation/replacement behavior remains the Stage 7 gate.

Live result before the quick-mask optimization: A* produced correct ramps for both the trivial down-ramp and full mining-body cases. The trivial Dijkstra explored 5,490 states in 180.02 seconds and timed out. The mining-body Dijkstra explored 6,076 states in 4.15 seconds, selected ten straight band states, and reconciled travel as nine four-tile V moves plus the two-cost spoke plus nine G tiles (`47` total); placement and Mega-seam validation passed. The roughly 48-fold throughput difference confirms that deep cheap histories, rather than node count alone, dominate the pathological flat case.

Exit gate: A* and Dijkstra return the same accepted plan and cost on deterministic fixtures and representative saves. A failed or cancelled V2 search does not leak state or block a subsequent request.

### Stage 8 — Experimental integration and rollout

* Route T3/AUTO-to-Mega requests to V2; T1/T2 remain V1; OFF remains off.
* Update the experimental tooltip and logs so V2 is no longer described as unsupported.
* Compare with a width-two legacy candidate only where the legacy generator genuinely meets the same Mega requirement.
* Preserve direct V2 failure reporting for external work endpoints.
* Run the full regression matrix and retain V1 as an independent path.

Exit gate: V2 is no worse than the valid width-two control on representative saves, all failures are diagnosable, V1 behavior is unchanged, and disabling the feature preserves the existing fallback behavior.

## Future refinements after V2 rollout

### V1 situation-pathability quick handoff

Backport the V2 quick-handoff optimization to V1 after the V2 rollout is stable. The pre-approved V1 masks are the two center vehicle lanes running through the terminal handoff designation and continuing for one cardinal vehicle-center step onto the outside ground. Accept the quick path when either lane is situation-pathable for the resolved vehicle after applying vanilla pathability, snapshot designation blockers, and candidate-history profiles and rays. Retain the full V1 crest, span, cleanup, and projected-seam evaluator as the fallback whenever neither simple mask qualifies.

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

### Fixed-provider interior clearance

The initial fixed-goal rule deliberately stops at an exposed pair of adjacent origins. Later, if tests show false provider connections matter in practice, validate that the fixed designation network behind the frontage retains Mega-width internal clearance and does not immediately pinch to one origin. Keep that refinement separate from local frontage discovery so the first V2 implementation remains tractable.

### Vehicle-speed-normalized traversal cost

Read the resolved vehicle prototype's travel speed and normalize traversal by slowness `s = 1 / v` relative to a documented baseline. Apply the same factor consistently to both graph domains: a full four-tile V transition costs `4 * s`, while each G tile costs `1 * s`. This should allow AUTO/T3 to reflect the slower Mega fleet without changing geometric feasibility. Keep the first V2 release on the current distance-only scale until prototype speed access, normalization, migration behavior, and cross-tier route comparisons have fixtures.

## Required regression matrix

At minimum, record route, plan, cost breakdown, visited states, runtime, placement result, and post-placement Mega reachability for:

* flat two-lane cut and fill;
* straight up-ramp and down-ramp;
* 90-degree turn with the minimum flat landing;
* switchback;
* direct lateral strafe on a uniform slope, proving the retained lane is neither re-costed nor rematerialized;
* mixed natural ground and generated V;
* early lateral V/G handoff on diagonal terrain;
* two- and three-row handoff spans;
* mixed lane mining/dumping terminal;
* one blocked seam lane;
* exposed width-two fixed-provider goal, non-exposed adjacent pair, and a provider that pinches internally as a documented initial limitation;
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

## Next implementation action

Run the Stage 7 A*/Dijkstra live comparison in `docs/test/accessway-v2-stage7-live-test.md`, beginning with the trivial down-ramp that previously timed out and then the non-trivial mining-body route. Retain the Stage 6 placement, Mega-pathability, clear, and rollback checks while evaluating the selected plan.
