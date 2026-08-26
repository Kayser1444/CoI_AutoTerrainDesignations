# V2 removable-prop traversal handoff

Status: fixed and runtime verified on 2026-08-26.

## Bug

The Cluster 2 scenario stopped using the older, cheaper V2-to-G handoff near
`(728,1688)`. The materialized route instead continued along a more expensive
western approach. An intermediate fix restored route completeness but made the
search explore a combinatorial G frontier for several minutes.

The reported props at `(729,1691)` and `(815,1684)` are independent single
rocks. Goal-connected and non-goal-connected physical ground have the same
local V-to-G handoff eligibility; goal connectivity is relevant only to the
remaining route.

## Diagnosis

The rocks and projected-ground connectivity were not the final blocker. Trace
comparison with the known-good route established that the cheap route:

1. travels west to `(736,1684)`;
2. strafes south to `(736,1688)`;
3. continues west to `(728,1688)`; and
4. forms a mining handoff through contacts `(728,1688)` and `(728,1692)` into
   ground at `(728,1693)`.

The current search generated that return strafe and accepted the terminal
ground entry. It then discarded the entry because `SearchKey` represented a G
label only by its concrete center. A cheaper, incompatible route had already
reached the same center with projected work that blocked the only goal suffix.
Center-only label dominance therefore erased the later history whose suffix was
valid.

An initial correction added the projected-work history signature to every G
label. That was complete for this case but propagated every competing V history
through the entire ground component, causing the observed G-frontier explosion.

## Fix

Ground labels are history-qualified only at an actual V-to-G entry node. This
lets competing handoffs at the same center survive long enough for their first
ground continuation to be validated. After ordinary G traversal begins, labels
collapse back to the cheapest arrival per concrete center, preserving bounded
ground-search behavior.

V-label dominance remains unchanged. Removable rocks remain cleanup-eligible,
and local handoff evaluation does not distinguish goal-connected from
non-goal-connected ground.

## Regression coverage

`AccessV2Fixtures.ValidateSearch` now contains a minimal graph where two V
routes enter the same G center. The cheaper history blocks the only goal suffix;
the later, more expensive history permits it. Before the fix the fixture failed
with `NoPath`; after the fix it reaches the goal.

The fixture intentionally exercises the production search-label seam rather
than rock classification. It guards the actual regression: a valid V-to-G
history must not be suppressed by a cheaper incompatible history at the same
ground center.

## Verification

- Debug build: zero warnings and zero errors.
- All `AccessV2FixtureRunner` suites pass.
- Case-specific `[DEBUG-rock-lifecycle]` instrumentation was removed.
- Runtime Cluster 2 result on 2026-08-26: success, cost `5009.72`, 23 V states,
  318 G states, and the expected mining handoff at `(728,1688)` with contacts
  `(728,1688)/(728,1692)`.
- The user confirmed that the cheaper materialized path and boulder penetration
  are restored.

The runtime still took about 80 seconds in this large traced Debug scenario.
That cost is separate from the correctness regression and should not be
interpreted as a claim that general V2 search performance is solved.
