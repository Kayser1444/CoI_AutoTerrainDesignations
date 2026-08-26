# Roadmap

Current priorities for Kayser's Automatic Terrain Designations. This version is intentionally shorter and more grounded in what has already landed versus what still needs attention.

## Status summary

The access framework, V2-style accessway search, and bounded out-of-area ramp fallback are now part of the implemented baseline. The roadmap below focuses on polish, reliability, and the remaining feature gaps that still affect gameplay quality.

## Near-term priorities

- Improve wide-ramp and mining-face connections where jagged or sloped terrain still produces awkward joins.
- Tighten generated mining-body clearance so narrow one-origin waists are less likely to block Mega/T3 vehicles.
- Continue improving construction assistance around tower overlap, soil export, and ground preparation behavior.
- Make designation creation consider possible farming work more explicitly when the surrounding terrain and tower context suggest it.
- Review vehicle auto-release and auto-assign behavior so tower ownership remains predictable.

## Feature ideas worth keeping on the radar

- Cut/copy/paste or blueprint-style designation workflows.
- Rail incline (12.5%) designation support.
- True saddle designation support (`[0 1; 1 0]` corner heights); the current diagonal-plane shape is documented as Planar.
- Rampless mining mode: when invoked with Ore quality = Max, create a mining deposit that naturally crests at a suitable point, reducing wasted work on a dedicated ramp.
- Demand-weighted accessway travel cost, where permanent driving distance
  matters more for clusters with larger expected mining or filling workloads.
- A useful-material rebate for accessway excavation cost. Its implementation
  must preserve terrain-extrema heuristic admissibility or weaken that
  heuristic component to zero wherever the rebate can apply.
- Underground pipe construction support for both vanilla and modded scenarios, if the complexity becomes practical.
- If the snapshot is too large, shrink it to managable size
- Replace static T1, T2, and T3 accessway clearance levels with dynamic levels based on the available excavator base class prototypes.

## Reliability and quality work

- Build the [Access Search Laboratory](access-search-laboratory.md) after the
  primitive capture pipeline: record owned real search cases, replay the exact
  mod DLL for semantic regression and Release benchmarking, then add
  collaborative route validation and bounded autonomous conformance tuning.
- Keep refining accessway heuristics and terrain-disturbance prediction, especially around avoidance settings and hazard warnings.
- Investigate topsoil optimization so farmability can be satisfied with less over-placing where it is safe to do so.
- Address concurrency and thread-safety issues around idle vehicle release and entity enumeration so the mod remains stable under broader game and mod interactions.

## Notes

This roadmap deliberately omits older planning items that are now implemented or superseded by the current access framework and V2 accessway work.
