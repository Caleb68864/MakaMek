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

    private static Stream StreamFor(string value) => new MemoryStream(Encoding.UTF8.GetBytes(value));

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

    private const string TemplateSvg = """
        <svg xmlns="http://www.w3.org/2000/svg" width="576" height="756" viewBox="0 0 576 756">
          <rect width="576" height="756" fill="white" stroke="black"/>
          <g id="canonArmorPips"/><g id="canonStructurePips"/>
          <g id="armorPipsCT"/><text id="textArmor_CT" x="10" y="20"/>
          <g id="armorPipsCTR"/><text id="textArmor_CTR" x="10" y="40"/>
          <g id="armorPipsLA"/><text id="textArmor_LA" x="10" y="80"/>
          <g id="isPipsCT"/><text id="textIS_CT" x="10" y="60"/>
          <g id="isPipsLA"/><text id="textIS_LA" x="10" y="100"/>
        </svg>
        """;

    private static string ClusterSvg(string name)
    {
        var x = 20 + Math.Abs(name.GetHashCode()) % 500;
        return $"""
            <svg xmlns="http://www.w3.org/2000/svg" width="576" height="756" viewBox="0 0 576 756">
              <switch><g><path d="M{x} 80h40v40h-40z" fill="black"/></g></switch>
            </svg>
            """;
    }
}
