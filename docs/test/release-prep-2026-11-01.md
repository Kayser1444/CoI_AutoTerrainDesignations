# ATD 0.9.5 experimental technical release

Prepared 2026-11-01 for Captain of Industry 0.8.8 Build 625. The owner reported that rebuilding eliminated the repeated truck logistics exceptions. Extensive gameplay testing of the entire Update 4.3 compatibility patch remains outstanding; startup, metadata and focused fixture checks are not extended gameplay certification.

## Scope

- Recompile existing active-dumping calls against Build 625's changed DumpingJob.Factory.EnqueueJob signature. No logistics policy, mixed-haul handling or save format change.
- Use Build 625 for the Update 4.3 payload. Earlier 4.3 builds share the same assembly version but lack this overload and are not supported by this payload.
- Retain a separately compiled 0.8.7d Build 619 payload and the automatic version selector.
- Preserve add/remove-from-save flags and existing version-only max_verified_game_version; this field cannot encode a game build or extensive gameplay qualification.
- Complete four pending translation-source entries across existing supported locales. English escape symbols and the ore-quality explanation were reviewed; Korean already matches the current source. French is not an ATD supported locale and is not introduced by shared-tool defaults.
- Build from committed release-preparation ATD sources and helper commit `e5afbdd7ceccbc9a0e052405aec829c1646d6cd5` in an isolated local build workspace. Uncommitted development/tooling changes are excluded.

## Evidence and remaining checks

The Build 625 audit refreshed all four managed reference trees, compared local Build 624 source and the Build 622 tag, and passed Debug builds, ATD Default Mine hooks/IL guards, BDT mixed-buffer fixtures and KPIE worker contracts. The stock binary checker missed nested factory references; a diagnostic full-type-name checker reproduced the old ATD failure and resolves the rebuilt payload. Release payload checks must include nested members as well.

Final Release ZIP hashes, exact entries and payload checks will be recorded in `release-0.9.5-package.json`. Live Hub page 4's Markdown was reviewed against its clean tracked source (same normalized content); the page body needs no feature edit for this patch. Public changelog and packaged manifest explicitly retain the experimental warning.

Remaining gameplay testing includes mixed storage pickup and dumping, filter changes before/after pickup, sorter exports and full/offline sorters, Default Mine delivery, logistics zones, and save/load/removal. The owner's exception-free observation is limited runtime evidence, not verification of those scenarios.
