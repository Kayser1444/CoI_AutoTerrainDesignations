# Mining worker and laboratory qualification

Status: local fixtures pass; the game checks below remain a qualification checklist.

Initial playtest: the maintainer reported fast placement. The 2026-08-31
22:25 session recorded `ore-spike-01` successfully in approximately 84 seconds,
but showed no recording toast. A deterministic presentation fixture reproduced
that symptom with 84 seconds active wall time and only 2 ms of game-thread
polling. Visibility now uses wall time; the fixture and complete manager suite
pass. The rebuilt toast still needs visual confirmation after restarting.
Design: [mining worker and laboratory](../dev/in-progress/mining-scan-worker-laboratory.md).

Use a disposable copy of a representative save and the current Release build.
Keep the previous build available for geometry comparison. No filter should
change ore-spike behavior in this milestone.

1. Compare a small mine, a large mine, disjoint deposits and a known spike at
   each ore-quality level. Exercise depth/elevation limits, clearance and
   bottom flattening. Compare designation origins and every corner height,
   not just the visible bottom.
2. Exercise ocean and building avoidance separately and together. Include
   exterior rays crossing different materials, a map-edge mine and hazards
   outside the designation footprint. Confirm missing capture fails rather
   than approving a partial plan.
3. Request towers A, B and C in that order. B/C must not cancel A. Replace B
   while queued: its latest settings should be captured when B activates,
   without moving it behind C. Replace A during capture and worker execution.
4. Use Stop during capture and computation, change settings/ore/area, destroy
   the tower, save, and load another world. No cancelled or old-world result
   should submit new designations. Repeat paused and at normal game speed.
5. Submit a large mine, then request that tower again while the command is
   pending. Observe completion and ownership of the already submitted batch;
   there is no rollback. Confirm Clear removes the generated cells. Exercise
   blocked placement and verify partial results are reported and not recorded
   as accepted baselines.
6. Enable debug diagnostics and record terrain-read/max-column time, retained
   memory estimate, worker time for each stage, and native batch completion
   wall time. Profile building collection, collection sealing, ownership
   registration and post-mine tree work separately. These atomic operations
   and the native processor can still dominate a frame.
7. Arm `atd_access_replay_arm ore-spike-01 ore-spike mining`, then force a fresh
   mine scan with explicit ore selection. Replay the resulting case through
   its archived Release DLL; require exact geometry. Record a no-ore case too.
   Confirm the manifest's `mapName` matches the active island map. Captures
   made before 2026-09-01 do not have this metadata.
   Verify a mine no-op and an access-only repair leave the mining arm intact.
8. Abort recording through the progress toast and load another world during
   encoding. Temporary output must not become a case, and accepted mining
   must not be rolled back. The arm is one-shot and was consumed when encoding
   began: explicitly run `atd_access_replay_arm` again before retrying the same
   case name. Ensure access arms are not consumed by mining.
9. Run all existing access corpus cases sequentially through the candidate DLL
   with matching game assemblies. Only the trivial case has been checked in
   this implementation session; the expensive case was stopped because of
   memory pressure.

The original ore spike has not been reproduced or fixed. Promote representative
real mining cases before changing the algorithm or evaluating spike filters.
