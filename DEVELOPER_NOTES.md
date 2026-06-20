# Better Map Filter — Dev Notes

Notes to self. How it works + traps I hit.

## Basics

No Harmony / IL patching in this game. You just load a class and poke the live UI at
runtime.

- `[assembly: RegisterModClass(typeof(X))]`, implement `IModBigAmbitions`.
- `[ModEntryOnInitializationLoad]` runs in-game. `OnLoadAsync` / `OnUnloadAsync`.
- Build: **Big Ambitions > Mod Builder > Build & Install**.
- No docs, so I decompiled with `ilspycmd`. `CityMapFilters` is in the global
  namespace; the rest are under `City.CityMap`.

## How it works

Rebuilds the City Map filter list into a 3-column grid of buttons (centered icon,
label below, glows when on).

- `BetterMapFilterMod` — entry point.
- `BetterMapFilterLogic` — spawns a `DontDestroyOnLoad` driver, cleans it up.
- `CityMapFilterEnhancer`:
  - waits for `CityMapFilters`, then
  - `BuildSections()` group headers + switches → `BuildGrid()` grid per category →
    `BuildCard()` restyle each switch → `LateUpdate()` sync the glow.

Tunables at the top: `Columns`, `SpacingX/Y`, `CardCornerRadius`, `OnColor`/`OffColor`.

## Traps

- **Double toggle.** Click made the sound but nothing changed — the prefab button
  had a *persistent* onClick that flipped the toggle on top of mine, so it cancelled
  out. `RemoveAllListeners()` doesn't touch persistent listeners; use
  `SetPersistentListenerState(i, Off)` first, then drive the logic yourself
  (`SetIsOnWithoutNotify` + `OnToggleClick`).
- **Game wouldn't quit.** My `Teardown()` called `ApplyFilters()`, which NREs during
  shutdown (CityManager already gone). The throw escaped `OnUnloadAsync` and hung the
  shutdown. Don't call game systems on unload; `OnUnloadAsync` must never throw.
- **Game re-asserts row `SetActive`** on collapse / ApplyFilters, so re-apply
  visibility in `LateUpdate`.

## Game types used (decompiled)

- `CityMapFilters` (global ns) — `FindObjectOfType`. `ApplyFilters()`,
  `ToggleFilter`, `Toggle(show)`. `searchDropdown` = business search, not filters.
- `CityMapFilter` (`City.CityMap`) — `Toggle`, `category` field, `OnToggleClick(bool)`.
  Toggle-all row has `category == null`.
- `CityMapFilterCategory` — `ToggleAll`, `IsCollapsed`, `AddFilter`.
- `CityMapFilterData` — `buildingType`, `businessTypeName`, `neighbourhood`, `rivalId`.

Hierarchy: `MapFilters > Scroll View > Viewport > Content`. Headers and switches are
flat siblings; skip the `Entry`/`LineEntry` templates.

Log: `%LOCALAPPDATA%Low/Hovgaard Games/Big Ambitions/Player.log` (`-prev` = last run).
