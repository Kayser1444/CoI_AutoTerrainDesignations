# Accessway V2 G-to-V live test

Status: ready for live verification

Use the Cluster 2 setup that previously exhausted the search while exploring only the disconnected ground component near the start.

1. Restart the game and confirm the newest ATD DLL timestamp in the startup log.
2. Enable A*, select T3 (or AUTO resolving to a Mega), and run Cluster 2 without the main mining designation if that remains the clearest moderate case.
3. Keep the local wooded ground pocket pathable, but ensure reaching the tower or fixed provider requires crossing a non-G mountain section.
4. Run **Create Designations** and inspect `[ATD V2 Search]`.

Expected:

* The trace first follows cheap G, then forms a two-origin V band wherever the combined travel and landscaping cost is best; this may be well before the natural-terrain boundary.
* The summary reports at least `ground=[...,v2g:1,g2v:1]`; a later return to ground normally makes `v2g:2`.
* Both seam directions pay the weighted canonical-center spoke, and decimal diagnostics remain limited to two places.
* The search does not repeatedly repaint the same G pocket or blink identical V bands under different histories. Only the cheapest route to each concrete G center or V band remains active, as in V1.
* Replay and placement succeed, the emitted handoff cells use the required mining/dumping operations, and the landscaped route is Mega-pathable across both seams.

As a control, block the prospective reverse seam or make one of its lanes infeasible. The search must try another boundary/profile or conclude without placing a partial route.
