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
* Replay and placement succeed, the emitted handoff cells use the corner-crest-selected mining, dumping, or leveling operations, and the landscaped route is Mega-pathable across both seams.
* A seam that does not crest in its first origin may use up to three additional straight origins for the current five-tile Mega clearance; replay must retain and validate the complete span in both V-to-G and G-to-V directions.
* The handoff has a cardinal post-work route from rank two through files 3-6 of the complete eight-file span to captured G. Rank one is supplied by the crest seam; the outer two files on either side never serve as path centers.
* Leveling centers in those middle files are accepted. Mining centers require vanilla terrain-only Mega pathability with props ignored or actual cut work at that center. Dumping centers require vanilla Mega pathability with trees ignored and no uncleared non-tree prop, or actual fill work at that center.
* Trees do not block either handoff direction. A removable non-tree prop in mining/leveling work is treated as cleared. Dumping remains blocked unless its fill works the center or the prop actually protrudes into a neighboring 4x4 origin that is free to retain a cleanup designation. A neighboring origin occupied by fixed/generated work, another handoff, a building, or a reservation does not qualify; cleanup on the dumping origin itself does not qualify either.
* The path-out proof crosses every origin in an extended handoff span and ends on the captured clearance-2 G graph; a corner crest with a blocked middle rank is rejected.

As a control, block the prospective reverse seam or make one of its lanes infeasible. The search must try another boundary/profile or conclude without placing a partial route.
