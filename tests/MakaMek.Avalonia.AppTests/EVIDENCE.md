# Harness evidence log

This project is a large, dull chunk of test infrastructure. The case for carrying it has to be made
with evidence, not assertion, so every use of it gets a row below — including the uses that found
nothing, because a harness that only ever agrees with the existing tests is not worth its weight.

**Keep this file updated in the same commit as the test that produced the entry.** A log written
later is a log nobody trusts.

## What this harness does that the existing Avalonia tests cannot

`tests/MakaMek.Avalonia.Tests` constructs views directly — `new BattleMapView()`, `new
TurnNotificationBanner()`. Nothing there boots `App`, builds the real service graph, or navigates.
So no test in that project can see a defect that only appears once the application is assembled.

It has to be a separate project: `AvaloniaTestApplication` is assembly-wide, the existing project
claims it for a stub `TestApp`, and only one headless platform may exist per assembly.

## Columns

| Date | Feature under test | What the existing tests said | What the harness showed | Would a human have caught it first? |
|---|---|---|---|---|
| 2026-09-29 | *(motivating case, not a harness catch)* initiative winner banner, PR #1534 | 10 tests green; mutation showed **1 of 14** could detect the defect | n/a — harness did not exist yet. The maintainer found it by playing the game: *"I don't see Initiative results banners, the movement phase one comes right after the initiative phase announcement, nothing in between"* | Yes, and he did. That is the cost this harness is meant to remove |
| 2026-09-29 | harness itself — boot and render | n/a | Real `App` boots headlessly, builds its `ServiceProvider`, and `MainWindow` renders a frame with varying pixels. No blockers in `RegisterDesktopServices` | n/a |
| 2026-09-29 | banner binding chain, `BattleMapView` to `TurnNotificationBanner` | control tests build the banner standalone; view model tests never render. Nothing covered the join | Binding resolves and the queue reaches the control inside the real view, with a view model from the real service graph | No. This is a gap neither side's tests reach |
| 2026-09-29 | *(negative result, recorded deliberately)* can the harness catch a broken binding path? | n/a | **No, and it does not need to.** Typing `TurnNotificationsTypo` into `BattleMapView.axaml` fails the **build**: this project sets `AvaloniaUseCompiledBindingsByDefault` and the view declares `x:DataType`, so a bad path is a compile error, not a silent runtime failure | The compiler catches it first |

## Rules for entries

- Record what the **existing** tests said first. The argument for this project is the gap between
  that and reality, so an entry without it proves nothing.
- If the harness found nothing, say so. Those rows are the honest denominator.
- Quote the maintainer verbatim where a defect was reported by him — that is the strongest evidence
  that the gap is real and not self-assessed.
- Note where a defect was reachable only by rendering or navigating. Those are the rows that justify
  a headless *application* harness rather than more view tests.

## What this harness is *not* for

Compiled bindings are on by default in `MakaMek.Avalonia` and the views declare `x:DataType`, so a
misspelled binding path is a build error. Do not justify this project on catching those; the compiler
is faster and more reliable at it. The gap it covers is narrower and harder to reach: whether an
announcement actually arrives on screen, in what order, and whether the assembled application
behaves once DI, navigation and rendering are all involved. Those are invisible to the compiler and
to view model tests alike.
