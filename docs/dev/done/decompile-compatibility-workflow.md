# Decompile and compatibility workflow

The official API comparison at
[API changes in 4.3](https://coigame.com/Modding/Api-Changes/4.3) covers
`0.8.7d → 0.8.8`. It publishes removed/added signatures and a Markdown download:
`https://coigame.com/Modding/Api-Changes/4.3/Download`.

Use it as an additional evidence source before reviewing local changes. It
quickly exposes interface, constructor, generic and property/field changes that
a name-only reflection scanner can miss. It also points to replacement APIs.
Decompilation remains needed for private reflection/Harmony targets, method
bodies, assets and exact hotfix versions. Builds, compiled-reference checks and
runtime tests remain necessary; none alone is a compatibility certificate.

## Codex discovery and migration

The original skill and scanner were found at
`Mods/.agents/skills/decompile/`, not in a Gemini-specific commands folder.
ATD is its own Git root. The shared skill above that root was absent from the
current chat's advertised skill list. An empty `~/.codex/skills/decompile`
directory was also present, without a `SKILL.md`, so it could not supply a skill.

The new maintained skill source is `tools/skills/decompile/`. Install it into
the user's discoverable `~/.agents/skills/decompile` directory using:

```powershell
.\tools\compatibility\install-codex-skill.ps1
```

The installer verifies copied file hashes and refuses to replace differing skill
files. It needs permission to write outside the repository. The original shared
skill remains untouched. Updates to the source skill require reviewing and
syncing the installed copy. Do not maintain two differing copies unknowingly.

Invoke with `$decompile`, or request it by name in prose. If the selector has
not refreshed, start a new chat/restart the app, or read the maintained skill
source directly. No Gemini context or Gemini executable is required.
See [official skill discovery documentation](https://learn.chatgpt.com/docs/build-skills).

## Commands

```powershell
# Reference refresh, official diff, static triage, Debug builds and compiled references.
.\tools\compatibility\check-coi-compatibility.ps1 `
  -Refresh -ApiRelease 4.3 -RunBinaryChecks -PythonPath '<Python executable>'

# Existing references, no builds; import a browser-downloaded official Markdown file.
.\tools\compatibility\check-coi-compatibility.ps1 `
  -ApiRelease 4.3 -OfficialMarkdownPath '<downloaded Markdown>' `
  -SkipBuilds -PythonPath '<Python executable>'
```

Pass `-ManagedPath` for an alternate complete assembly set. `-BaseRef` accepts
an explicit commit/tag in the decompiled repository and saves its source diff.
Do not guess that `HEAD~1` is the previous game version: catalog-only commits
may intervene. API release names such as `4.3` are explicit inputs, not derived
from assembly version arithmetic.

Reports use a fresh timestamped directory in `%TEMP%/coi-decompile-reports`.
`compatibility.md` summarizes the capture, scan, builds and binary check.
It links the full static report and saves machine-readable findings, build logs,
working-tree snapshots, the official diff and its type/source leads.

Every update review also includes **API opportunities**. The capture generates
`official-api-additions.md` for the complete added surface, including APIs our
mods do not currently use and replacement signatures that may not add capability.
The wrapper creates `api-opportunities.md` and marks the analysis pending in the
main report and `evidence.json`. An agent completes that analysis using the skill,
summarizes it in `compatibility.md`, and records completed/limited review status.
Script success alone does not mean the opportunity review was performed.

Prioritize concrete gains for maintained mods: native replacements for fragile
hooks or custom accounting, correctness/lifecycle improvements, performance
opportunities, base-game integration and worthwhile new capabilities. Verify
contracts against matching game source and current mod code; distinguish inferred
benefits from runtime-tested or measured results and retain older-version fallback
plans. The review does not authorize implementing recommendations.

Report depth follows the incremental update. A small patch may merit one short
paragraph, including an evidenced finding of no material opportunities. Do not
repeat the major update's additions as new if its API page remains unchanged for
a hotfix: use a verified prior-build comparison or state the missing range.
Missing official/local evidence is reported as a limitation, never as no changes.

The official cross-check uses source tokens, including inactive branches and
comments. Review the fully qualified type and signature before patching.
Its type-scope count includes nested types; it need not equal the website's
top-level display count. Download errors and version mismatches remain visible.

If shell HTTPS cannot reach the official site, use the page's **Markdown** link
in the browser and pass the file to `-OfficialMarkdownPath`. This records a
local import and hash, not a fresh HTTP fetch. Missing official notes do not stop
independent local checks or silently become a successful compatibility result.

## Decompile side effects

`tools/decompile-coi.ps1` still refreshes the four Mafi source trees and asset
catalog. It now supports explicit Managed/output paths for testing and alternate
assemblies. It no longer edits mod manifests or private changelogs, or marks a
version verified simply because ILSpy ran. Git snapshots/tags are opt-in through
`-SnapshotGit`; existing version tags are not forcibly moved. Follow the workspace
elevation rule before any reference-history operation accessing `.git/index.lock`.

Debug builds retain their existing project targets and may deploy rebuilt DLLs.
The audit wrapper reports this; it does not patch source, package, publish,
advance versions or certify in-game behavior.

## Validation on 2026-10-30

- Imported the real 4.3 Markdown export, SHA-256
  `69d5ab7b6465d47ac6211319b6237738bbdca7747e5af4fb60f418f1344e68da`.
- Confirmed detection of removed `KeyBinding`, added `KbHandle`, expanded
  `IProductBuffer`, changed `JsonWriter`, and removed `LocStr2` constructor.
- Official parser contracts cover nested/removed/added types, source immutability,
  import provenance and rejecting an HTML response as unavailable.
- Isolated fake-ILSpy refresh verifies catalog generation/version metadata and
  a second unchanged run, without touching installed reference output or mods.
- Report-only integration against current maintained source completed, preserving
  the original scanner's limitations and explicitly recording all builds as not run.
- Isolated build-command fixtures verify all five project commands, passing the
  explicit Managed path, running binary checks only after successful builds, and
  reporting/propagating simulated build failures. These are orchestration tests,
  not substitutes for compiling the real mods.
- PowerShell parser checks and bundled skill validation passed.
- Opportunity-review checks preserve unused additions and replacement context;
  the real 4.3 export also produces the additions index. Script-only orchestration
  retains a pending opportunity-review status even when build commands pass.

No new full decompilation, mod builds or in-game compatibility certification was
requested or performed as part of migrating this workflow.
