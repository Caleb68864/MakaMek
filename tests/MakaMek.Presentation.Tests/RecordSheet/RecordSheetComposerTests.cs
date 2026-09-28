using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Sanet.MakaMek.Assets.Services;
using Sanet.MakaMek.Core.Models.Units;
using Sanet.MakaMek.Core.Models.Units.Mechs;
using Sanet.MakaMek.Presentation.RecordSheet;
using Shouldly;

namespace Sanet.MakaMek.Presentation.Tests.RecordSheet;

/// <summary>
/// Composition has no UI dependency, so it is tested directly rather than through a rendered
/// control: no window, no dispatcher, no rasterisation.
/// </summary>
public class RecordSheetComposerTests
{
    [Fact]
    public async Task ComposeAsync_ProducesASheet_ForAMechWithOnePart()
    {
        var composer = CreateComposer(out _);
        var data = RecordSheetDiagramData.FromUnit(
            new Mech("TestMech", "TestModel", 20, [new CenterTorso("Center Torso", 10, 3, 6)]));

        var svg = await composer.ComposeAsync(data);

        svg.ShouldNotBeNull();
        Encoding.UTF8.GetString(svg!).ShouldContain("armorPipsCT");
    }

    [Fact]
    public async Task ComposeAsync_ReturnsNull_WhenTheTemplateIsUnavailable()
    {
        var composer = CreateComposer(out var assets);
        assets.GetTemplateAsync(Arg.Any<string>()).Returns(Task.FromResult<Stream?>(null));

        var svg = await composer.ComposeAsync(RecordSheetSamples.LightMech);

        svg.ShouldBeNull("a missing template is the signal to fall back to the text sheet");
    }

    private static RecordSheetComposer CreateComposer(out IRecordSheetTemplateProvider assets)
    {
        assets = Substitute.For<IRecordSheetTemplateProvider>();
        assets.GetTemplateAsync("mek_biped_default.svg").Returns(_ => StreamFor(TemplateSvg));
        assets.GetPipClusterAsync(Arg.Any<string>()).Returns(call => StreamFor(ClusterSvg(call.Arg<string>())));
        return new RecordSheetComposer(assets, new RecordSheetLayout(),
            NullLogger<RecordSheetComposer>.Instance);
    }

    private static Stream StreamFor(string value) => new MemoryStream(Encoding.UTF8.GetBytes(value));

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
