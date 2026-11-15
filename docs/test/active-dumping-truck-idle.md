# Active dumping truck job acquisition

[Hub topic 21295](https://coigame.com/Topic/21295#post-54263) reports that free trucks, including trucks hauling rock from an ore sorter, idle at the dump site. Removing a dump designation or restarting the game can release them.

Replay the reporter's `no ore.save` (SHA-256 `939742BDAF9212C1850AE09796B4296BE20A81420B9A886B8DCA558D84E0AAAD`) from tick 548585 on game 0.8.8d build 633. Load a disposable copy with ATD and the save's existing mod selection. Resume the simulation, run through tick 552269, and check the new log for `Mafi.Core.Vehicles.Trucks.Truck.tryGetJob` in an assertion stack. The assertion means the provider returned success without enqueueing a job.

- Baseline: the local ATD 0.10.0 DLL from November 12 asserted at tick 548875. The same save has several towers with active dumping priorities.
- Patched: Debug build passed with zero warnings and errors. A fresh session verified the rebuilt DLL timestamp and advanced from tick 548585 to 552365 at 5× without the assertion. Neither test saved over the fixture.

ATD's virtual input previously passed vanilla candidate selection even if the truck had no eligible dump designation. The delivery factory hook could then enqueue nothing while vanilla assumed that delivery had been scheduled. The fix rejects those candidates at the native truck eligibility check, including when cargo is being routed to an explicit export destination.

This replay verifies the job acquisition failure against the reporter's save. It does not track individual trucks or establish that every instance of the visual pileup has the same cause.
