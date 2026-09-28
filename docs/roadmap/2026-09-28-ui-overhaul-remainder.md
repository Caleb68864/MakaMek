# UI overhaul — layers 5, 7 and 8

**Epic:** [#1515](https://github.com/anton-makarevich/MakaMek/issues/1515), the split of #1453.
**Status:** layers 1, 3 and 6 merged. Layer 2 dropped. Layer 4 is three open PRs. Layers 5, 7 and 8
remain, all three assigned to `Caleb68864`.

This brief covers what is left, so the next layer can go out as soon as the layer 4 PRs merge rather
than being worked out from scratch then.

## Where layer 4 stands

Four PRs open upstream, all reporting mergeable, to be merged in this order:

| PR | Adds | Version |
|----|------|---------|
| #1533 command feedback | +216 | 0.64.14 |
| #1535 turn guidance and attack cost | +444 cumulative | 0.64.15 |
| #1536 unit inspection, panel exclusivity | +939 cumulative | 0.64.16 |
| #1534 initiative winner in the banner | | 0.64.17 |

#1536 closes [#1521](https://github.com/anton-makarevich/MakaMek/issues/1521). Nothing is owed on
these; they are waiting on review. Two review comments on #1533 are addressed in code but were never
replied to.

## The central problem: the source branches are cumulative

Layers 5 and 7 were built **before** the epic split, each branched from the previous layer's branch
rather than from `main`. Their diffs against `main` therefore contain their predecessors:

| Layer | Issue | Branch | Diff vs `main` | Epic's isolated estimate |
|-------|-------|--------|----------------|--------------------------|
| 5 | #1522 | `feature/mech-selection` | 23 files, +2,410/−17 | ~7 files |
| 7 | #1524 | `feature/ui-converters` | 47 files, +3,395/−17 | 23 files |

Neither branch can be proposed as-is. Each layer has to be **re-cut from `main`** carrying only its own
files. The content exists and is tested; the work is extraction and re-verification, not authoring.

Do not try to isolate by diffing against the branch a merge commit names as its parent — that was tried
and gives a misleading answer, because the parent branches were themselves cumulative and some no
longer exist locally. Cut from `main` and add the layer's files explicitly, by path.

## Layer 5 — searchable, budgeted mech selection (#1522)

Unit selection in `NewGameViewModel` becomes searchable and budget-aware: filter as you type, and see
remaining tonnage as units are added, so picking a force from a long variant list works on a phone.
Presentation only.

**Source:** `feature/mech-selection`. Take only the `NewGameViewModel` / unit-selection files and their
tests; leave everything inherited from earlier layers behind.

**Independent of layer 4.** It touches the new-game screen, not the battle map, so it does not wait for
the open PRs. This is the layer to send next.

**Accept when:** filtering narrows the list as the query changes; the remaining budget reflects the
current selection; the existing selection flow is unchanged when no query is entered.

## Layer 7 — Avalonia converters for the tactical HUD (#1524)

Eleven single-purpose value converters, one class each, plus tests:

```
src/MakaMek.Avalonia/MakaMek.Avalonia/Converters/
  CompactPanelLayoutConverter.cs      DrawerPinTextConverter.cs
  HeatRiskToBrushConverter.cs         UnitActionHintConverter.cs
  UnitEventBadgeConverter.cs          UnitIntegrityPercentConverter.cs
  UnitMovementSummaryConverter.cs     UnitPositionSummaryConverter.cs
  UnitResourceWarningConverter.cs     UnitStatusTextConverter.cs
  UnitStatusToBrushConverter.cs
```

**Source:** `feature/ui-converters`, converters and their tests only.

**Depends on layer 4**, since the converters read the view-model surface that #1536 introduces. Cannot
go out before those PRs merge.

**Open question already put to him** on #1515 and #1524: at 23 files this is the largest layer.
Splitting a converter set across several PRs seemed worse than one PR of small independent classes, and
he was offered the split. If he has not answered by the time layer 4 merges, send it whole and offer
again in the description rather than deciding unilaterally.

**Note:** converters take their services by injection, not a static `Initialize()`, per the pattern
established when [#1429](https://github.com/anton-makarevich/MakaMek/issues/1429) was closed.

## Layer 8 — the views (#1525)

The `.axaml` work that finally renders everything layers 4 to 7 built. The epic records this as "not
built yet", which is **stale**: the view work exists on `backup/ui-overhaul-wip` (16 September, the
pre-split #1453 branch), including

```
Controls/UnitStatusBarItem.axaml        +124  (new)
Views/BattleMapView.axaml               +161
Views/BattleMapView.axaml.cs            +84
Controls/UnitRecordSheet.axaml          +126
Views/AvailableUnitsTableView.axaml     +165/−142
Controls/WeaponSelectionPanel.axaml     +25
Controls/UnitBasicInfoPanel.axaml       +8
TemplatedControls/GamePanel.axaml       +9
```

It predates the split, so it binds to a view-model surface that has since changed across layers 4 to 7.
Treat it as **raw material to rebase, not as a branch to propose**: re-cut from `main` on top of the
merged layers, then fix the bindings against the final view models.

Budget for binding drift. A wrong binding path in Avalonia fails **silently at runtime** — no compile
error, no exception, just an empty control — so this layer needs to be run and looked at, not merely
compiled and unit-tested. Use the `run` skill to launch the desktop head and drive it to a HUD screen.

**This layer probably wants splitting**, on the evidence above: roughly 700 lines of `.axaml` across
eight files, of which `BattleMapView` and `UnitStatusBarItem` are the substantial parts. A sensible cut
is the status bar item and its host first, then the battle map view, then the record sheet and
selection-table changes. Decide once the real post-rebase size is known, not from these figures.

## Outstanding offer to the maintainer

From the #1515 epic, unanswered:

> Nothing in layers 4 to 7 renders: they are view models and converters, and the `.axaml` is layer 8.
> If you would rather judge the HUD's density before taking more view models, I can bring those views
> forward and build them on top of #1535 and #1536 so it can be run and looked at.

If he takes that up, layer 8 jumps ahead of layers 5 and 7 and the order above changes. Worth a nudge
when the layer 4 PRs are reviewed, because four merged layers that render nothing is a lot to accept on
trust.

## Recommended order

1. **Layer 5** — send now; it does not depend on layer 4.
2. **Layer 7** — when the layer 4 PRs merge, whole unless he asked for the split.
3. **Layer 8** — last, unless he takes the offer above, in which case first.

## House rules

Same as every PR here: branch from `main`, one patch-segment bump in `Directory.Build.props` above
whatever `main` is at the time, lowercase Conventional Commits, and per-project `dotnet test` rather
than a whole-solution run (`MakaMek.Avalonia.iOS` fails restore without the iOS workload pack, which is
an environment gap unrelated to any change).

Avalonia headless testing has sharp edges that are documented in
[2026-09-28-record-sheet-paper-doll.md](2026-09-28-record-sheet-paper-doll.md) under Traps — read trap
5 before writing tests for layers 7 or 8.

## Not planned here

The README's "Future (possible) phases" — paved areas and buildings, advanced tech rules, vehicles and
infantry, custom unit and map tooling, a MonoGame 3D version — have no issues, no PRD and no agreed
scope. There is nothing to brief yet, and writing one would be inventing a direction rather than
recording one.
