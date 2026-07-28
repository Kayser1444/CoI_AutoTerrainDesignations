# Roadmap

Current priorities for Kayser's Automatic Terrain Designations. This version is intentionally shorter and more grounded in what has already landed versus what still needs attention.

## Status summary

The access framework and the V2-style accessway search are now part of the implemented baseline. The roadmap below focuses on polish, reliability, and the remaining feature gaps that still affect gameplay quality.

## Near-term priorities

- Improve wide-ramp and mining-face connections where jagged or sloped terrain still produces awkward joins.
- Tighten generated mining-body clearance so narrow one-origin waists are less likely to block Mega/T3 vehicles.
- Continue improving construction assistance around tower overlap, soil export, and ground preparation behavior.
- Make designation creation consider possible farming work more explicitly when the surrounding terrain and tower context suggest it.
- Review vehicle auto-release and auto-assign behavior so tower ownership remains predictable.

## Feature ideas worth keeping on the radar

- Cut/copy/paste or blueprint-style designation workflows.
- Rail incline (12.5%) designation support.
- Saddle designation support.
- Underground pipe construction support for both vanilla and modded scenarios, if the complexity becomes practical.

## Reliability and quality work

- Keep refining accessway heuristics and terrain-disturbance prediction, especially around avoidance settings and hazard warnings.
- Investigate topsoil optimization so farmability can be satisfied with less over-placing where it is safe to do so.
- Address concurrency and thread-safety issues around idle vehicle release and entity enumeration so the mod remains stable under broader game and mod interactions.

## Notes

This roadmap deliberately omits older planning items that are now implemented or superseded by the current access framework and V2 accessway work.
