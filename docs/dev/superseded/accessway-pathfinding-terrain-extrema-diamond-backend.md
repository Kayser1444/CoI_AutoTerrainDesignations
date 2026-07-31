# Recursive-Diamond Terrain-Extrema Backend

Status: superseded on 2026-07-31. The active terrain-extrema design uses exact
translated swept-mask scans first, adding only measured exact-query caching or
overlap reuse. This backend remains archived as a rejected comparison approach,
not an implementation candidate.

The proposal cached exact minimum/maximum terrain extrema for Manhattan
diamonds `(centre, radius)`, recursively combining the four cardinal
radius-`r - 1` child diamonds. Its motivation was speculative reuse between
nearby A* heuristic queries; a cold query remained `O(r^3)` in cached states,
whereas a direct exact mask scan is `O(r^2)` and matches the semantic work
domain more closely.

Do not revive this approach without fresh measurements showing direct
exact-mask scans are material and that recursive prefetch has high useful
reuse. Any revisit must preserve exact extrema, immutable-snapshot ownership,
explicit coverage failure-to-zero, and A*/Dijkstra validation.

