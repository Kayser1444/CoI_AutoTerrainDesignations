# Codex Instructions for ATD

ATD is the workspace entry point, but it is not automatically the target
project. If a request names another maintained mod or shared project, route the
work to that repository before inspecting or editing files. The explicit user
request takes precedence over this repository's current directory.

In particular, route Lighthouse, Beacon, early buildings, research
progression, Hauler, Miner, Lumberjack, Cart Hauler, and other KPIE worker
requests to:

`C:\Users\jonas.adolphson\AppData\Roaming\Captain of Industry\Mods\KaysersPreIndustrialEra`

Route shared logging, translation tooling, common helpers, and other shared
features to:

`C:\Users\jonas.adolphson\AppData\Roaming\Captain of Industry\Mods\CoI_AutoHelpers`

“Lumberjack” refers to the KPIE worker unless the request explicitly concerns
forestry designations. After routing, read the target repository's `AGENTS.md`
and only the relevant shared instructions.

The shared workspace instructions live at:

- `../AGENTS.md`
- `../.github/instructions/coi-maintained-mods.instructions.md`

Read those first. Then read ATD's local Copilot instruction file for the
ATD-specific save-removability and notification constraints:

- `.github/instructions/AutoTerrainDesignations mod instructions.instructions.md`

## Agent skills

### Issue tracker

Issues and PRDs are tracked in this repository's GitHub Issues. See
`docs/agents/issue-tracker.md`.

### Triage labels

Use the five canonical triage labels. See `docs/agents/triage-labels.md`.

### Domain docs

This is a single-context repository. See `docs/agents/domain.md`.
