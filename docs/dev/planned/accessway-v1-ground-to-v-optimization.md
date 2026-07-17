# V1 Ground-to-V Optimization Backport

## Status

**Implemented; live measurement pending**

## Shared-edge leveling rule

V1 and V2 now use the same sign-symmetric geometric rule for a direct
leveling entry. The captured G vehicle center lies on a canonical four-tile
grid edge: `x & 3 == 0` for an X-oriented entry or `y & 3 == 0` for Y. Travel
orientation selects which of the two cells adjacent to that shared edge
becomes V.

V1 additionally requires the transverse coordinate to be local lane one or
two. Both adjacent V1 origins are derived exactly. V2 derives the adjacent
lane origin and its width-two companion. Negative coordinates use the same
bitwise residue rule.

The captured G terrain must be on an integer designation level. Candidate
profiles are limited to flat, rising, or falling along the travel axis and are
translated so their complete entry edge equals that G level. The generated
origin still passes ordinary tower-area, useful-height, profile, history,
side-ray, cleanup, cost, and label-dominance checks. Leveling owns the
post-work V surface, so only the expensive general handoff evaluator is
skipped.

The direct predicate is deliberately not cached. Its residue, lane, and edge-
height checks are cheaper than a composite cache lookup, while history and
cost validation remain route-specific. Ordinary best-label replacement
therefore stays authoritative until a state is settled; a later cheaper route
is not suppressed by first-success ownership.

Mining and dumping retain the complete existing V1 handoff evaluator. V1 also
retains its broad legacy origin/height enumeration as fallback for those
operations and for non-direct leveling contacts.

## Forward smooth-face leveling

V-to-G in both V1 and V2 recognizes the symmetric case where every sample in
the ground-facing height signature is within the existing epsilon of level.
It selects leveling directly for that face. The leveling designation may
ignore props, while the normal terrain, vehicle-clearance, history, cost, and
replay validation remains authoritative. Any non-level sample retains the
ordinary mining/dumping handoff classification.

## Dumping prop burial

V1's shared prospective-work evaluator applies the same rule to G-to-V and
V-to-G; the distinct V2 evaluators apply it in both directions as well. A
non-tree prop blocks dumping unless the selected profile, interpolated at the
prop's exact within-tile position, raises the future terrain strictly more
than the prop's scaled burial threshold above its placement height. The same
test filters emitted cleanup so a prop is omitted only when that concrete
designation proves it buried. Mining and leveling continue to ignore props.

## Ground suffix completion

V1 A* reuses the exact reverse ground-goal field after reaching a
tower-connected G component. It follows Bellman-descending ground edges while
validating diagonal side corridors, generated-history disturbance, cleanup
topology and cost, the request cost limit, final materialization, and the
ordinary goal rejection callback. A failed suffix proof leaves the search
unchanged and resumes ordinary G/V expansion.

## Diagnostics

The V1 search summary reports direct-leveling accepts plus ground-suffix
attempts, successes, fallbacks, and concrete steps. Existing origin, profile,
handoff, history, ray, cleanup, and relaxation counters retain the remaining
breakdown.

## Deferred

Uniform lane-operation pruning is V2-only. Multi-label history-sensitive
search is deliberately out of scope: retaining every route to a concrete G or
V state would recreate the state explosion that label dominance prevents.
