# V2 Accessway Pathfinding: Jagged Fringes

**Status: Proposed**

## Motivation
Currently, the V2 accessway pathfinder requires a flat, 2-designation-wide "exposed" edge on the perimeter of a cluster to begin or end a search. If the cluster has a staggered or "jagged" edge, no valid exposed pair exists, and the pathfinder cannot form a route. 

To resolve this limitation without introducing complex edge-case geometry logic, we will modify the pathfinder to start from the *center* of the source cluster and search outwards. When the search reaches the jagged perimeter, it will naturally evaluate "stepping out" by placing the missing companion designation required to bridge the gap.

## Algorithm Changes

### 1. Starting from the Center (Not the Edge)
Instead of scanning the perimeter for an exposed flat edge, the search will be seeded from the **middle** of the source cluster. 
- There is an existing definition of "middle" from the V1 implementation that will be reused as the reference point.
- The pathfinder will initialize its search queue with the 2x1 designation pairs corresponding to this center point.

### 2. Removal of `IsExposed`
Since the search now begins inside the cluster and flows outward, we will completely remove the `IsExposed` check from `AccessV2FrontageDiscovery`. It is no longer needed, and removing it simplifies the discovery logic while natively supporting staggered edges.

### 3. Traversal Costing Inside the Cluster
While expanding nodes that are fully contained within the existing source cluster:
- **Generated Work Cost** will naturally be `0`, as the designations already exist (`fixedProfiles`).
- **Traversal Cost** will remain `4` (the actual physical distance of the step).
By retaining the traversal cost, the pathfinder will naturally seek the shortest path from the center of the source cluster to the edge that leads to the goal, rather than treating the entire cluster interior as a zero-cost teleportation zone.

### 4. Heuristic Guidance
The existing potential field (heuristic) will actively guide the internal search. Starting from the center, the heuristic will pull the frontier directly toward the target goal. The search will rapidly walk through the internal designations until it hits the perimeter.

### 5. Stepping Out and Patching
When the search reaches the jagged edge, it will evaluate transitions that step outside the cluster. 
- If a strafe or straight transition lands on one existing designation and one empty tile, the transition logic will naturally cost the generation of the missing designation.
- This creates an emergent "patching" behavior: the pathfinder naturally places exactly the designations needed to square off the jagged edge and form a valid accessway route.
- Any added companion designation must naturally fulfill the fight invariant: all its corners must align with any corners already placed at those origins. If no legal designation under the V2 accessway framework satisfies this (e.g., if it would require a corner or saddle designation, which V2 does not support), then the transition step must be rejected.
- Care must be taken so that any companion designation(s) are not blocked by disturbance rules. If the blocking envelope is implemented correctly and the designations fulfill the fight invariant, this should naturally be avoided.

### 6. Transitioning into Clusters (Goals)
The process of entering the target cluster is symmetrical to leaving the source cluster. 
- A move into an existing fixed designation on the edge of the target cluster is valid and approved if it forms a legal 2x1 move after generating the necessary companion designation(s) to bridge the gap.
- Since the search goal is defined by reaching any valid designation inside the target cluster (or its "middle"), the pathfinder will evaluate these "step in" transitions.
- Once the path bridges into the target cluster by placing a companion, the remaining steps toward the goal center will cost 0 for generated work and simply use the distance cost of 4.

### 7. Future Refinement: Diagonal Internal Moves
As a further refinement, diagonal transitions within the cluster could be supported to provide more accurate pathing distances. 
- Similar to diagonal movement rules over G (Ground), a diagonal move would be permitted if both adjacent cardinal nodes exist.
- The traversal cost for such diagonal moves would be based on the Euclidean distance (e.g., squared distance sum), providing a more realistic traversal weight when navigating large, irregularly shaped interiors.

## Summary of Benefits
- Eliminates special-case logic for starting edge validation.
- Naturally supports highly irregular, jagged, and staggered cluster shapes.
- Leverages the existing A* heuristic and cost functions to natively discover the optimal exit point from any starting shape.
- Readily applicable to V1 pathfinding and future V3+ logic.
