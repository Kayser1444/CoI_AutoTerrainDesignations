# Excavation without a Mine Tower

In ATD settings, open **World Settings** and enable **Unassigned excavators
excavate**. This option starts off and is saved per world.

Unassigned excavators mine existing Mining designations outside all Mine Tower
areas. They continue to appear unassigned and can be assigned to a tower at any
time. Paused towers still exclude their areas. Material priority remains a
preference in vanilla selection.

Each excavator requests one global truck when it starts filling a bucket. The
truck follows it between designations and stays until full. If later cargo is
incompatible, the truck delivers its partial load while the excavator retains
the scoop for a replacement. Without a truck, the excavator holds one scoop.
If delivery has no eligible destination, the loaded truck waits with the
vanilla warning and blocks replacement until it starts moving.

Pickup and delivery use the Logistics Zones containing the excavator when the
truck was requested. Movement does not change that request. Deleted zones lose
permission; remaining captured zones continue to apply, with Default Zone used
when none remain.

Manual excavator orders, assignment, pause, or disabling the option interrupt
the work and discard the bucket as vanilla does. Empty pickups return to
logistics; loaded trucks finish delivery. Cancelling, assigning, or pausing the
paired truck permits a replacement while the excavator retains its scoop.

This feature is currently awaiting in-game validation. See the
[test plan](../test/default-mine-excavators.md) for save/load and removal checks.
