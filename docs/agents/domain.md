# Domain Docs

How engineering skills should consume this repository's domain documentation.

## Before exploring, read these

- `CONTEXT.md` at the repository root.
- Relevant decision records under `docs/adr/`.

If either location is absent, proceed silently. Domain documentation is
created or extended only when project terminology or architectural decisions
need it.

## File structure

This is a single-context repository:

```text
/
|-- CONTEXT.md
|-- docs/adr/
`-- src/
```

## Use the glossary's vocabulary

When an issue, proposal, hypothesis, or test names a domain concept, use the
term defined in `CONTEXT.md`. Do not drift to a synonym the glossary explicitly
avoids. If a necessary concept is missing, reconsider the term or note the gap
for domain-modeling work.

## Flag ADR conflicts

Surface any conflict with an existing ADR explicitly instead of silently
overriding it.
