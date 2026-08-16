# Issue tracker: GitHub

Issues and PRDs for this repo live as GitHub issues in
`Kayser1444/CoI_AutoTerrainDesignations`. Use the `gh` CLI for all operations.

## Conventions

- **Create an issue**: `gh issue create --title "..." --body "..."`.
- **Read an issue**: `gh issue view <number> --comments`, including labels.
- **List issues**: `gh issue list --state open --json number,title,body,labels,comments`
  with appropriate label and state filters.
- **Comment on an issue**: `gh issue comment <number> --body "..."`.
- **Apply / remove labels**: `gh issue edit <number> --add-label "..."` /
  `--remove-label "..."`.
- **Close**: `gh issue close <number> --comment "..."`.

Infer the repo from `git remote -v`; `gh` does this automatically when run
inside this clone.

## Pull requests as a triage surface

**PRs as a request surface: no.**

GitHub shares one number space across issues and PRs. Resolve an ambiguous
`#42` with `gh pr view 42` and fall back to `gh issue view 42`.

## When a skill says "publish to the issue tracker"

Create a GitHub issue.

## When a skill says "fetch the relevant ticket"

Run `gh issue view <number> --comments`.

## Wayfinding operations

Used by `/wayfinder`. The **map** is a single issue with **child** issues as
tickets.

- **Map**: an issue labelled `wayfinder:map`, holding Notes,
  Decisions-so-far, and Fog.
- **Child ticket**: a GitHub sub-issue where supported, otherwise a task-list
  entry in the map plus `Part of #<map>` in the child body. Use the appropriate
  `wayfinder:<type>` label.
- **Blocking**: prefer GitHub's native issue dependencies. Fall back to a
  `Blocked by: #<n>` line when dependencies are unavailable.
- **Frontier query**: choose the first open, unassigned child without open
  blockers in map order.
- **Claim**: assign the ticket to the driving developer.
- **Resolve**: comment with the answer, close the child, and add the context
  pointer to the map's Decisions-so-far.
