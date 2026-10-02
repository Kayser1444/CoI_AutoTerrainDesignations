# Localization Architecture
Current as of release: 0.4.0k

ATD uses `CoI.AutoHelpers.Localization` to translate strings at runtime. All
user-visible strings are declared as `public static LocStr` fields in
`AtdLocalization` and are rebound by `ModTranslations.Apply()` once per game
load at renderer init state.

---

## Static LocStr fields

All translatable strings live in `src/ATD.Localization.cs` as `public static
LocStr` fields declared with `Loc.Str(key, englishDefault, comment)`:

```csharp
public static LocStr DesigTitle =
    Loc.Str("panel.designations.title", "Terrain Designations", "...");
```

The English default is used before translations are loaded and as a fallback for
any missing key. `ModTranslations.Apply()` scans the calling assembly via
reflection and rebinds all matching static `LocStr` fields from the loaded
localization tables.

Static `LocStr` fields cover the designation panel, ore composition panel,
farmland preparation panel, corner toolbox items, notification prototype
messages, and farming session status strings.

---

## Tooltip helper

`AtdLocalization.Tip(LocStr s)` returns a `LocStrFormatted` with the ATD mod
marker appended (`\n[ATD mod name/version]`). Use this wherever the sign-off is
expected:

```csharp
.Tooltip(AtdLocalization.Tip(AtdLocalization.DesigCreateTip))
```

The marker is appended at call time using `AutoTerrainDesignationsMod.Tt()`, so
translated text is used automatically.

---

## Format strings

For strings with positional placeholders (`{0}`, `{1}`, …), call
`string.Format` at the use site:

```csharp
string.Format(AtdLocalization.OrePrioritySelectedTipFmt.TranslatedString, coloredName)
```

---

## Farming session strings

`ATD.FarmingPreparationSession.cs` uses a computed-key pattern (`"farming." +
key`) for status strings at roughly 40 call sites. These keys are still mapped
to static `LocStr` fields in `AtdLocalization`, but accessed through two private
helpers that dispatch on the key suffix at call time:

```csharp
private static string FarmingTr(string key, string englishDefault)
private static string FarmingTrFormat(string key, string englishDefault, params object[] args)
```

Each helper uses a `switch` expression over the key suffix and reads the current
`TranslatedString` from the matching `AtdLocalization` field. This keeps all
existing call sites in that file unchanged.

---

## Bootstrap

Localization is applied once per game load from `AutoTerrainDesignationsMod.Initialize`:

```csharp
RegisterAutoHelpersLocalizationLateApply(resolver);
```

This registers a callback at renderer init state that:

1. Probes `<modRoot>/translations/` for JSON bundles.
2. Calls `ModTranslations.Apply()` to upsert translated keys into CoI's
   `LocalizationManager.s_data` and rebind all static `LocStr` fields.
3. Routes diagnostics through `Log.Info` / `Log.Warning` based on severity.
4. Logs the full apply result (upserted keys, scanned fields, rebound fields,
   diagnostic count).

---

## Translation file format

Files live under `translations/`. Each file is a JSON tuple array:

```json
[
  ["panel.designations.title", "Terrain Designations"],
  ["farming.no_tower", "[ATD Farming] No tower selected."]
]
```

ATD currently ships `en.json`, `de.json`, `es.json`, `it.json`,
`pt.json`, `ru.json`, `sv.json`, `zh.json`, and `ko.json`.

The English file is the source of truth for key names and default text. New keys
must be added to every supported locale file. Untranslated entries in
non-English files can use the English text verbatim.

---

## Key naming

Existing keys use a path-style convention without a mod namespace prefix:

| Prefix | Scope |
|---|---|
| `common.*` | Shared level labels (Off, Low, Med, High, Max) |
| `panel.designations.*` | Terrain designation inspector panel |
| `panel.ore.*` | Ore composition inspector panel |
| `panel.farming.*` | Farmland preparation inspector panel |
| `toolbox.*` | Toolbox item tooltips |
| `notification.*` | Notification prototype messages |
| `farming.*` | Farming session status strings |

Preserve existing keys. New keys should follow the same path convention. New
keys that risk collision with other mods using the same CoI localization
namespace should carry an ATD-specific prefix segment.

---

## Known constraints

- **Notification strings are always English.** Notification prototypes are
  registered at `RegisterPrototypes` time, before `ModTranslations.Apply()`
  runs at renderer init state. Translated notification text is not supported.
- **UI panels built before renderer init receive English defaults.** In practice
  ATD panels are built lazily on first open, so they receive translated text
  under normal gameplay. Panels opened before renderer init completes (unusual)
  would show English defaults.
