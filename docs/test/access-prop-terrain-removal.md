# Accessway prop terrain-removal exemption — 2026-09-03

## Scope

Commit-time non-tree cleanup only. Internal margin: `0.5` terrain levels beyond
the live vanilla destruction threshold. No public parameter, replay-schema
change, persistent state, route scoring change, or tree-harvest change.

## Automated verification

- `dotnet build AutoTerrainDesignations.sln -c Debug`: zero warnings/errors.
- `AccessV2FixtureRunner <ATD DLL> <game Managed directory>`: all 13 fixture
  groups pass. `AccessPropTerrainRemovalFixtures` runs in the core group.
- The new adjacent-prop fixture first failed against the extracted old cleanup
  policy with `Adjacent prop still requests Always cleanup despite projected
  undermining beyond the internal margin`. It passes after the correction.
- Boundary coverage includes strict margin equality, live placement offsets,
  scaled burial thresholds, missing/invalid probes, safety-only effects,
  opposing/mixed-direction work, non-finite heights, all three cleanup modes,
  explicit mining/dumping/leveling, and direct targets overriding crossing rays
  both at the prop and at supporting sample corners.
- `git diff --check` passes.

## Captured-case evidence

The private ghost-tower case is
`ghost-tower-redundant-start-leveling-c85bc3ecfee82aa3.atd-access-case`.
Its original log records separate quick-removal requests for both adjacent props
below. The local `.scratch/PropDisruptionAudit` tool reads this immutable case,
replays its stored route, and invokes the production cleanup decision.

| Prop | Placement height | Projected cut ceiling | Expected decision |
| --- | ---: | ---: | --- |
| `(1074,982)` | 46.17676 | 42.53333 | Skip separate cleanup |
| `(1063,976)` | 42.92871 | 42.61322 | Retain cleanup |

Old captures do **not** contain `PlacementHeightOffset`. The diagnostic tests
the explicit hypothetical values `-2` and `0`, not fabricated captured facts.
Both witnesses give the expected decision at both values. Runtime instead reads
the real offset and burial threshold from `TerrainPropData` immediately before
deciding whether to request cleanup. This does not establish actual in-game
destruction or final vehicle access.

## Route-selection regression checks

Candidate replay preserves the exact canonical outcome for:

- `expensive-v2-01-3a322d4481323a82`
- `trivial-ground-handoff-64e6888f229b9a47` (normalizing its legacy encoding)

`farming-automation-bounded-terminal-02202f74896cd6e2` differs from its old
canonical result. An untouched export/build of HEAD
`240519a980815f234319f71cf95a8c9811bfb2af` produces the **same** actual outcome
as this change:
`15e9efc9244340b818da8f4e50366bf1fbf4261a17621b75abaefd20627c3ded`.
Its `costBits`, route metadata, and materialized-plan mismatch therefore
predate this change. No corpus files or expectations were updated.

## Reference verification

ILSpy `11.0.0.9375` was verified through the required update check. The maintained
decompile script found all four assembly outputs current, refreshed/committed
the generated asset catalog (4,050 entries; game 0.8.7b, build 615), and attempted
an ATD manifest metadata update. That manifest update was precisely reverted:
this task makes no release/version changes.

The maintained solution built successfully. The skill's broad static scanner
reported 161 compatible, 4 possible incompatibilities and 79 unresolved
findings; these are not a blanket runtime compatibility verdict and unrelated
findings were not fixed. Its private report is retained under
`.scratch/PropDisruptionAudit/compatibility.md`.

## Remaining in-game check

Load an affected scenario, generate the accessway under **Always**, and confirm
that clearly doomed adjacent props receive no separate cleanup request while
borderline props still do. Let terrain work complete and verify that the skipped
props actually disappear and vehicle access becomes usable. Repeat for dumping.
The side-ray projection remains approximate; this runtime check is not replaced
by pure replay or the internal margin.
