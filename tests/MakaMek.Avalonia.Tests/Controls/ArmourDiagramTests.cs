using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
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
        HeadlessUnitTestSession.StartNew(typeof(TestApp));

    [Fact]
    public async Task RenderAsync_WithArmourAndStructure_RendersNonblankWholeSheet()
    {
        await Session.Dispatch(async () =>
        {
            var assets = Substitute.For<IRecordSheetTemplateProvider>();
            assets.GetTemplateAsync("mek_biped_default.svg")
                .Returns(_ => StreamFor(TemplateSvg));
            assets.GetPipClusterAsync(Arg.Any<string>())
                .Returns(call => StreamFor(ClusterSvg(call.Arg<string>())));
            var control = new ArmourDiagram(assets, new RecordSheetLayout(), NullLogger<ArmourDiagram>.Instance);
            control.Measure(new Size(900, 1200));
            control.Arrange(new Rect(0, 0, 900, 1200));

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
            var lightPng = control.RenderToPngBytes(900, 1200);
            await control.RenderAsync(RecordSheetSamples.AssaultMech);
            var assaultPng = control.RenderToPngBytes(900, 1200);

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
        }, CancellationToken.None);
    }

    [Fact]
    public async Task RenderAsync_WithOptionalArtwork_ComposesItWithoutBlockingTheSheet()
    {
        await Session.Dispatch(async () =>
        {
            var assets = Substitute.For<IRecordSheetTemplateProvider>();
            assets.GetTemplateAsync("mek_biped_default.svg").Returns(_ => StreamFor(TemplateSvg));
            assets.GetPipClusterAsync(Arg.Any<string>()).Returns(call => StreamFor(ClusterSvg(call.Arg<string>())));
            var artwork = Substitute.For<IRecordSheetArtworkProvider>();
            artwork.GetMechArtworkAsync("TestMech TestModel").Returns(_ => Task.FromResult<Stream?>(PngFor(SKColors.Red)));
            var control = new ArmourDiagram(assets, new RecordSheetLayout(), NullLogger<ArmourDiagram>.Instance, artwork);
            control.Measure(new Size(900, 1200));
            control.Arrange(new Rect(0, 0, 900, 1200));
            var data = RecordSheetSamples.LightMech with { FluffArtworkName = "TestMech TestModel" };

            await control.RenderAsync(data);
            var baseline = control.RenderToPngBytes(900, 1200);
            await Task.Delay(100);

            var withArtwork = control.RenderToPngBytes(900, 1200);
            withArtwork.SequenceEqual(baseline).ShouldBeFalse();
            await artwork.Received(1).GetMechArtworkAsync("TestMech TestModel");
        }, CancellationToken.None);
    }

    [Fact]
    public async Task RenderAsync_ArmourValueBeyondPipCoverage_GeneratesFallbackAndKeepsDiagramAvailable()
    {
        await Session.Dispatch(async () =>
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
            control.Measure(new Size(900, 1200));
            control.Arrange(new Rect(0, 0, 900, 1200));

            await control.RenderAsync(RecordSheetDiagramData.Create(20,
                [new KeyValuePair<ArmourRegion, int>(new(PartLocation.CenterTorso, ArmourFace.Front), 52)]));

            availability.ShouldContain(true);
            await assets.Received().GetPipClusterAsync("Armor_CT_52_Humanoid.svg");
        }, CancellationToken.None);
    }

    [Fact]
    public async Task RenderAsync_MissingSupportedPip_MarksDiagramUnavailableForTextFallback()
    {
        await Session.Dispatch(async () =>
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
            control.Measure(new Size(900, 1200));
            control.Arrange(new Rect(0, 0, 900, 1200));

            var data = RecordSheetDiagramData.Create(20,
                [new KeyValuePair<ArmourRegion, int>(new(PartLocation.CenterTorso, ArmourFace.Front), 10)]);
            await control.RenderAsync(data);
            clusterAvailable = false;
            await control.RenderAsync(data);

            availability.ShouldBe([true, false]);
        }, CancellationToken.None);
    }

    [Fact]
    public async Task RenderAsync_WhenArtworkFetchFails_KeepsTheBaselineSheetAvailable()
    {
        await Session.Dispatch(async () =>
        {
            var pendingArtwork = new TaskCompletionSource<Stream?>(TaskCreationOptions.RunContinuationsAsynchronously);
            var assets = Substitute.For<IRecordSheetTemplateProvider>();
            assets.GetTemplateAsync("mek_biped_default.svg").Returns(_ => StreamFor(TemplateSvg));
            assets.GetPipClusterAsync(Arg.Any<string>()).Returns(call => StreamFor(ClusterSvg(call.Arg<string>())));
            var artwork = Substitute.For<IRecordSheetArtworkProvider>();
            artwork.GetMechArtworkAsync("TestMech TestModel").Returns(_ => pendingArtwork.Task);
            var control = new ArmourDiagram(assets, new RecordSheetLayout(), NullLogger<ArmourDiagram>.Instance, artwork);
            control.Measure(new Size(900, 1200));
            control.Arrange(new Rect(0, 0, 900, 1200));

            await control.RenderAsync(RecordSheetSamples.LightMech with { FluffArtworkName = "TestMech TestModel" });
            var baseline = control.RenderToPngBytes(900, 1200);
            using (var bitmap = SKBitmap.Decode(baseline))
                bitmap!.Pixels.Count(pixel => pixel != SKColors.White).ShouldBeGreaterThan(100);

            pendingArtwork.SetException(new IOException("artwork source unavailable"));
            await Task.Delay(50);

            control.RenderToPngBytes(900, 1200).SequenceEqual(baseline).ShouldBeTrue();
            await artwork.Received(1).GetMechArtworkAsync("TestMech TestModel");
        }, CancellationToken.None);
    }

    [Fact]
    public async Task ViewModelBinding_WhenClearedDuringArtworkFetch_DoesNotRestoreOldSheet()
    {
        await Session.Dispatch(async () =>
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
            control.Measure(new Size(900, 1200));
            control.Arrange(new Rect(0, 0, 900, 1200));

            _ = artwork.Received(1).GetMechArtworkAsync("TestMech TestModel");
            viewModel.SelectUnit(null);
            var image = (Image)((Border)((ScrollViewer)control.Content!).Content!).Child!;
            image.Source.ShouldBeNull();

            pendingArtwork.SetResult(PngFor(SKColors.Red));
            await Task.Delay(50);

            image.Source.ShouldBeNull();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task ViewModelBinding_WhenUnitIsDamaged_RendersUpdatedValuesWithoutReselection()
    {
        await Session.Dispatch(async () =>
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
            control.Measure(new Size(900, 1200));
            control.Arrange(new Rect(0, 0, 900, 1200));
            await Task.Delay(50);
            var beforeDamage = control.RenderToPngBytes(900, 1200);

            centerTorso.ApplyDamage(4, HitDirection.Front);
            viewModel.Refresh();
            await Task.Delay(50);

            var afterDamage = control.RenderToPngBytes(900, 1200);
            afterDamage.SequenceEqual(beforeDamage).ShouldBeFalse();
            viewModel.Unit.ShouldBeSameAs(unit);
            viewModel.DiagramData!.Armour[new(PartLocation.CenterTorso, ArmourFace.Front)].ShouldBe(6);

            viewModel.AddRecentDamage([PartLocation.CenterTorso]);
            await Task.Delay(50);
            var recentDamage = control.RenderToPngBytes(900, 1200);
            recentDamage.SequenceEqual(afterDamage).ShouldBeFalse();
            using (var highlightedBitmap = SKBitmap.Decode(recentDamage))
                highlightedBitmap!.Pixels.Count(pixel => pixel.Red > 120 && pixel.Red > pixel.Green)
                    .ShouldBeGreaterThan(0);

            viewModel.ClearRecentDamage();
            await Task.Delay(50);
            control.RenderToPngBytes(900, 1200).SequenceEqual(afterDamage).ShouldBeTrue();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task ViewModelBinding_DamageRefresh_DoesNotRenderInsideTheChangeNotification()
    {
        await Session.Dispatch(async () =>
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
            control.Measure(new Size(900, 1200));
            control.Arrange(new Rect(0, 0, 900, 1200));

            viewModel.SelectUnit(CreateUnit(10));
            Volatile.Read(ref templateCalls).ShouldBe(0);
            await Task.Delay(50);
            Volatile.Read(ref templateCalls).ShouldBe(1);
        }, CancellationToken.None);
    }

    [Fact]
    public async Task ViewModelBinding_WhenUnitIsSwitchedQuickly_OnlyLatestRenderIsDisplayed()
    {
        await Session.Dispatch(async () =>
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
            control.Measure(new Size(900, 1200));
            control.Arrange(new Rect(0, 0, 900, 1200));

            await Task.Delay(20);
            Volatile.Read(ref templateCalls).ShouldBe(1);
            viewModel.SelectUnit(CreateUnit(47));
            await Task.Delay(50);
            var latestRender = control.RenderToPngBytes(900, 1200);
            firstTemplate.SetResult(StreamFor(TemplateSvg));
            await Task.Delay(50);

            control.RenderToPngBytes(900, 1200).SequenceEqual(latestRender).ShouldBeTrue();
            viewModel.DiagramData!.Tonnage.ShouldBe(100);
        }, CancellationToken.None);
    }

    [Fact]
    public async Task RenderAsync_DestroyedAndBlownOffLocationsHaveDifferentMarks()
    {
        await Session.Dispatch(async () =>
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
            control.Measure(new Size(900, 1200));
            control.Arrange(new Rect(0, 0, 900, 1200));
            await Task.Delay(50);
            var destroyedRender = control.RenderToPngBytes(900, 1200);

            viewModel.SelectUnit(CreateUnit(blownOffArm));
            await Task.Delay(50);

            control.RenderToPngBytes(900, 1200).SequenceEqual(destroyedRender).ShouldBeFalse();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task RenderAsync_CriticalSlotsDistinguishEmptyHitDestroyedAndMissingLocations()
    {
        await Session.Dispatch(async () =>
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
            control.Measure(new Size(900, 1200));
            control.Arrange(new Rect(0, 0, 900, 1200));
            await Task.Delay(50);
            var intactAndEmptySlots = control.RenderToPngBytes(900, 1200);

            centerTorso.CriticalHit(3);
            viewModel.Refresh();
            await Task.Delay(50);
            var hitSlot = control.RenderToPngBytes(900, 1200);
            hitSlot.SequenceEqual(intactAndEmptySlots).ShouldBeFalse();

            centerTorso.CriticalHit(4);
            viewModel.Refresh();
            await Task.Delay(50);
            var destroyedComponent = control.RenderToPngBytes(900, 1200);
            destroyedComponent.SequenceEqual(hitSlot).ShouldBeFalse();

            var partialMech = new Mech("Test", "Partial", 20,
                [new CenterTorso("Center Torso", 10, 3, 6)]);
            viewModel.SelectUnit(partialMech);
            await Task.Delay(50);
            var missingHead = control.RenderToPngBytes(900, 1200);
            missingHead.SequenceEqual(intactAndEmptySlots).ShouldBeFalse();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task RenderAsync_MissingLocationRegion_ClearsPreviousSheetForFallback()
    {
        await Session.Dispatch(async () =>
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
        }, CancellationToken.None);
    }

    [Fact]
    public async Task RenderAsync_LateArtworkForSameChassis_PreservesLatestDamageSnapshot()
    {
        await Session.Dispatch(async () =>
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
        }, CancellationToken.None);
    }

    [Fact]
    public async Task RenderAsync_StaticSample_LeavesUnpopulatedCriticalTablesBlank()
    {
        await Session.Dispatch(async () =>
        {
            var assets = Substitute.For<IRecordSheetTemplateProvider>();
            assets.GetTemplateAsync(Arg.Any<string>()).Returns(_ => StreamFor(TemplateSvg));
            assets.GetPipClusterAsync(Arg.Any<string>()).Returns(call => StreamFor(ClusterSvg(call.Arg<string>())));
            var control = new ArmourDiagram(assets, new RecordSheetLayout(), NullLogger<ArmourDiagram>.Instance);

            await control.RenderAsync(RecordSheetSamples.LightMech);
            var png = control.RenderToPngBytes(900, 1200);
            using var bitmap = SKBitmap.Decode(png);
            bitmap!.Pixels.Count(pixel => pixel.Red > 120 && pixel.Red > pixel.Green * 1.5)
                .ShouldBe(0);
        }, CancellationToken.None);
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
