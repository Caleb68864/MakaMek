using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.VisualTree;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Sanet.MakaMek.Avalonia.Controls;
using Sanet.MakaMek.Assets.Services;
using Sanet.MakaMek.Core.Models.Units;
using Sanet.MakaMek.Core.Models.Units.Mechs;
using Sanet.MakaMek.Presentation.RecordSheet;
using Shouldly;

namespace MakaMek.Avalonia.Tests.Controls;

public class UnitRecordSheetTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.StartNew(typeof(TestApp));

    [Fact]
    public async Task Mech_WithAvailableTemplate_ShowsRecordSheetTab()
    {
        await Session.Dispatch(async () =>
        {
            var assets = CreateAssets(templateAvailable: true);
            var control = CreateControl(assets);
            control.Unit = CreateMech();
            control.Measure(new Size(400, 800));
            control.Arrange(new Rect(0, 0, 400, 800));

            await WaitForAsync(() => control.FindControl<TabItem>("RecordSheetTab")!.IsVisible);

            await assets.Received(1).GetTemplateAsync("mek_biped_default.svg");
            control.FindControl<TabItem>("RecordSheetTab")!.IsVisible.ShouldBeTrue();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task NonMech_UsesTextTabs_WithoutRequestingDiagramAssets()
    {
        await Session.Dispatch(async () =>
        {
            var assets = CreateAssets(templateAvailable: true);
            var control = CreateControl(assets);
            control.Unit = Substitute.For<Unit>("Test", "Vehicle", 50, Array.Empty<UnitPart>());
            control.Measure(new Size(400, 800));
            control.Arrange(new Rect(0, 0, 400, 800));
            await Task.Delay(50);

            control.FindControl<TabItem>("RecordSheetTab")!.IsVisible.ShouldBeFalse();
            await assets.DidNotReceive().GetTemplateAsync(Arg.Any<string>());
            await assets.DidNotReceive().GetPipClusterAsync(Arg.Any<string>());
            control.FindControl<TabControl>("RecordSheetTabs")!.Items
                .OfType<TabItem>().Count(tab => tab.IsVisible).ShouldBe(4);
        }, CancellationToken.None);
    }

    [Fact]
    public async Task MissingTemplate_LeavesExistingTextTabsUnchanged()
    {
        await Session.Dispatch(async () =>
        {
            var assets = CreateAssets(templateAvailable: false);
            var control = CreateControl(assets);
            control.Unit = CreateMech();
            control.Measure(new Size(400, 800));
            control.Arrange(new Rect(0, 0, 400, 800));
            await Task.Delay(50);

            control.FindControl<TabItem>("RecordSheetTab")!.IsVisible.ShouldBeFalse();
            control.FindControl<TabControl>("RecordSheetTabs")!.Items
                .OfType<TabItem>().Count(tab => tab.IsVisible).ShouldBe(4);
            await assets.Received(1).GetTemplateAsync("mek_biped_default.svg");
            await assets.DidNotReceive().GetPipClusterAsync(Arg.Any<string>());
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Mech_AtPhoneWidth_FitsDiagramWithoutHorizontalScrolling()
    {
        await Session.Dispatch(async () =>
        {
            var control = CreateControl(CreateAssets(templateAvailable: true));
            control.Unit = CreateMech();
            control.Measure(new Size(320, 640));
            control.Arrange(new Rect(0, 0, 320, 640));
            var recordSheetTab = control.FindControl<TabItem>("RecordSheetTab")!;

            await WaitForAsync(() => recordSheetTab.IsVisible);
            control.FindControl<TabControl>("RecordSheetTabs")!.SelectedItem = recordSheetTab;
            control.Measure(new Size(320, 640));
            control.Arrange(new Rect(0, 0, 320, 640));

            var diagram = control.GetVisualDescendants().OfType<ArmourDiagram>().Single();
            var scrollViewer = (ScrollViewer)diagram.Content!;
            var frame = (Border)scrollViewer.Content!;
            var image = (Image)frame.Child!;
            scrollViewer.HorizontalScrollBarVisibility.ShouldBe(ScrollBarVisibility.Disabled);
            diagram.Bounds.Width.ShouldBe(320);
            image.Width.ShouldBe(296);
        }, CancellationToken.None);
    }

    [Fact]
    public async Task SelectedDiagram_WhenUnitBecomesUnavailable_ReturnsToTextTab()
    {
        await Session.Dispatch(async () =>
        {
            var control = CreateControl(CreateAssets(templateAvailable: true));
            control.Unit = CreateMech();
            var tab = control.FindControl<TabItem>("RecordSheetTab")!;
            var tabs = control.FindControl<TabControl>("RecordSheetTabs")!;
            await WaitForAsync(() => tab.IsVisible);
            tabs.SelectedItem = tab;

            control.Unit = null;

            tab.IsVisible.ShouldBeFalse();
            tabs.SelectedIndex.ShouldBe(0);
        }, CancellationToken.None);
    }

    [Fact]
    public async Task ExternalSnapshotForAnotherUnit_HidesOldDiagramUntilMatchingSnapshotArrives()
    {
        await Session.Dispatch(async () =>
        {
            var control = CreateControl(CreateAssets(templateAvailable: true));
            var first = CreateMech();
            var second = CreateMech();
            var snapshot = new RecordSheetViewModel();
            snapshot.SelectUnit(first);
            control.DiagramViewModel = snapshot;
            control.Unit = first;
            var tab = control.FindControl<TabItem>("RecordSheetTab")!;
            await WaitForAsync(() => tab.IsVisible);

            control.Unit = second;
            tab.IsVisible.ShouldBeFalse();
            snapshot.SelectUnit(second);
            await WaitForAsync(() => tab.IsVisible);
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Preview_SelectsAvailableDiagram_AndFallsBackToTextWhenItFails()
    {
        await Session.Dispatch(async () =>
        {
            var assets = CreateAssets(templateAvailable: true);
            var control = CreateControl(assets);
            control.PreferDiagram = true;
            var snapshot = new RecordSheetViewModel();
            var unit = CreateMech();
            snapshot.SelectUnit(unit);
            control.DiagramViewModel = snapshot;
            control.Unit = unit;
            var tab = control.FindControl<TabItem>("RecordSheetTab")!;
            var tabs = control.FindControl<TabControl>("RecordSheetTabs")!;
            await WaitForAsync(() => tab.IsVisible);
            tabs.SelectedItem.ShouldBeSameAs(tab);

            assets.GetTemplateAsync(Arg.Any<string>()).Returns(Task.FromResult<Stream?>(null));
            snapshot.Refresh();
            await WaitForAsync(() => !tab.IsVisible);
            tabs.SelectedIndex.ShouldBe(0);
        }, CancellationToken.None);
    }

    [Fact]
    public async Task SwitchingUnits_ImmediatelyHidesPreviousSheetWhileNextTemplateLoads()
    {
        await Session.Dispatch(async () =>
        {
            var assets = CreateAssets(templateAvailable: true);
            var control = CreateControl(assets);
            control.Unit = CreateMech();
            var tab = control.FindControl<TabItem>("RecordSheetTab")!;
            await WaitForAsync(() => tab.IsVisible);
            var pending = new TaskCompletionSource<Stream?>(TaskCreationOptions.RunContinuationsAsynchronously);
            assets.GetTemplateAsync(Arg.Any<string>()).Returns(pending.Task);

            control.Unit = CreateMech();

            tab.IsVisible.ShouldBeFalse();
            pending.SetResult(StreamFor(TemplateSvg));
            await WaitForAsync(() => tab.IsVisible);
        }, CancellationToken.None);
    }

    private static UnitRecordSheet CreateControl(IRecordSheetTemplateProvider assets) =>
        new(assets, new RecordSheetLayout(), NullLogger<ArmourDiagram>.Instance);

    private static IRecordSheetTemplateProvider CreateAssets(bool templateAvailable)
    {
        var assets = Substitute.For<IRecordSheetTemplateProvider>();
        assets.GetTemplateAsync("mek_biped_default.svg")
            .Returns(_ => Task.FromResult<Stream?>(templateAvailable ? StreamFor(TemplateSvg) : null));
        assets.GetPipClusterAsync(Arg.Any<string>()).Returns(_ => StreamFor(
            "<svg xmlns=\"http://www.w3.org/2000/svg\"><switch><g><path d=\"M0 0h1v1z\"/></g></switch></svg>"));
        return assets;
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 100 && !condition(); attempt++)
            await Task.Delay(20);

        condition().ShouldBeTrue();
    }

    private static Stream StreamFor(string value) => new MemoryStream(Encoding.UTF8.GetBytes(value));

    private static Mech CreateMech() => new(
        "Test",
        "Record Sheet",
        20,
        [
            new Head("Head", 8, 3),
            new CenterTorso("Center Torso", 10, 3, 6),
            new SideTorso("Left Torso", PartLocation.LeftTorso, 8, 2, 5),
            new SideTorso("Right Torso", PartLocation.RightTorso, 8, 2, 5),
            new Arm("Left Arm", PartLocation.LeftArm, 4, 4),
            new Arm("Right Arm", PartLocation.RightArm, 4, 4),
            new Leg("Left Leg", PartLocation.LeftLeg, 8, 4),
            new Leg("Right Leg", PartLocation.RightLeg, 8, 4)
        ]);

    private static readonly string TemplateSvg = CreateTemplate();

    private static string CreateTemplate()
    {
        var layout = new RecordSheetLayout();
        var ids = Enum.GetValues<PartLocation>().SelectMany(location => new[]
        {
            layout.TemplateRegionId(location, ArmourFace.Front),
            layout.TemplateRegionId(location, ArmourFace.Rear),
            layout.StructureRegionId(location)
        }).OfType<string>();
        return """
        <svg xmlns="http://www.w3.org/2000/svg" width="576" height="756" viewBox="0 0 576 756">
          <g id="canonArmorPips"/><g id="canonStructurePips"/>
        </svg>
        """.Replace("</svg>", string.Concat(ids.Select(id => $"<g id=\"{id}\"/>")) + "</svg>");
    }
}
