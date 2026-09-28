# Record sheet paper doll — landing plan

**Status:** built and green on a branch; not yet proposed upstream.
**Source branch:** `feature/record-sheet-paper-doll` (Caleb68864 fork) — 42 files, +4,765/−16.
**Test state at time of writing:** 6,344 tests green across all eight test projects.

## What the feature is

A BattleTech record sheet rendered as an image inside the app: the blank SVG template for the unit's
configuration, overlaid with armour and internal-structure pip clusters reflecting live damage, the
critical-slot table, the heat scale, and the unit's fluff artwork. It appears as a tab on the unit
record sheet panel and can be exported to PDF.

Templates, pips and artwork are **not** bundled. They are fetched through the existing asset provider
configuration, defaulting to MegaMek's own `mm-data` repository, so the feature ships with no vendored
assets and no hardcoded URLs.

## Architecture in one paragraph

Composition is framework-agnostic and lives in `MakaMek.Presentation`: `RecordSheetComposer` takes a
`RecordSheetDiagramData` snapshot plus optional artwork bytes and produces a composed SVG document as
bytes. Turning that into pixels is behind `IRecordSheetRasterizer`, whose only implementation is
Skia-based and lives in the Avalonia head. That seam is the reason the feature is testable without a
UI toolkit, and it must not be collapsed: nothing in `MakaMek.Presentation` may reference Avalonia or
SkiaSharp.

## Why this is twelve PRs

This repository rejects large PRs on size. [#1454](https://github.com/anton-makarevich/MakaMek/pull/1454)
(131 files) was closed with a request to split by layer, and the same feature came back as a ladder of
issues instead. Recently merged PRs for calibration:

| PR | Files | Lines |
|----|-------|-------|
| #1516 Allow retrying the last game connection | 4 | +101/−1 |
| #1532 Offer a retry when the connection degrades | 3 | +179/−1 |
| #1489 Expose pending command state on the client game | 4 | +144/−8 |
| #1528 Band attack-reachable highlights by weapon range | 13 | +175/−70 |

The working bar is **≤13 files and ≤250 changed lines including tests**. 4,765 lines does not fit that
in eight PRs. It fits in twelve, with two unavoidably over (see [Oversized layers](#oversized-layers)).

## Dependency facts

Verified against the branch, not assumed. These constrain the order:

- `RecordSheetComposer` takes `IRecordSheetTemplateProvider` (namespace `Sanet.MakaMek.Assets.Services`)
  as a constructor parameter. **The Assets layers must land before the composer.** This is a
  compile-time dependency, not a preference.
- `RecordSheetDiagramData`, `RecordSheetLayout` and `RecordSheetViewModel` import only
  `Sanet.MakaMek.Core.*`. They are independent of the Assets work and can land in parallel with it.
- `BattleMapViewModel` receives `IRecordSheetComposer?` and `IRecordSheetRasterizer?` as **optional,
  defaulted** constructor parameters. Its layer therefore breaks no existing caller. Keep them
  optional; making them required turns a leaf change into a fan-out across every construction site.

Two chains run in parallel from the start: Assets (1 → 2 → 3 → 4) and Presentation data (5 → 6).
They converge at layer 7.

## The layers

Each row is one PR, from `main`, with one patch-segment bump in `Directory.Build.props`.

### 1. Assets — new asset types and MegaMek defaults

```
src/MakaMek.Assets/Configuration/AssetType.cs
src/MakaMek.Assets/Configuration/MegaMekDefaults.cs
src/MakaMek.Assets/ResourceProviders/ResourceStreamProviderFactory.cs
tests/MakaMek.Assets.Tests/ResourceProviders/ResourceStreamProviderFactoryTests.cs
```

Adds `RecordSheetTemplates`, `RecordSheetPips` and `UnitFluff` to the `AssetType` enum, a
`MegaMekDefaults` constant for the `mm-data` base URL, and the three subpaths the factory maps them to
(`images/recordsheets/templates_us`, `images/recordsheets/biped_pips`, `images/units/meks`).

**Accept when:** the three enum values exist; the factory returns the correct subpath for each; no
behaviour changes for `Units` or `Hexes`.

### 2. Assets — configured resource providers

```
src/MakaMek.Assets/Services/ConfiguredResourceProviders.cs
tests/MakaMek.Assets.Tests/Services/ConfiguredResourceProvidersTests.cs
```

Lazy async resolution of the active provider set for an asset type, gated by a `SemaphoreSlim`.
Re-resolves when the previous attempt produced nothing, so a source that was briefly unreachable is
retried rather than written off for the session.

**Accept when:** providers resolve once and are cached; an empty resolution is retried on the next
call; concurrent callers resolve exactly once.

### 3. Assets — record sheet template provider

```
src/MakaMek.Assets/Services/IRecordSheetTemplateProvider.cs
src/MakaMek.Assets/Services/RecordSheetTemplateProvider.cs
tests/MakaMek.Assets.Tests/Services/RecordSheetTemplateProviderTests.cs
```

Fetches blank templates and pip clusters by name through the layer-2 providers.

**PR description must state** the XML parser configuration and why (see trap 1). A reviewer seeing
`DtdProcessing.Parse` will otherwise read it as an XXE hole.

### 4. Assets — record sheet artwork provider

```
src/MakaMek.Assets/Services/IRecordSheetArtworkProvider.cs
src/MakaMek.Assets/Services/RecordSheetArtworkProvider.cs
tests/MakaMek.Assets.Tests/Services/RecordSheetArtworkProviderTests.cs
```

Optional per-unit fluff art. Missing artwork is not an error and must not log as one — most units have
none.

**PR description must state** the chassis-not-model resolution and the rejected alternative (trap 2).

### 5. Presentation — diagram data snapshot

```
src/MakaMek.Presentation/RecordSheet/RecordSheetDiagramData.cs
tests/MakaMek.Presentation.Tests/RecordSheet/RecordSheetSamples.cs
```

An immutable snapshot of everything the sheet draws: armour and structure values per location,
destroyed parts, critical slot state, heat.

**PR description must state** why it hand-writes `Equals`/`GetHashCode` (trap 3). Without that it
reads as boilerplate a reviewer will ask to delete.

### 6. Presentation — layout

```
src/MakaMek.Presentation/RecordSheet/IRecordSheetLayout.cs
src/MakaMek.Presentation/RecordSheet/RecordSheetLayout.cs
tests/MakaMek.Presentation.Tests/RecordSheet/RecordSheetLayoutTests.cs
```

Where each element sits on the sheet, as data rather than as drawing code.

**Accept when:** critical-slot indices map to the correct column and row — note slots are 1-based and
the arithmetic is `(slot - 1) / rows` and `(slot - 1) % rows`. An off-by-one here is silent: every slot
renders, one row low.

### 7. Presentation — the composer

```
src/MakaMek.Presentation/RecordSheet/IRecordSheetComposer.cs
src/MakaMek.Presentation/RecordSheet/IRecordSheetRasterizer.cs
src/MakaMek.Presentation/RecordSheet/RecordSheetComposer.cs
tests/MakaMek.Presentation.Tests/RecordSheet/RecordSheetComposerTests.cs
```

Loads the template, parses it, overlays pips per the layout and the diagram data, embeds artwork, and
returns SVG bytes. Caches parsed templates **by content hash, not by name** (trap 4).

Oversized — see below.

### 8. Presentation — view model

```
src/MakaMek.Presentation/RecordSheet/RecordSheetViewModel.cs
tests/MakaMek.Presentation.Tests/RecordSheet/RecordSheetViewModelTests.cs
```

Holds the selected unit and exposes its `RecordSheetDiagramData`, raising change notifications when
either moves.

### 9. Presentation — PDF export command

```
src/MakaMek.Presentation/ViewModels/BattleMapViewModel.cs
src/MakaMek.Presentation/UiStates/WeaponsAttackState.cs
src/MakaMek.Localization/FakeLocalizationService.cs
tests/MakaMek.Presentation.Tests/ViewModels/BattleMapViewModelTests.cs
tests/MakaMek.Presentation.Tests/UiStates/WeaponsAttackStateTests.cs
```

Export through the existing `IPdfExportService`, gated with `#if DEBUG` exactly as the existing map
export is. It ships dark; that is intended, not an oversight, and the PR description should say so.

**Append** new constructor parameters after the existing ones. Inserting them mid-signature breaks
every positional construction site for no benefit.

### 10. Avalonia — Skia rasterizer and DI

```
src/MakaMek.Avalonia/MakaMek.Avalonia/Services/SkiaRecordSheetRasterizer.cs
src/MakaMek.Avalonia/MakaMek.Avalonia/DI/CoreServices.cs
src/MakaMek.Avalonia/MakaMek.Avalonia/MakaMek.Avalonia.csproj
src/MakaMek.Avalonia/MakaMek.Avalonia/App.axaml.cs
```

The only `IRecordSheetRasterizer` implementation. Dimensions come from `drawing.Picture?.CullRect`.
Adds the SkiaSharp and Svg.Skia package references.

### 11. Avalonia — the ArmourDiagram control

```
src/MakaMek.Avalonia/MakaMek.Avalonia/Controls/ArmourDiagram.cs
tests/MakaMek.Avalonia.Tests/Controls/ArmourDiagramTests.cs
tests/MakaMek.Avalonia.Tests/Controls/RecordSheetSamples.cs
tests/MakaMek.Avalonia.Tests/HeadlessTestSetup.cs
```

Hosts the rendered sheet, cancels in-flight renders when the data changes, and unsubscribes on detach.
Oversized — see below.

**Accept when:** a render in flight is cancelled by a newer one; disposal during a render does not
throw; detaching from the visual tree unsubscribes so neither the control nor its bitmap leaks.

### 12. Avalonia — record sheet tab and export button

```
src/MakaMek.Avalonia/MakaMek.Avalonia/Controls/UnitRecordSheet.axaml
src/MakaMek.Avalonia/MakaMek.Avalonia/Controls/UnitRecordSheet.axaml.cs
src/MakaMek.Avalonia/MakaMek.Avalonia/Views/BattleMapView.axaml
src/MakaMek.Avalonia/MakaMek.Avalonia.Controls/TemplatedControls/GamePanel.axaml
tests/MakaMek.Avalonia.Tests/Controls/UnitRecordSheetTests.cs
```

The tab hides itself when no template is available, so a failed fetch degrades to the existing text
view rather than an empty pane.

## Oversized layers

**Layer 7 — composer, ~1,130 lines** (616 of class, 477 of tests). One class plus its test file;
regrouping does not help. If the size is challenged, it splits along real seams:

- 7a — template load, parse, validation, content-hash cache (~250 lines). Produces a valid blank sheet.
- 7b — armour and structure pip placement (~250).
- 7c — critical slots, heat scale, fluff art (~250).

Each step is independently demonstrable, which is the argument for offering this split up front rather
than waiting to be asked for it.

**Layer 11 — ArmourDiagram, ~1,075 lines** (332 of control, 701 of tests). The test file is the bulk
and is not padding: it covers the cancellation path, the disposal race and the detach leak. Splitting
control from tests would put an untested control on `main` for one PR, which is worse than the size.
Flag the ratio in the description instead.

## Traps

Each of these was found the expensive way. Each is a thing an implementer will otherwise do wrong.

**1. The XML parser must allow DTDs.** `DtdProcessing.Prohibit` is the reflex for untrusted XML and it
breaks every real asset: MegaMek's pip clusters ship a public DTD with a *used* internal subset, so
prohibiting it fails the parse outright. The working configuration is `DtdProcessing.Parse` with
`XmlResolver = null` and a `MaxCharactersFromEntities` cap — entity expansion is bounded and no
external entity is ever fetched. Do not "fix" this to `Prohibit`; there is a test that will fail, and
the reason is not obvious from the failure.

**2. Artwork resolves by chassis, not model.** Upstream files art as `Atlas.png`, alongside variants
like `Atlas_7A.png`. Those suffixes are not this project's model strings — there is no
`Atlas_AS7-D.png` — so a variant-specific match is a bonus, not the expectation. Mapping between the
two schemes was rejected deliberately: showing a different variant's illustration is worse than showing
the chassis.

**3. `RecordSheetDiagramData` needs structural equality.** It holds dictionaries and sets. C# record
equality compares those **by reference**, so two snapshots with identical contents compare unequal and
the sheet re-renders on every game command. The hand-written `Equals`/`GetHashCode` is a performance
fix, not boilerplate.

**4. Cache parsed templates by content hash, not by name.** Name-keyed caching passes the obvious tests
and breaks the moment a test — or a provider — serves different content under the same name.

**5. Avalonia headless testing has three sharp edges.**
- `HeadlessUnitTestSession` has **no `Func<Task>` overload**. Pass an async lambda and it binds to
  `Action`, the returned task is dropped, and assertion failures vanish. Tests appear to pass. Wrap
  async work in an explicit dispatch helper that returns a task and is awaited.
- A control must be hosted in a `Window` and the render timer pumped before `RenderToPngBytes`
  produces anything. Pump until a real signal — a completion flag, not a fixed sleep.
- **One headless platform per test assembly.** Configuring a second session breaks unrelated tests in
  the same assembly with errors that do not name the cause. The Skia configuration
  (`UseSkia()` plus `UseHeadlessDrawing = false`) belongs in the single assembly-wide setup.

## House rules for each PR

- Branch from `main`. One patch-segment bump per PR in `Directory.Build.props`; the value must exceed
  `main`'s. Major and minor segments are the maintainer's and must never be touched by an agent.
- Lowercase Conventional Commits subject.
- Nothing under `data/` is modified — those assets are MegaMek's, CC BY-NC-SA 4.0, used as-is.
- Type registries under `MakaMek.SourceGenerators` are generated at compile time. Add the type; never
  hand-edit a generated switch.
- Verify with `dotnet test <project>.csproj` per affected project. **Do not** rely on a whole-solution
  run: `MakaMek.Avalonia.iOS` fails restore without the iOS workload pack, which is an environment gap
  unrelated to any change.
- Diff coverage is reported by a bot on every PR. It is informational and has no `fail-under` gate, but
  every new public member should still arrive with a test that exercises it. It counts **changed lines
  only**, so do not chase pre-existing gaps in a file you merely touched — that inflates the diff and
  invites the size objection this split exists to avoid.

### Coverage as it stands

Measured against changed lines only, which is what the bot reports:

| Layer group | Diff coverage |
|---|---|
| Presentation composer (layer 7) | **98.8%** — remainder structurally unreachable |
| Assets providers (layers 1-4) | **99.4%** — remainder is a coverlet artefact on a `catch` clause |
| Avalonia (layers 10-12) | **not measured at all** — see below |

**Layers 10, 11 and 12 will report no coverage data, and their PR bodies must say why.**
`avalonia.yml` instruments only `Sanet.MakaMek.Avalonia.Converters*` and `…Game*`. These layers live in
`…Controls` and `…Services`, so the collector never looks at them: `ArmourDiagramTests.cs` is 701 lines
and none of it will show. A reviewer seeing "no coverage" against a 332-line control will otherwise
assume it is untested, and the honest answer is that the workflow does not look there. Widening the
filter is a one-line change to that workflow and the maintainer's call — offer it alongside layer 10
rather than making it unasked.

## Open questions — maintainer's call, not the implementer's

1. All twelve up front as a tracking epic (as [#1515](https://github.com/anton-makarevich/MakaMek/issues/1515)
   does for the UI overhaul), or one PR at a time with the next opened on merge?
2. Layer 7 as one PR, or pre-split as 7a–7c?
3. Layers 1–4 are generic provider plumbing that the terrain and unit caches could also use. Worth
   checking against [#803](https://github.com/anton-makarevich/MakaMek/issues/803) and the closed
   [#1392](https://github.com/anton-makarevich/MakaMek/issues/1392) before they land, in case they
   should be shaped to match that refactor.
