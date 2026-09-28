using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Svg.Skia;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Sanet.MakaMek.Avalonia.Controls;
using Sanet.MakaMek.Avalonia.Controls.Extensions;
using Sanet.MakaMek.Assets.Services;
using Sanet.MakaMek.Core.Models.Units;
using Sanet.MakaMek.Core.Models.Units.Mechs;
using Sanet.MakaMek.Core.Models.Units.Components.Internal;
using Sanet.MakaMek.Presentation.RecordSheet;
using SkiaSharp;

namespace MakaMek.Avalonia.Tests.Controls;

public class ArmourDiagramTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.StartNew(typeof(HeadlessTestSetup));

    /// <summary>
    /// Runs async work on the headless dispatcher. HeadlessUnitTestSession has no Func&lt;Task&gt;
    /// overload, so passing an async lambda straight to Dispatch binds it as an Action - async void -
    /// and every assertion failure inside it is swallowed. Returning a value picks the
    /// Func&lt;Task&lt;T&gt;&gt; overload, which propagates.
    /// </summary>
    private static Task DispatchAsync(Func<Task> action) =>
        Session.Dispatch(async () =>
        {
            await action();
            return true;
        }, CancellationToken.None);

    [Fact]
    public async Task RenderAsync_WithArmourAndStructure_RendersNonblankWholeSheet()
    {
        await DispatchAsync(async () =>
        {
            var assets = Substitute.For<IRecordSheetTemplateProvider>();
            assets.GetTemplateAsync("mek_biped_default.svg")
                .Returns(_ => StreamFor(TemplateSvg));
            assets.GetPipClusterAsync(Arg.Any<string>())
                .Returns(call => StreamFor(ClusterSvg(call.Arg<string>())));
            var control = new ArmourDiagram(assets, new RecordSheetLayout(), NullLogger<ArmourDiagram>.Instance);
            var window = Host(control);

            await control.RenderAsync(RecordSheetSamples.LightMech);
            var scrollViewer = (ScrollViewer)control.Content!;
            var sheetFrame = (Border)scrollViewer.Content!;
            var image = (Image)sheetFrame.Child!;
            scrollViewer.HorizontalScrollBarVisibility.ShouldBe(ScrollBarVisibility.Disabled);
            sheetFrame.Padding.ShouldBe(new Thickness(12));
            image.Width.ShouldBe(876);
            control.Measure(new Size(700, 1200));
            control.Arrange(new Rect(0, 0, 700, 1200));
            image.Width.ShouldBe(676);
            var lightPng = await CaptureAsync(window, control);
            await control.RenderAsync(RecordSheetSamples.AssaultMech);
            var assaultPng = await CaptureAsync(window, control);

            using var bitmap = SKBitmap.Decode(lightPng);
            bitmap.ShouldNotBeNull();
            bitmap!.Width.ShouldBe(900);
            bitmap.Height.ShouldBe(1200);
            bitmap.Pixels.Count(pixel => pixel != SKColors.White).ShouldBeGreaterThan(100);
            assaultPng.SequenceEqual(lightPng).ShouldBeFalse();
            await assets.Received().GetPipClusterAsync("Armor_CT_10_Humanoid.svg");
            await assets.Received().GetPipClusterAsync("Armor_CT_47_Humanoid.svg");
            await assets.Received().GetPipClusterAsync("BipedIS20_CT.svg");
            await assets.Received().GetPipClusterAsync("BipedIS100_CT.svg");
        });
    }

    [Fact]
    public async Task RenderAsync_WithOptionalArtwork_ComposesItWithoutBlockingTheSheet()
    {
        await DispatchAsync(async () =>
        {
            var assets = Substitute.For<IRecordSheetTemplateProvider>();
            assets.GetTemplateAsync("mek_biped_default.svg").Returns(_ => StreamFor(TemplateSvg));
            assets.GetPipClusterAsync(Arg.Any<string>()).Returns(call => StreamFor(ClusterSvg(call.Arg<string>())));
            // The artwork is gated so the baseline is captured before it can arrive; left ungated it
            // can land during the first capture and both renders come back identical.
            var pendingArtwork = new TaskCompletionSource<Stream?>(TaskCreationOptions.RunContinuationsAsynchronously);
            var artwork = Substitute.For<IRecordSheetArtworkProvider>();
            artwork.GetMechArtworkAsync("TestMech TestModel").Returns(_ => pendingArtwork.Task);
            var control = new ArmourDiagram(assets, new RecordSheetLayout(), NullLogger<ArmourDiagram>.Instance, artwork);
            var window = Host(control);
            var data = RecordSheetSamples.LightMech with { FluffArtworkName = "TestMech TestModel" };

            await control.RenderAsync(data);
            var baseline = await CaptureAsync(window, control);

            pendingArtwork.SetResult(PngFor(SKColors.Red));
            await SettleAsync(window);

            var withArtwork = await CaptureAsync(window, control);
            withArtwork.SequenceEqual(baseline).ShouldBeFalse();
            await artwork.Received(1).GetMechArtworkAsync("TestMech TestModel");
        });
    }

    [Fact]
    public async Task RenderAsync_ArmourValueBeyondPipCoverage_GeneratesFallbackAndKeepsDiagramAvailable()
    {
        await DispatchAsync(async () =>
        {
            var assets = Substitute.For<IRecordSheetTemplateProvider>();
            assets.GetTemplateAsync("mek_biped_default.svg").Returns(_ => StreamFor(TemplateSvg));
            assets.GetPipClusterAsync(Arg.Any<string>()).Returns(call =>
                call.Arg<string>() == "Armor_CT_52_Humanoid.svg"
                    ? Task.FromResult<Stream?>(null)
                    : Task.FromResult<Stream?>(StreamFor(ClusterSvg(call.Arg<string>()))));
            var control = new ArmourDiagram(assets, new RecordSheetLayout(), NullLogger<ArmourDiagram>.Instance);
            var availability = new List<bool>();
            control.TemplateAvailabilityChanged += (_, isAvailable) => availability.Add(isAvailable);
            var window = Host(control);

            await control.RenderAsync(RecordSheetDiagramData.Create(20,
                [new KeyValuePair<ArmourRegion, int>(new(PartLocation.CenterTorso, ArmourFace.Front), 52)]));

            availability.ShouldContain(true);
            await assets.Received().GetPipClusterAsync("Armor_CT_52_Humanoid.svg");
        });
    }

    [Fact]
    public async Task RenderAsync_MissingSupportedPip_MarksDiagramUnavailableForTextFallback()
    {
        await DispatchAsync(async () =>
        {
            var assets = Substitute.For<IRecordSheetTemplateProvider>();
            assets.GetTemplateAsync("mek_biped_default.svg").Returns(_ => StreamFor(TemplateSvg));
            var clusterAvailable = true;
            assets.GetPipClusterAsync(Arg.Any<string>()).Returns(call =>
                clusterAvailable
                    ? Task.FromResult<Stream?>(StreamFor(ClusterSvg(call.Arg<string>())))
                    : Task.FromResult<Stream?>(null));
            var control = new ArmourDiagram(assets, new RecordSheetLayout(), NullLogger<ArmourDiagram>.Instance);
            var availability = new List<bool>();
            control.TemplateAvailabilityChanged += (_, isAvailable) => availability.Add(isAvailable);
            var window = Host(control);

            var data = RecordSheetDiagramData.Create(20,
                [new KeyValuePair<ArmourRegion, int>(new(PartLocation.CenterTorso, ArmourFace.Front), 10)]);
            await control.RenderAsync(data);
            clusterAvailable = false;
            await control.RenderAsync(data);

            availability.ShouldBe([true, false]);
        });
    }

    [Fact]
    public async Task RenderAsync_WhenArtworkFetchFails_KeepsTheBaselineSheetAvailable()
    {
        await DispatchAsync(async () =>
        {
            var pendingArtwork = new TaskCompletionSource<Stream?>(TaskCreationOptions.RunContinuationsAsynchronously);
            var assets = Substitute.For<IRecordSheetTemplateProvider>();
            assets.GetTemplateAsync("mek_biped_default.svg").Returns(_ => StreamFor(TemplateSvg));
            assets.GetPipClusterAsync(Arg.Any<string>()).Returns(call => StreamFor(ClusterSvg(call.Arg<string>())));
            var artwork = Substitute.For<IRecordSheetArtworkProvider>();
            artwork.GetMechArtworkAsync("TestMech TestModel").Returns(_ => pendingArtwork.Task);
            var control = new ArmourDiagram(assets, new RecordSheetLayout(), NullLogger<ArmourDiagram>.Instance, artwork);
            var window = Host(control);

            await control.RenderAsync(RecordSheetSamples.LightMech with { FluffArtworkName = "TestMech TestModel" });
            var baseline = await CaptureAsync(window, control);
            using (var bitmap = SKBitmap.Decode(baseline))
                bitmap!.Pixels.Count(pixel => pixel != SKColors.White).ShouldBeGreaterThan(100);

            pendingArtwork.SetException(new IOException("artwork source unavailable"));
            await Task.Delay(50);

            (await CaptureAsync(window, control)).SequenceEqual(baseline).ShouldBeTrue();
            await artwork.Received(1).GetMechArtworkAsync("TestMech TestModel");
        });
    }

    [Fact]
    public async Task ViewModelBinding_WhenClearedDuringArtworkFetch_DoesNotRestoreOldSheet()
    {
        await DispatchAsync(async () =>
        {
            var pendingArtwork = new TaskCompletionSource<Stream?>(TaskCreationOptions.RunContinuationsAsynchronously);
            var assets = Substitute.For<IRecordSheetTemplateProvider>();
            assets.GetTemplateAsync("mek_biped_default.svg").Returns(_ => StreamFor(TemplateSvg));
            assets.GetPipClusterAsync(Arg.Any<string>())
                .Returns(call => StreamFor(ClusterSvg(call.Arg<string>())));
            var artwork = Substitute.For<IRecordSheetArtworkProvider>();
            artwork.GetMechArtworkAsync("TestMech TestModel").Returns(_ => pendingArtwork.Task);
            var viewModel = new RecordSheetViewModel();
            viewModel.SelectUnit(new Mech("TestMech", "TestModel", 20,
                [new CenterTorso("Center Torso", 10, 3, 6)]));
            var control = new ArmourDiagram(assets, new RecordSheetLayout(),
                NullLogger<ArmourDiagram>.Instance, artwork) { ViewModel = viewModel };
            var window = Host(control);
            // The render is queued at Background priority and rasterises on a pool thread; the
            // artwork lookup only starts once it has succeeded.
            await SettleAsync(window, () => artwork.ReceivedCalls().Any());

            await artwork.Received(1).GetMechArtworkAsync("TestMech TestModel");

            viewModel.SelectUnit(null);
            await SettleAsync(window);
            var image = (Image)((Border)((ScrollViewer)control.Content!).Content!).Child!;
            image.Source.ShouldBeNull();

            // The artwork lands after the unit was cleared, and must not bring the old sheet back.
            pendingArtwork.SetResult(PngFor(SKColors.Red));
            await SettleAsync(window);

            image.Source.ShouldBeNull();
        });
    }

    [Fact]
    public async Task Rasterising_AndBitmapConstruction_AreSafeOffTheUiThread()
    {
        // The control composes and rasterises the sheet on a background thread and hands back a
        // Bitmap. Avalonia asserts the UI thread on AvaloniaObject property access, but Bitmap is
        // not one; this pins the assumption the off-thread render rests on.
        await DispatchAsync(async () =>
        {
            var svg = """<svg xmlns="http://www.w3.org/2000/svg" width="8" height="8"><rect width="8" height="8" fill="#123456"/></svg>""";

            var bitmap = await Task.Run(() =>
            {
                using var source = StreamFor(svg);
                using var drawing = new SKSvg();
                drawing.Load(source).ShouldNotBeNull();
                using var png = new MemoryStream();
                drawing.Save(png, SKColors.White, SKEncodedImageFormat.Png, 100, 1f, 1f);
                png.Position = 0;
                return new Bitmap(png);
            });

            bitmap.ShouldNotBeNull();
            bitmap.PixelSize.Width.ShouldBe(8);
            bitmap.Dispose();
        });
    }

    [Fact]
    public async Task ViewModelBinding_AfterDetach_StopsListeningToTheViewModel()
    {
        await DispatchAsync(async () =>
        {
            var templateFetches = 0;
            var assets = Substitute.For<IRecordSheetTemplateProvider>();
            assets.GetTemplateAsync("mek_biped_default.svg").Returns(_ =>
            {
                templateFetches++;
                return StreamFor(TemplateSvg);
            });
            assets.GetPipClusterAsync(Arg.Any<string>())
                .Returns(call => StreamFor(ClusterSvg(call.Arg<string>())));
            var centerTorso = new CenterTorso("Center Torso", 10, 3, 6);
            var unit = new Mech("TestMech", "TestModel", 20, [centerTorso]);
            var viewModel = new RecordSheetViewModel();
            viewModel.SelectUnit(unit);
            var control = new ArmourDiagram(assets, new RecordSheetLayout(),
                NullLogger<ArmourDiagram>.Instance) { ViewModel = viewModel };
            var window = new Window { Width = 900, Height = 1200, Content = control };
            window.Show();
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(50);
            Dispatcher.UIThread.RunJobs();
            templateFetches.ShouldBeGreaterThan(0, "the sheet renders while the control is in the tree");

            // While attached, a damage refresh re-renders.
            var attachedFetches = templateFetches;
            centerTorso.ApplyDamage(2, HitDirection.Front);
            viewModel.Refresh();
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(50);
            Dispatcher.UIThread.RunJobs();
            templateFetches.ShouldBeGreaterThan(attachedFetches, "an attached diagram follows the unit");

            // The view model outlives the view, so a detached control that stayed subscribed would
            // keep re-rendering and keep itself and its bitmap alive.
            window.Content = null;
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            var detachedFetches = templateFetches;

            centerTorso.ApplyDamage(2, HitDirection.Front);
            viewModel.Refresh();
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(50);
            Dispatcher.UIThread.RunJobs();

            templateFetches.ShouldBe(detachedFetches, "a detached diagram must not react to the view model");
        });
    }

    [Fact]
    public async Task ViewModelBinding_WhenUnitIsDamaged_RendersUpdatedValuesWithoutReselection()
    {
        await DispatchAsync(async () =>
        {
            var unit = Substitute.For<IUnit>();
            var centerTorso = new CenterTorso("Center Torso", 10, 3, 6);
            unit.Tonnage.Returns(20);
            unit.Parts.Returns(new Dictionary<PartLocation, UnitPart>
            {
                [PartLocation.CenterTorso] = centerTorso
            });
            var viewModel = new RecordSheetViewModel();
            viewModel.SelectUnit(unit);
            var assets = Substitute.For<IRecordSheetTemplateProvider>();
            assets.GetTemplateAsync("mek_biped_default.svg").Returns(_ => StreamFor(TemplateSvg));
            assets.GetPipClusterAsync(Arg.Any<string>())
                .Returns(call => StreamFor(ClusterSvg(call.Arg<string>())));
            var control = new ArmourDiagram(assets, new RecordSheetLayout(), NullLogger<ArmourDiagram>.Instance)
            {
                ViewModel = viewModel
            };
            var window = Host(control);
            await Task.Delay(50);
            var beforeDamage = await CaptureAsync(window, control);

            centerTorso.ApplyDamage(4, HitDirection.Front);
            viewModel.Refresh();
            await Task.Delay(50);

            var afterDamage = await CaptureAsync(window, control);
            afterDamage.SequenceEqual(beforeDamage).ShouldBeFalse();
            viewModel.Unit.ShouldBeSameAs(unit);
            viewModel.DiagramData!.Armour[new(PartLocation.CenterTorso, ArmourFace.Front)].ShouldBe(6);

            viewModel.AddRecentDamage([PartLocation.CenterTorso]);
            await Task.Delay(50);
            var recentDamage = await CaptureAsync(window, control);
            recentDamage.SequenceEqual(afterDamage).ShouldBeFalse();
            using (var highlightedBitmap = SKBitmap.Decode(recentDamage))
                highlightedBitmap!.Pixels.Count(pixel => pixel.Red > 120 && pixel.Red > pixel.Green)
                    .ShouldBeGreaterThan(0);

            viewModel.ClearRecentDamage();
            await Task.Delay(50);
            (await CaptureAsync(window, control)).SequenceEqual(afterDamage).ShouldBeTrue();
        });
    }

    [Fact]
    public async Task ViewModelBinding_DamageRefresh_DoesNotRenderInsideTheChangeNotification()
    {
        await DispatchAsync(async () =>
        {
            var templateCalls = 0;
            var assets = Substitute.For<IRecordSheetTemplateProvider>();
            assets.GetTemplateAsync("mek_biped_default.svg").Returns(_ =>
            {
                Interlocked.Increment(ref templateCalls);
                return StreamFor(TemplateSvg);
            });
            assets.GetPipClusterAsync(Arg.Any<string>())
                .Returns(call => StreamFor(ClusterSvg(call.Arg<string>())));
            var viewModel = new RecordSheetViewModel();
            var control = new ArmourDiagram(assets, new RecordSheetLayout(), NullLogger<ArmourDiagram>.Instance)
            {
                ViewModel = viewModel
            };
            var window = Host(control);

            viewModel.SelectUnit(CreateUnit(10));
            Volatile.Read(ref templateCalls).ShouldBe(0);
            await Task.Delay(50);
            Volatile.Read(ref templateCalls).ShouldBe(1);
        });
    }

    [Fact]
    public async Task ViewModelBinding_WhenUnitIsSwitchedQuickly_OnlyLatestRenderIsDisplayed()
    {
        await DispatchAsync(async () =>
        {
            var firstTemplate = new TaskCompletionSource<Stream?>(TaskCreationOptions.RunContinuationsAsynchronously);
            var templateCalls = 0;
            var assets = Substitute.For<IRecordSheetTemplateProvider>();
            assets.GetTemplateAsync(Arg.Any<string>()).Returns(_ =>
                Interlocked.Increment(ref templateCalls) == 1
                    ? firstTemplate.Task
                    : Task.FromResult<Stream?>(StreamFor(TemplateSvg)));
            assets.GetPipClusterAsync(Arg.Any<string>())
                .Returns(call => StreamFor(ClusterSvg(call.Arg<string>())));
            var viewModel = new RecordSheetViewModel();
            viewModel.SelectUnit(CreateUnit(10));
            var control = new ArmourDiagram(assets, new RecordSheetLayout(), NullLogger<ArmourDiagram>.Instance)
            {
                ViewModel = viewModel
            };
            var window = Host(control);

            // Wait on the condition rather than on the clock: the render is posted at Background
            // priority and rasterises on a pool thread, so a bare delay proves nothing.
            await SettleAsync(window, () => Volatile.Read(ref templateCalls) == 1);
            Volatile.Read(ref templateCalls).ShouldBe(1);
            viewModel.SelectUnit(CreateUnit(47));
            await SettleAsync(window, () => Volatile.Read(ref templateCalls) == 2);
            var latestRender = await CaptureAsync(window, control);

            // The first template finally arrives, superseded; it must not replace what is shown.
            firstTemplate.SetResult(StreamFor(TemplateSvg));
            await SettleAsync(window);

            (await CaptureAsync(window, control)).SequenceEqual(latestRender).ShouldBeTrue();
            viewModel.DiagramData!.Tonnage.ShouldBe(100);
        });
    }

    [Fact]
    public async Task RenderAsync_DestroyedAndBlownOffLocationsHaveDifferentMarks()
    {
        await DispatchAsync(async () =>
        {
            var destroyedArm = new Arm("Left Arm", PartLocation.LeftArm, 4, 4);
            destroyedArm.ApplyDamage(8, HitDirection.Front);
            var blownOffArm = new Arm("Left Arm", PartLocation.LeftArm, 4, 4);
            blownOffArm.BlowOff();
            var assets = Substitute.For<IRecordSheetTemplateProvider>();
            assets.GetTemplateAsync(Arg.Any<string>()).Returns(_ => StreamFor(TemplateSvg));
            assets.GetPipClusterAsync(Arg.Any<string>())
                .Returns(call => StreamFor(ClusterSvg(call.Arg<string>())));
            var viewModel = new RecordSheetViewModel();
            viewModel.SelectUnit(CreateUnit(destroyedArm));
            var control = new ArmourDiagram(assets, new RecordSheetLayout(), NullLogger<ArmourDiagram>.Instance)
            {
                ViewModel = viewModel
            };
            var window = Host(control);
            await Task.Delay(50);
            var destroyedRender = await CaptureAsync(window, control);

            viewModel.SelectUnit(CreateUnit(blownOffArm));
            await Task.Delay(50);

            (await CaptureAsync(window, control)).SequenceEqual(destroyedRender).ShouldBeFalse();
        });
    }

    [Fact]
    public async Task RenderAsync_CriticalSlotsDistinguishEmptyHitDestroyedAndMissingLocations()
    {
        await DispatchAsync(async () =>
        {
            var centerTorso = new CenterTorso("Center Torso", 10, 3, 6);
            centerTorso.Components.OfType<Gyro>().ShouldHaveSingleItem();
            var unit = new Mech("Test", "Slots", 20, [centerTorso, new Head("Head", 8, 3)]);
            var viewModel = new RecordSheetViewModel();
            viewModel.SelectUnit(unit);
            var assets = Substitute.For<IRecordSheetTemplateProvider>();
            assets.GetTemplateAsync("mek_biped_default.svg").Returns(_ => StreamFor(TemplateSvg));
            assets.GetPipClusterAsync(Arg.Any<string>())
                .Returns(call => StreamFor(ClusterSvg(call.Arg<string>())));
            var control = new ArmourDiagram(assets, new RecordSheetLayout(), NullLogger<ArmourDiagram>.Instance)
            {
                ViewModel = viewModel
            };
            var window = Host(control);
            await Task.Delay(50);
            var intactAndEmptySlots = await CaptureAsync(window, control);

            centerTorso.CriticalHit(3);
            viewModel.Refresh();
            await Task.Delay(50);
            var hitSlot = await CaptureAsync(window, control);
            hitSlot.SequenceEqual(intactAndEmptySlots).ShouldBeFalse();

            centerTorso.CriticalHit(4);
            viewModel.Refresh();
            await Task.Delay(50);
            var destroyedComponent = await CaptureAsync(window, control);
            destroyedComponent.SequenceEqual(hitSlot).ShouldBeFalse();

            var partialMech = new Mech("Test", "Partial", 20,
                [new CenterTorso("Center Torso", 10, 3, 6)]);
            viewModel.SelectUnit(partialMech);
            await Task.Delay(50);
            var missingHead = await CaptureAsync(window, control);
            missingHead.SequenceEqual(intactAndEmptySlots).ShouldBeFalse();
        });
    }

    [Fact]
    public async Task RenderAsync_MissingLocationRegion_ClearsPreviousSheetForFallback()
    {
        await DispatchAsync(async () =>
        {
            var assets = Substitute.For<IRecordSheetTemplateProvider>();
            assets.GetTemplateAsync(Arg.Any<string>()).Returns(_ => StreamFor(TemplateSvg));
            assets.GetPipClusterAsync(Arg.Any<string>()).Returns(call => StreamFor(ClusterSvg(call.Arg<string>())));
            var control = new ArmourDiagram(assets, new RecordSheetLayout(), NullLogger<ArmourDiagram>.Instance);
            var availability = new List<bool>();
            control.TemplateAvailabilityChanged += (_, available) => availability.Add(available);
            await control.RenderAsync(RecordSheetSamples.LightMech);
            assets.GetTemplateAsync(Arg.Any<string>())
                .Returns(_ => StreamFor(TemplateSvg.Replace("armorPipsCT", "missingCT")));

            await control.RenderAsync(RecordSheetSamples.LightMech);

            availability.ShouldBe([true, false]);
            ((Image)((Border)((ScrollViewer)control.Content!).Content!).Child!).Source.ShouldBeNull();
        });
    }

    [Fact]
    public async Task RenderAsync_LateArtworkForSameChassis_PreservesLatestDamageSnapshot()
    {
        await DispatchAsync(async () =>
        {
            var pendingArtwork = new TaskCompletionSource<Stream?>(TaskCreationOptions.RunContinuationsAsynchronously);
            var assets = Substitute.For<IRecordSheetTemplateProvider>();
            assets.GetTemplateAsync(Arg.Any<string>()).Returns(_ => StreamFor(TemplateSvg));
            assets.GetPipClusterAsync(Arg.Any<string>()).Returns(call => StreamFor(ClusterSvg(call.Arg<string>())));
            var artwork = Substitute.For<IRecordSheetArtworkProvider>();
            artwork.GetMechArtworkAsync("Same chassis").Returns(pendingArtwork.Task);
            var control = new ArmourDiagram(assets, new RecordSheetLayout(), NullLogger<ArmourDiagram>.Instance, artwork);
            await control.RenderAsync(RecordSheetSamples.LightMech with { FluffArtworkName = "Same chassis" });
            await control.RenderAsync(RecordSheetSamples.AssaultMech with { FluffArtworkName = "Same chassis" });
            assets.ClearReceivedCalls();

            pendingArtwork.SetResult(PngFor(SKColors.Red));
            await Task.Delay(100);

            await assets.Received().GetPipClusterAsync("Armor_CT_47_Humanoid.svg");
            await assets.DidNotReceive().GetPipClusterAsync("Armor_CT_10_Humanoid.svg");
        });
    }

    [Fact]
    public async Task RenderAsync_StaticSample_LeavesUnpopulatedCriticalTablesBlank()
    {
        await DispatchAsync(async () =>
        {
            var assets = Substitute.For<IRecordSheetTemplateProvider>();
            assets.GetTemplateAsync(Arg.Any<string>()).Returns(_ => StreamFor(TemplateSvg));
            assets.GetPipClusterAsync(Arg.Any<string>()).Returns(call => StreamFor(ClusterSvg(call.Arg<string>())));
            var control = new ArmourDiagram(assets, new RecordSheetLayout(), NullLogger<ArmourDiagram>.Instance);
            var window = Host(control);

            await control.RenderAsync(RecordSheetSamples.LightMech);
            var png = await CaptureAsync(window, control);
            using var bitmap = SKBitmap.Decode(png);
            bitmap!.Pixels.Count(pixel => pixel.Red > 120 && pixel.Red > pixel.Green * 1.5)
                .ShouldBe(0);
        });
    }

    /// <summary>
    /// Hosts a diagram in a window and captures what is actually on screen. A control that is only
    /// measured and arranged never paints its image, so every capture comes back the same and any
    /// "these renders differ" assertion passes without testing anything.
    /// </summary>
    private static Window Host(Control control, int width = 900, int height = 1200)
    {
        var window = new Window { Width = width, Height = height, Content = control };
        window.Show();
        window.UpdateLayout();
        return window;
    }

    /// <summary>
    /// Pumps the dispatcher until queued renders have run. Work that lands after an await - the
    /// artwork lookup, a queued re-render - needs the loop pumped or the capture races it.
    /// </summary>
    private static async Task SettleAsync(Window window, Func<bool>? until = null, int rounds = 40)
    {
        for (var round = 0; round < rounds; round++)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(10);
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            // Rasterisation happens on a background thread, so there is no way to know it has
            // finished without something to test; given a condition, stop as soon as it holds.
            if (until is not null && until()) return;
        }
    }

    /// <summary>
    /// Settles, then captures. Composition and rasterisation run on a background thread, so the
    /// bitmap is not on screen the moment a render is asked for.
    /// </summary>
    private static async Task<byte[]> CaptureAsync(Window window, Control control,
        int width = 900, int height = 1200)
    {
        var image = control is ArmourDiagram diagram
            ? (Image)((Border)((ScrollViewer)diagram.Content!).Content!).Child!
            : null;
        var before = image?.Source;
        await SettleAsync(window, image is null ? null : () => !ReferenceEquals(image.Source, before));
        await SettleAsync(window, rounds: 3);
        return CapturePng(window, control, width, height);
    }

    private static byte[] CapturePng(Window window, Control control, int width = 900, int height = 1200)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        return control.RenderToPngBytes(width, height);
    }

    private static Stream StreamFor(string value) => new MemoryStream(Encoding.UTF8.GetBytes(value));

    private static Stream PngFor(SKColor color)
    {
        using var bitmap = new SKBitmap(8, 8);
        bitmap.Erase(color);
        using var image = SKImage.FromBitmap(bitmap);
        return new MemoryStream(image.Encode(SKEncodedImageFormat.Png, 100).ToArray());
    }

    private static IUnit CreateUnit(int centerTorsoArmor)
    {
        var unit = Substitute.For<IUnit>();
        unit.Tonnage.Returns(centerTorsoArmor == 47 ? 100 : 20);
        unit.Parts.Returns(new Dictionary<PartLocation, UnitPart>
        {
            [PartLocation.CenterTorso] = new CenterTorso("Center Torso", centerTorsoArmor, 3, 6)
        });
        return unit;
    }

    private static IUnit CreateUnit(UnitPart part)
    {
        var unit = Substitute.For<IUnit>();
        unit.Tonnage.Returns(20);
        unit.Parts.Returns(new Dictionary<PartLocation, UnitPart> { [part.Location] = part });
        return unit;
    }

    private static readonly string TemplateSvg = CompleteTemplate("""
        <svg xmlns="http://www.w3.org/2000/svg" width="576" height="756" viewBox="0 0 576 756">
          <rect width="576" height="756" fill="white" stroke="black"/>
          <g id="canonArmorPips"/><g id="canonStructurePips"/>
          <g id="armorPipsCT">
            <rect id="armorCTRow00" x="0" y="0" width="33" height="6"/>
            <rect id="armorCTRow01" x="0" y="5.3" width="33" height="6"/>
            <rect id="armorCTRow02" x="0" y="10.6" width="33" height="6"/>
          </g><text id="textArmor_CT" x="10" y="20"/>
          <g id="armorPipsCTR"/><text id="textArmor_CTR" x="10" y="40"/>
          <g id="armorPipsLA"/><text id="textArmor_LA" x="10" y="80"/>
          <g id="isPipsCT">
            <rect id="isCTRow00" x="0" y="0" width="22" height="5"/>
            <rect id="isCTRow01" x="0" y="4.7" width="22" height="5"/>
            <rect id="isCTRow02" x="0" y="9.4" width="22" height="5"/>
          </g><text id="textIS_CT" x="10" y="60"/>
          <g id="isPipsLA"/><text id="textIS_LA" x="10" y="100"/>
          <rect id="crits_CT" x="120" y="200" width="94.397" height="103.5" fill="none"/>
          <rect id="crits_HD" x="250" y="200" width="94.397" height="50.025" fill="none"/>
          <rect id="fluffSinglePilot" x="350" y="200" width="80" height="100" fill="none"/>
        </svg>
        """);

    private static string CompleteTemplate(string source)
    {
        var document = System.Xml.Linq.XDocument.Parse(source);
        var root = document.Root!;
        var layout = new RecordSheetLayout();
        var ids = Enum.GetValues<PartLocation>().SelectMany(location => new[]
        {
            layout.TemplateRegionId(location, ArmourFace.Front),
            layout.TemplateRegionId(location, ArmourFace.Rear),
            layout.StructureRegionId(location)
        }).OfType<string>();
        foreach (var id in ids)
            if (!root.Descendants().Any(element => (string?)element.Attribute("id") == id))
                root.Add(new System.Xml.Linq.XElement(root.Name.Namespace + "g",
                    new System.Xml.Linq.XAttribute("id", id)));
        return document.ToString();
    }

    private static string ClusterSvg(string name)
    {
        var x = 20 + Math.Abs(name.GetHashCode()) % 500;
        return $"""
            <svg xmlns="http://www.w3.org/2000/svg" width="576" height="756" viewBox="0 0 576 756">
              <switch><g><path d="M{x} 80h40v40h-40z" fill="none" stroke="#000000" stroke-width="0.5"/></g></switch>
            </svg>
            """;
    }
}
