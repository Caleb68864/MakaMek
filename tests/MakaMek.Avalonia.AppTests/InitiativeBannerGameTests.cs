using global::Avalonia;
using global::Avalonia.Headless;
using global::Avalonia.Controls;
using global::Avalonia.Threading;
using global::Avalonia.VisualTree;
using Sanet.MakaMek.Avalonia.Controls.TemplatedControls;
using Sanet.MakaMek.Avalonia.Views;
using Microsoft.Extensions.DependencyInjection;
using Sanet.MakaMek.Avalonia;
using Sanet.MakaMek.Bots.Models;
using Sanet.MakaMek.Core.Data.Game.Commands.Client;
using Sanet.MakaMek.Core.Data.Game.Commands.Server;
using Sanet.MakaMek.Core.Data.Units;
using Sanet.MakaMek.Core.Models.Game;
using Sanet.MakaMek.Core.Models.Game.Factories;
using Sanet.MakaMek.Core.Models.Game.Phases;
using Sanet.MakaMek.Bots.Services;
using Sanet.MakaMek.Core.Models.Game.Players;
using Sanet.MakaMek.Core.Services.Transport;
using Sanet.MakaMek.Map.Factories;
using Sanet.MakaMek.Map.Generators;
using Sanet.MakaMek.Map.Models.Terrains;
using System.Reactive.Concurrency;
using Sanet.MakaMek.Presentation.ViewModels;
using Sanet.MakaMek.Presentation.ViewModels.Wrappers;
using Shouldly;

namespace MakaMek.Avalonia.AppTests;

/// <summary>
/// Runs an actual local game of two bots far enough to reach the initiative phase, and checks what
/// the battle map view model announces.
///
/// This is the test that would have caught the defect the maintainer found by playing: the winner
/// banner was queued before the phase it reports on. Every unit test of that feature passed while it
/// was broken, because they hand-built the command sequence instead of letting a real game produce it.
/// </summary>
public class InitiativeBannerGameTests
{
    [Fact]
    public Task ARealLocalGame_ReachesInitiative_AndAnnouncesTheWinnerAfterThePhase() => HarnessSession.Run(async () =>
    {
        var services = ((App)Application.Current!).ServiceProvider!;
        var gameManager = services.GetRequiredService<IGameManager>();
        var gameFactory = services.GetRequiredService<IGameFactory>();
        var publisher = services.GetRequiredService<ICommandPublisher>();
        var botManager = services.GetRequiredService<IBotManager>();
        var mapFactory = services.GetRequiredService<IBattleMapFactory>();

        (await gameManager.InitializeLocalLobby()).ShouldBeTrue("the local lobby must start");
        var game = gameFactory.CreateClientGame(publisher, gameManager.ServerGameId);
        botManager.Initialize(game, new DecisionEngineProvider(game));

        var unit = (await LocalGameFixture.LoadBundledUnitsAsync(services))[0];
        await JoinBot(game, botManager, publisher, unit, "Alpha", "#FF0000");
        await JoinBot(game, botManager, publisher, unit, "Bravo", "#0000FF");
        await PumpUntil(() => game.Players.Count == 2 && game.Players.All(p => p.Status == PlayerStatus.Ready));
        game.Players.Count.ShouldBe(2, "both bots should have joined before the map is set");

        gameManager.SetBattleMap(mapFactory.GenerateMap(
            15, 17, new SingleTerrainGenerator(15, 17, new ClearTerrain())));
        gameManager.TryStartGame();

        // Attach the battle map view model before the game moves, so it sees the announcements a
        // player would. Resolved from the real container, not constructed with stand-ins.
        // Probe: is the scheduler the view model will subscribe through actually alive on this
        // dispatcher? A stale static would deliver nothing and look like a silent game.
        var schedulerAlive = false;
        services.GetRequiredService<Sanet.MakaMek.Services.IDispatcherService>()
            .Scheduler.Schedule(() => schedulerAlive = true);
        await PumpUntil(() => schedulerAlive, 50);

        // Built with a per test dispatcher rather than resolved from DI, so the command
        // subscription does not go through the process wide AvaloniaScheduler static.
        using var dispatcher = new TestDispatcherService();
        var viewModel = ActivatorUtilities.CreateInstance<BattleMapViewModel>(services, dispatcher);
        viewModel.Game = game;

        // Probe: record every command the client actually receives, and the phase it was in.
        var seen = new List<string>();
        var rolls = new List<string>();
        game.Commands.Subscribe(c =>
        {
            seen.Add(c.GetType().Name + "@" + game.TurnPhase);
            if (c is DiceRolledCommand d)
                rolls.Add(d.PlayerId + " rolled " + d.Roll +
                          " known=" + game.Players.Any(pl => pl.Id == d.PlayerId));
        });

        await PumpUntil(() => game.TurnPhase is PhaseNames.Initiative or PhaseNames.Movement);
        // Report the phase itself so a stall says where, not just that it happened.
        game.TurnPhase.ShouldBeOneOf(PhaseNames.Initiative, PhaseNames.Movement);

        // Wait on the signal itself, with a budget generous enough that a slow run is not a
        // failure. A fixed round count that happens to be enough most of the time is how a suite
        // earns a reputation for flakiness, which is worse than having no test at all.
        await PumpUntil(() => viewModel.TurnNotifications.Any(
            n => n.Kind == TurnNotificationKind.Initiative));

        var kinds = viewModel.TurnNotifications.Select(n => n.Kind).ToList();
        var announced = string.Join(" -> ", viewModel.TurnNotifications.Select(n => $"{n.Kind}:{n.Text}"));

        var diagnosis = string.Join("\n", new[]
        {
            $"Queue: {announced}",
            $"Players={game.Players.Count} AlivePlayers={game.AlivePlayers.Count}",
            "Units per player: " + string.Join(", ", game.Players.Select(pl => pl.Name + ":" + pl.Units.Count + " alive " + pl.AliveUnits.Count)),
            "SchedulerAlive=" + schedulerAlive,
            "Rolls: " + string.Join(" | ", rolls),
            $"Commands: {string.Join(", ", seen)}"
        });

        kinds.ShouldContain(TurnNotificationKind.Initiative,
            "a real game produced no initiative announcement at all.\n" + diagnosis);
        kinds.IndexOf(TurnNotificationKind.Initiative)
            .ShouldBeGreaterThan(kinds.IndexOf(TurnNotificationKind.Phase),
                "the winner must be announced after the phase it reports on.\n" + diagnosis);
    });


    [Fact]
    public Task TheAnnouncementNamesTheHighestRoller_AndTheirRoll() => HarnessSession.Run(async () =>
    {
        var (game, viewModel, rolls) = await StartTwoBotGame();
        await PumpUntil(() => viewModel.TurnNotifications.Any(n => n.Kind == TurnNotificationKind.Initiative));

        var announcement = viewModel.TurnNotifications
            .First(n => n.Kind == TurnNotificationKind.Initiative);
        var best = rolls.OrderByDescending(r => r.Roll).First();
        var winner = game.Players.First(p => p.Id == best.PlayerId);

        announcement.Text.ShouldContain(winner.Name.ToUpperInvariant(),
            Case.Insensitive, "the banner must name the player who actually won");
        announcement.Text.ShouldContain(best.Roll.ToString(),
            Case.Insensitive, "and the roll it was won with");
        announcement.Tint.ShouldBe(winner.Tint, "banners are tinted with the player's colour");
    });

    [Fact]
    public Task TheAnnouncementIsNotARawLocalizationKey() => HarnessSession.Run(async () =>
    {
        var (_, viewModel, _) = await StartTwoBotGame();
        await PumpUntil(() => viewModel.TurnNotifications.Any(n => n.Kind == TurnNotificationKind.Initiative));

        var text = viewModel.TurnNotifications
            .First(n => n.Kind == TurnNotificationKind.Initiative).Text;

        // A missing key renders as the key itself, which reaches players as gibberish. That exact
        // defect shipped once already and is what PR #1490 fixes.
        text.ShouldNotContain("BattleMap_Notification", Case.Insensitive);
        text.ShouldNotContain("_", Case.Insensitive, "a localization key leaked into the banner: " + text);
    });

    [Fact]
    public Task TheWinnerBannerIsActuallyDisplayed_InTheRealView() => HarnessSession.Run(async () =>
    {
        var (_, viewModel, _) = await StartTwoBotGame();

        var view = new BattleMapView { DataContext = viewModel };
        var window = new Window { Width = 1100, Height = 700, Content = view };
        window.Show();
        Pump(window);

        var banner = view.GetVisualDescendants().OfType<TurnNotificationBanner>().FirstOrDefault();
        banner.ShouldNotBeNull();

        // Record what the control puts on screen, not what the queue holds.
        var displayed = new List<string>();
        for (var i = 0; i < 1500; i++)
        {
            Pump(window);
            if (!string.IsNullOrEmpty(banner.CurrentText) &&
                (displayed.Count == 0 || displayed[^1] != banner.CurrentText))
                displayed.Add(banner.CurrentText);
            if (displayed.Any(text => text.Contains("INITIATIVE", StringComparison.OrdinalIgnoreCase)
                                      && text.Contains("WINS", StringComparison.OrdinalIgnoreCase)))
                break;
            await Task.Delay(10);
        }

        window.Close();
        displayed.ShouldContain(text => text.Contains("WINS", StringComparison.OrdinalIgnoreCase),
            "the winner banner never reached the screen. Displayed: " + string.Join(" -> ", displayed));
    });

    /// <summary>Starts a two bot local game and returns once it has left deployment.</summary>
    private sealed record RunningGame(
        IClientGame Game, BattleMapViewModel ViewModel, List<(Guid PlayerId, int Roll)> Rolls);

    private static async Task<RunningGame> StartTwoBotGame()
    {
        var services = ((App)Application.Current!).ServiceProvider!;
        var gameManager = services.GetRequiredService<IGameManager>();
        var gameFactory = services.GetRequiredService<IGameFactory>();
        var publisher = services.GetRequiredService<ICommandPublisher>();
        var botManager = services.GetRequiredService<IBotManager>();
        var mapFactory = services.GetRequiredService<IBattleMapFactory>();

        await gameManager.InitializeLocalLobby();
        var game = gameFactory.CreateClientGame(publisher, gameManager.ServerGameId);
        botManager.Initialize(game, new DecisionEngineProvider(game));

        var rolls = new List<(Guid PlayerId, int Roll)>();
        game.Commands.Subscribe(c =>
        {
            if (c is DiceRolledCommand d) rolls.Add((d.PlayerId, d.Roll));
        });

        var dispatcher = new TestDispatcherService();
        var viewModel = ActivatorUtilities.CreateInstance<BattleMapViewModel>(services, dispatcher);

        var unit = (await LocalGameFixture.LoadBundledUnitsAsync(services))[0];
        await JoinBot(game, botManager, publisher, unit, "Alpha", "#FF0000");
        await JoinBot(game, botManager, publisher, unit, "Bravo", "#0000FF");
        await PumpUntil(() => game.Players.Count == 2 && game.Players.All(p => p.Status == PlayerStatus.Ready));

        viewModel.Game = game;
        gameManager.SetBattleMap(mapFactory.GenerateMap(
            15, 17, new SingleTerrainGenerator(15, 17, new ClearTerrain())));
        gameManager.TryStartGame();
        await PumpUntil(() => game.TurnPhase is PhaseNames.Initiative or PhaseNames.Movement);
        return new RunningGame(game, viewModel, rolls);
    }

    private static void Pump(Window window)
    {
        Dispatcher.UIThread.RunJobs(DispatcherPriority.SystemIdle);
        window.UpdateLayout();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs(DispatcherPriority.SystemIdle);
    }

    private static async Task JoinBot(
        IClientGame game, IBotManager botManager, ICommandPublisher publisher,
        UnitData unit, string name, string tint)
    {
        var player = new Player(Guid.NewGuid(), name, PlayerControlType.Bot, tint);
        var unitId = Guid.NewGuid();
        botManager.AddBot(player);
        await game.JoinGameWithUnits(player, [unit with { Id = unitId }], [
            new PilotAssignmentData
            {
                UnitId = unitId,
                PilotData = PilotData.CreateDefaultPilot("MechWarrior", name)
            }
        ]);
        // The server has to have seen the join before the status means anything.
        await PumpUntil(() => game.Players.Any(p => p.Id == player.Id));
        publisher.PublishCommand(new UpdatePlayerStatusCommand
        {
            GameOriginId = game.Id,
            PlayerId = player.Id,
            PlayerStatus = PlayerStatus.Ready
        });
    }

    /// <summary>Pumps the dispatcher until the condition holds, or gives up.</summary>
    private static async Task<bool> PumpUntil(Func<bool> condition, int rounds = 1500)
    {
        for (var i = 0; i < rounds; i++)
        {
            if (condition()) return true;
            // Drain every priority: AvaloniaScheduler posts below the RunJobs default,
            // so the default drain leaves scheduled work sitting in the queue.
            Dispatcher.UIThread.RunJobs(DispatcherPriority.SystemIdle);
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            await Task.Delay(10);
        }

        return condition();
    }

}
