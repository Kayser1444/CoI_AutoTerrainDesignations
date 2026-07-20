# Ground-Recovery Distance Field for A* Heuristic

## Problem

We need an admissible A* heuristic for a navigation problem over terrain.

The terrain is defined by a ground height field:

[
g(x,y)
]

The navigation state is:

[
(x,y,h,\text{direction})
]

where:

* `(x,y)` is a position on a reduced navigation grid.
* `h` is height above the terrain.
* `direction` is the current movement direction.

The reduced navigation grid has:

* horizontal spacing: 4 terrain units,
* vertical spacing: 1 height unit.

A move:

* changes `(x,y)` by one grid cell,
* may change height by 0 or 1 toward the ground,

Additional constraint:

* Turning is only allowed if the previous movement was flat (height change 0).

The goal of this heuristic is:

> Given a state `(x,y,h,direction)`, estimate the minimum number of moves required to reach the ground.

---

# Key Observation

Instead of computing the path to ground during A*, preprocess the answer.

Define:

[
L(x,y,h,d)
]

as:

> The minimum number of moves required to reach ground from state `(x,y,h,d)`.

This is exactly the heuristic value.

If we can compute this field once per terrain snapshot, A* lookup becomes:

```cpp
heuristic = L[x][y][h][direction]
```

The value is admissible because it is the exact shortest distance for this subproblem.

---

# Why a 3D height search is unnecessary

The height range is small after reduction.

For a 256×256 terrain:

* reduced horizontal grid:
  [
  64\times64=4096
  ]

* height levels:
  [
  -20 \ldots +20
  ]

giving:

[
4096\times41\approx168000
]

states.

Adding direction and a small turn-state flag still keeps the graph comfortably small.

A full precomputation is feasible.

---

# Reverse Multi-Source BFS

The easiest way to compute (L) is to reverse the navigation graph.

Instead of asking:

> How do I get from air to ground?

ask:

> How do I get from ground to every possible air state?

All ground states are starting points.

Initialize:

```
distance[ground states] = 0
distance[other states] = infinity
```

Then perform BFS on reversed edges.

Every time we discover a predecessor:

```
distance[predecessor] =
    distance[current] + 1
```

The final distance table is the desired heuristic.

---

# State Representation

The minimal state must include every piece of information needed to determine legal moves.

Likely:

```
(x, y, h, direction, canTurn)
```

where:

```
canTurn = true
```

means the previous move was flat.

If the existing A* node representation already contains this information implicitly, the heuristic table can use the same node ID.

The important rule:

> The preprocessing graph must exactly match the A* movement graph.

This guarantees admissibility and consistency.

---

# Generating Reverse Edges

The forward transition rules are already known.

For every state:

```
(x,y,h,direction,canTurn)
```

generate legal successors:

* move forward,
* optionally change height by 1 toward ground,
* optionally turn if allowed.

For preprocessing, invert these transitions.

Because the grid is small, reverse neighbors do not need to be stored. They can be generated procedurally.

---

# Why this works well

The terrain envelope guarantees:

> Every move toward ground from a valid state remains inside the valid domain.

Therefore the reverse search never needs to explore impossible states.

The precomputed table automatically respects:

* terrain shape,
* height limits,
* overhangs,
* local constraints,
* turning rules.

No special geometric reasoning is required.

---

# Memory Estimate

Without turn state:

[
64\times64\times41\times4
]

states:

[
\approx 670000
]

With one byte per result:

[
<1\text{ MB}
]

Even with additional flags or 16-bit distances, memory usage is negligible.

---

# Alternative: Store H(L) Later

A possible optimization is to remove the height dimension and store:

[
H_{x,y}(L)
]

where:

> maximum height from which ground can be reached after at most (L) moves.

Then queries solve:

[
H_{x,y}(L)+L\ge h
]

and find the smallest valid (L).

This is more compressed and may be useful later.

However, it is harder to incorporate:

* direction,
* turn restrictions,
* movement modes.

The direct (L(x,y,h,direction)) field is simpler and is already small enough.

---

# Recommended First Implementation

1. Keep the exact A* state representation.
2. Implement reverse BFS using the same movement rules.
3. Seed the queue with all grounded states.
4. Store the resulting distance as the heuristic table.
5. Compare A* performance with the old heuristic.

If memory or preprocessing time later becomes a problem, the (H(L)) representation is the natural next optimization.
