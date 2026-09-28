# AGENTS.md

This file provides guidance to AI coding agents when working with code in this repository.

## Overview

MakaMek is a cross-platform, turn-based tactical BattleTech implementation built with **.NET 10** and **AvaloniaUI**. It is inspired by [MegaMek](https://megamek.org/) but prioritizes simplicity and a mobile-/web-first UX. Runs on Windows, Linux, macOS, Web (WASM), Android, and iOS.

## Build & Test Commands

- **Build the whole solution:** `dotnet build MakaMek.slnx`
- **Run all tests:** `dotnet test MakaMek.slnx`
- **Run one test project:** `dotnet test tests/MakaMek.Core.Tests/MakaMek.Core.Tests.csproj`
- **Run a single test / filter:** `dotnet test tests/MakaMek.Core.Tests/MakaMek.Core.Tests.csproj --filter "FullyQualifiedName~SomeTestClass"`
- **Coverage (mirrors CI):**
  ```bash
  dotnet test tests/MakaMek.Core.Tests/MakaMek.Core.Tests.csproj \
    /p:CollectCoverage=true /p:CoverletOutputFormat=opencover \
    /p:ExcludeByAttribute=GeneratedCodeAttribute /p:Include=[Sanet.MakaMek.Core]*
  ```
  The coverage filter uses the source assembly name (test assembly name minus `.Tests`). See `skills/coverage-check`.
- **Diff-coverage (PR pipeline):** [Cocodif](https://github.com/sanet/Cocodif) runs as a composite GitHub Action in each coverage workflow. It parses the coverlet OpenCover XML, computes `git diff --merge-base` against the PR base branch, and posts a per-file diff-coverage report as a sticky PR comment (one per module). The action is informational only — no `fail-under` gate. Each module workflow passes its own `coverage.opencover.xml`, `include` globs scoped to `src/<Module>/**`, and a unique `comment-marker` so reports don't collide. For local diff-coverage, install the CLI: `dotnet tool install --global Sanet.Cocodif`.
- **Run the desktop app:** `dotnet run --project src/MakaMek.Avalonia/MakaMek.Avalonia.Desktop`

Assembly/root namespaces are prefixed `Sanet.` (e.g. `MakaMek.Core` → `Sanet.MakaMek.Core`), even though project/folder names omit it.

## Testing Conventions

Tests use **xUnit** + **Shouldly** (assertions) + **NSubstitute** (mocking). UI (Avalonia) is intentionally excluded from coverage; presentation logic lives in `MakaMek.Presentation` (ViewModels/UiStates) specifically so it *can* be unit-tested without the UI. Prefer adding logic there over the Avalonia layer.

### Writing code that reports clean coverage

Two different reports land on every PR and they measure different things. **Cocodif** (the per-module sticky comment) counts *changed lines* that were executed. **Codecov** additionally counts *partial branches*, so a line that runs but only ever takes one side of a condition lowers its patch percentage and earns an `:x:`. Write for both:

- **Cover changed lines from the owning module's own test project.** Coverage is collected per module with `/p:Include=[Sanet.MakaMek.<Module>]*`, so a `MakaMek.Presentation` line is only covered by `MakaMek.Presentation.Tests`. Exercising it from `MakaMek.Avalonia.Tests` does not count. A PR touching several modules must satisfy each module's report separately.
- **Do not add unreachable defensive guards.** A `?? fallback`, `x == null` check, or `is not null` test that no caller can reach is a branch that can never be covered. If a private helper's only caller has already established a value, pass it in as a parameter rather than re-checking it.
- **Assert single-expression bool properties on both sides.** View-binding flags such as `IsSomethingVisible => CurrentState is SomeState && Something != null` are not read by anything else until the `.axaml` layer exists, so without a test they are reported uncovered, and with only one assertion they are reported partial.
- **Read `field ??=` cached members twice.** Lazily created commands need a second read for the cached side of the null-coalescing assignment, e.g. `sut.ZoomInCommand.ShouldBeSameAs(sut.ZoomInCommand)`.
- **Cover the null-object paths that are real.** `Game` is null before a game is attached, and `PhaseStepState?.ActivePlayer` is null between turns; both are reachable and need their own assertions.
- **Exercise filters rather than assuming them.** A `Where`/`Any` predicate needs at least one item on each side — a shutdown or wrecked unit alongside a healthy one, an unselected weapon alongside a selected one.
- **Check before pushing** with `skills/coverage-check`, which runs the same coverlet command and Cocodif report CI uses.

### Trusting your own tests

A passing run is only evidence if the test was capable of failing. These are all real mistakes made in this repo, each of which produced a green suite that verified nothing:

- **Prove a new test file can fail before trusting it.** Temporarily break one assertion in it and confirm a red run. Do this once per new test class, and after any change to how the class dispatches or sets up work.
- **Read an unfamiliar framework API's real overload set** rather than inferring it from the shape of the call. `HeadlessUnitTestSession` has no `Func<Task>` overload, so `await Session.Dispatch(async () => { ... }, ct)` resolves to `Dispatch<Task>` and returns `Task<Task>`; the single `await` unwraps the outer task, drops the inner one, and every assertion failure inside is lost on an unobserved task. Nothing warns. Be suspicious of any awaited call whose static type could be `Task<Task>`.
- **Assert the oracle for pixel and render comparisons.** A control that is only `Measure`d and `Arrange`d never paints, so every capture is identical — which silently satisfies any "these renders differ" assertion. Host the control in a `Window`, then pump (`Dispatcher.UIThread.RunJobs()`, `UpdateLayout()`, `AvaloniaHeadlessPlatform.ForceRenderTimerTick()`) before capturing, and have one test prove that a blank and a drawn control produce different, decodable bytes. Rasterising needs `UseSkia()` with `UseHeadlessDrawing = false`; there can be only **one** headless platform per test assembly, so configure it in `HeadlessTestSetup`, never as a second session.
- **Pump, do not sleep.** Work queued at `DispatcherPriority.Background` never runs inside a bare `await Task.Delay(...)`, so poll loops waiting on it spin out and captures race it.
- **Make async arrivals deterministic.** Gate a fetch behind a `TaskCompletionSource` when a test needs a "before" state; otherwise it can land during the baseline and both observations match.
- **`Substitute.For<T>` on a class needs every constructor argument** — Castle does not apply optional-parameter defaults.

### Two traps worth knowing

- **`record` does not give value equality over collection members.** `EqualityComparer<T>.Default` on `IReadOnlyDictionary`/`IReadOnlySet` is reference equality, so a projection that rebuilds its collections never compares equal to the previous one. Gating change notification on that equality then fires on every rebuild. Implement `Equals`/`GetHashCode` over the contents, and add the negative test: unchanged state raises no `PropertyChanged` and triggers no re-render. Count the expensive operation and assert the count.
- **Every subscription needs a removal point on a lifecycle hook of the subscriber** — `OnDetachedFromVisualTree`, `Dispose` — not only inside a property-change handler. A control subscribed to a view model that outlives it stays alive with everything it holds, and no functional test notices.

## Versioning (required for PRs)

`Directory.Build.props` holds a single `<VersionPrefix>` for all packages. **Every PR that modifies files under `src/` must bump this version** — `pr-version-check.yml` fails the PR if the version is not greater than `main`. Bump it as part of your change.
- The version should only be incremented once per PR
- Test-only, docs-only, or infra-only PRs do not require a version bump.
- Agents may only bump the **patch** segment (e.g. `0.63.10` → `0.63.11`). Never change Major or Minor without explicit human approval.
- Commit messages follow Conventional Commits] (`feat`/`fix`/`docs`/`refactor`/`chore`/`build`, imperative subject).

## Architecture

The codebase is a layered set of projects; dependencies flow **Avalonia → Presentation → Core** (Core has no UI dependencies).

### Layers (`src/`)
- **MakaMek.Core** — Engine and all domain logic: game loop, state machine, phases, commands, units/components, combat & piloting mechanics, dice. No UI. This is where game rules live.
- **MakaMek.Map** — Hex-grid map representation, coordinates, terrain, map generation.
- **MakaMek.Presentation** — ViewModels and **UiStates** (per-phase interaction logic). The testable bridge between Core and the UI. Uses the `Sanet.MVVM` framework (see the `sanet-mvvm` skill before touching ViewModels/navigation/DI).
- **MakaMek.Avalonia** — AvaloniaUI views + per-platform heads (`.Desktop`, `.Android`, `.iOS`, `.Browser`) and shared `.Controls`.
- **MakaMek.Bots** — AI bot framework: per-phase decision engines, plus an experimental LLM-powered bot (agents/tools). Ships as a Docker BotAgent.
- **MakaMek.Services** — Platform-abstraction interfaces (files, images, dispatcher, PDF export) with Avalonia implementations in `MakaMek.Services.Avalonia`.
- **MakaMek.Assets** — 2D asset management (unit/terrain images).
- **MakaMek.Localization** — Localized strings.
- **MakaMek.SourceGenerators** — Roslyn generators that build type registries at compile time (command types, component providers, movement-cost / roll-modifier / PSR-context resolvers). If you add a new command/component/modifier type, the registry is generated — don't hand-maintain a switch.

### Core game model (the "big picture")

Client-server architecture even for local play (RX-based transport locally, SignalR for LAN):
- **`BaseGame`** is the abstract root. **`ServerGame`** owns authoritative state, drives phase transitions, validates and applies commands, and broadcasts updates. **`ClientGame`** submits commands and mirrors state. `GameManager` handles lifecycle/lobby/network/DI wiring.
- **Commands** (`IGameCommand`, split into Client/Server) are the *only* way state changes — they are serialized and sent over the transport (`Services/Transport`) via `CommandPublisher`. State changes propagate through observables + commands.
- **Phases** implement `IGamePhase`, coordinated by `PhaseManager`, in order: Start → Deployment → Initiative → Movement → WeaponsAttack → WeaponAttackResolution → Heat → End. Each transition calls `ResetPhaseState()` on units. `PhysicalAttackPhase` exists but is not yet wired into the flow.
- **Mechanics** (`Models/Game/Mechanics`) are calculator services (`IToHitCalculator`, `IPilotingSkillCalculator`, `IFallProcessor`, critical hits, damage transfer, heat effects, etc.), performed server-side and broadcast for client display.

Two key patterns documented in `docs/architecture/` worth reading before touching those areas:
- **Movement-Phase-Interrupt-Pattern** — chain-of-handlers + game-action for movement hazards (skid, bridge collapse, water, jump landing).
- **Weapon-Attack-Resolution-Pattern** — phase-as-orchestrator + resolver pipeline + optional pre-attack gates (`BuildAttackQueue()`).

### UI interaction model
`MakaMek.Presentation/UiStates` mirrors the game phases: each `IUiState` (DeploymentState, MovementState, WeaponsAttackState, EndState, IdleState) encapsulates that phase's input handling, available actions, and multi-step sub-workflows (e.g. `MovementStep`, `WeaponsAttackStep` enums). This is where phase-specific UX logic belongs.

## Documentation

`docs/INDEX.md` is the single entry point to all docs (architecture, analysis, project, rules, design, archive), each category with its own `INDEX.md`. Game-rule implementations are documented under `docs/rules/`. Load only the specific document you need rather than scanning — see the `navigate-docs` skill. Docs are synced to the GitHub Wiki.

## Assets & Data

Unit/terrain art in `data/` comes from the MegaMek Data Repository (CC BY-NC-SA 4.0), used as-is and distributed separately as downloadable content — do not modify or commit derivatives into the source tree. Mechs are imported from MegaMek's **MTF** format (Level 1 equipment).

## Skills

Repo-local skills in `skills/` (installed to `.agents/skills` via `mise run install-skills`): `sanet-mvvm` (MVVM framework patterns — consult even for small ViewModel/navigation changes), `style-avalonia-app`, `coverage-check`, `generate-unit-tests`, `navigate-docs` (efficient documentation discovery — read before searching docs).

## MCP Tools

Prefer Serena (`serena_initial_instructions`) and Rider tools when available.
