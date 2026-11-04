# ATD localization

ATD uses native mod translations on 0.8.8. The 210 fields in
`src/ATD.Localization.cs` have IDs of the form
`AutoTerrainDesignations.<FieldName>`, with a single valid string-ID segment.
Translation text and formatting were preserved during migration; this is not a
new translation pass. Prototype IDs, shortcut preferences and saved settings
were not renamed.

## Startup and language changes

The vanilla loader scans static strings and merges each mod's active translation
bundle. ATD's 0.8.8 payload excludes the legacy private dictionary/rebind adapter;
renderer initialization constructs settings tabs after native merging. `LocStr`
resolves through the native registry, while formatted text and existing UI
controls retain snapshots. Rebuild or refresh a captured UI value when needed;
this change does not repaint every open panel on language change.

Before 4.3, the same mutable fields are translated through AutoHelpers at
renderer initialization. That preserves the older game's startup order. Text
captured before this late pass, including notification content, retains the
older limitation; native loading should not be described by that old rule.

## Bundles and authoring

Bundles are JSON tuples in `translations/`. Use one file per locale, including
native `pt_BR.json` and `zh_Hans.json`; legacy selection understands those names.
The supplied Korean bundle is retained along with the maintained locale set.
Source-tracking metadata uses the same migrated IDs. Preserve numeric and named
placeholders such as `{0}` and `{entity}`. Use AutoHelpers' normal contextual
translation workflow for new or changed text.

Declare a string with its English fallback and translator context:

```csharp
public static LocStr DesigTitle =
    Loc.Str("AutoTerrainDesignations.DesigTitle", "Mining designations", "Title of the mining designations inspector panel.");
```

`AtdLocalization.Tip` appends the ordinary tooltip marker;
`AtdLocalization.PanelTip` adds the inspector-panel marker. Format strings at
the use site without treating already formatted text as a live translation.

## Verification

AutoHelpers `tools/ModLocalizationContracts` exercises the actual native game
engine with the compiled ATD DLL: ID syntax, bundle completeness, format tokens,
early field copies, all supplied locales, missing-key fallback and captured versus
deferred text. Against a separately built 0.8.7d DLL it checks legacy rebind in a
fresh process per language. These checks do not replace in-game UI inspection.
