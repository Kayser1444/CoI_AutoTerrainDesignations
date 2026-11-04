---
name: decompile
description: Refresh Captain of Industry references and review update compatibility and new modding opportunities against official API changes, maintained source, builds, and binary checks. Use for CoI update audits and comparison reports; do not package or release mods.
---

# Decompile and review CoI updates

Use the existing workspace tooling; there is no Gemini or model dependency.
Resolve the maintained workspace from the user's project or
`%APPDATA%\Captain of Industry\Mods`. The owner is
`AutoTerrainDesignations/tools/compatibility/check-coi-compatibility.ps1`.
Read workspace and affected-project instructions before running it.

## Choose the requested work

- **Refresh and audit:** pass `-Refresh`; add `-Force` only for a requested full
  refresh or demonstrated stale decompilation.
- **Report only:** omit `-Refresh` and state that the existing references were used.
- **Explicit fixes:** audit first, then prepare the authorized patches. An audit
  alone does not authorize code changes, releases or publishing. Do not ask again
  for permission the user already gave.

Pass the developer's explicit API release (for example `-ApiRelease 4.3`). Do not
derive `4.3` arithmetically from game version `0.8.8`. If unknown, discover it at
`https://coigame.com/Modding/Api-Changes` or continue with the evidence gap stated.

```powershell
& '<Mods>\AutoTerrainDesignations\tools\compatibility\check-coi-compatibility.ps1' `
  -Refresh -ApiRelease 4.3 -RunBinaryChecks -PythonPath '<available Python executable>'
```

The wrapper captures the official Markdown export, records its URL, time and
hash, correlates changed API types with maintained source, runs the migrated
reflection/Harmony triage, and builds ATD, AFD, BDT, KPIE and AH in Debug.
`-RunBinaryChecks` runs the existing helper's installed-member reference check
after successful builds. Use
`-ManagedPath` for a complete alternate assembly set. `-BaseRef` captures a
decompiled-source comparison against a verified local commit/tag; do not assume
`HEAD~1` is the previous game build. Reports go to a new timestamped directory
under `%TEMP%\coi-decompile-reports` unless a destination is requested.

Read [api-audit.md](references/api-audit.md) to interpret or verify the report.

## Review opportunities on every update

Every update report includes both compatibility findings and an **API
opportunities** analysis. Review additions even when they have no matches in our
current source: unused new APIs can be the most useful ones. Look for native
replacements for reflection/Harmony or custom bookkeeping, better robustness or
performance, improved base-game integration, and worthwhile new mod capabilities.

Follow [the opportunity review guidance](references/api-audit.md#api-opportunities).
Complete the generated `api-opportunities.md` and summarize its conclusions in
`compatibility.md` and the user-facing report. The scripts gather candidates and
mark this review pending; running them alone does not complete the analysis.

Scale depth and length to the incremental update. A small hotfix may warrant
only a short finding or an evidenced "no material opportunities found". Do not
pad the report, repeat the previous major update's opportunities as new, or
mistake an unavailable/currently unchanged official page for an empty patch.
Recommendations do not authorize implementation.

## Evidence and side effects

- Official API signatures prioritize review and suggest replacements. They do
  not cover all private reflection, method bodies, asset changes or runtime
  behavior. Confirm findings against the actual targeted assembly/source version.
- A clean name scan or build does not certify compatibility. Review overloads,
  inherited/nested types, conditional branches, serialization and Harmony hooks.
- If official download fails, use its browser-visible **Markdown** link and pass
  the saved file as `-OfficialMarkdownPath`. This is recorded as a local import.
  If unavailable, finish local checks and retain the gap in the report.
- Do not automatically install/update global ILSpy. Record `ilspycmd --version`;
  update when requested or needed for a demonstrated decompiler issue. A failed
  optional update check should not prevent useful offline checks.
- Refresh writes the decompiled reference tree/catalog; builds write normal
  outputs and may deploy DLLs through existing project targets. The wrapper does
  not edit mod source, manifests, changelogs, Git history or tags. Repository
  permissions still apply to sibling builds/reference refresh.
- `decompile-coi.ps1 -SnapshotGit` is a separate, explicit reference-history mode.
  It stages/commits local reference files and creates missing version tags.
  Follow the workspace rule for elevated `.git/index.lock` operations before it.
- Preserve unrelated work. Compare the before/after status snapshots, report
  failures and unsupported checks, and do not advance verified-version metadata
  until the actual verification supports that claim.

Deliver the report path, confirmed failures versus review leads, build/binary
check results, old-version coverage, API opportunities and the remaining in-game
checks. State evidence gaps and distinguish checked opportunities from proposals
that still need runtime validation or measurement.
