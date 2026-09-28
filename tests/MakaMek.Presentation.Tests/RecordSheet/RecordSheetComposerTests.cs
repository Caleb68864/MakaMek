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

    [Fact]
    public async Task ComposeAsync_MarksRecentlyDamagedLocations()
    {
        var composer = CreateComposer(out _);
        var data = RecordSheetDiagramData.FromUnit(CreateMech()) with
        {
            RecentDamageLocations = new HashSet<PartLocation> { PartLocation.CenterTorso }
        };

        var svg = await Compose(composer, data);

        svg.ShouldContain("data-recent-damage=\"CenterTorso\"");
        svg.ShouldContain("#e4572e");
    }

    [Theory]
    [InlineData(true, false, "0.04", " ×")]
    [InlineData(false, true, "0.3", " †")]
    public async Task ComposeAsync_DimsAndMarksLocationsThatAreGone(
        bool blownOff, bool destroyed, string opacity, string marker)
    {
        var composer = CreateComposer(out _);
        var mech = CreateMech();
        var data = RecordSheetDiagramData.FromUnit(mech) with
        {
            PartStates = new Dictionary<PartLocation, RecordSheetPartData>
            {
                [PartLocation.CenterTorso] = new(6, 10, 2, 3, 4, 6, destroyed, blownOff)
            }
        };

        var svg = await Compose(composer, data);

        svg.ShouldContain($"opacity=\"{opacity}\"");
        svg.ShouldContain(marker);
    }

    [Fact]
    public async Task ComposeAsync_ScalesStructurePipOpacityWithRemainingStructure()
    {
        var composer = CreateComposer(out _);
        var data = RecordSheetDiagramData.FromUnit(CreateMech()) with
        {
            PartStates = new Dictionary<PartLocation, RecordSheetPartData>
            {
                [PartLocation.CenterTorso] = new(10, 10, 3, 3, 3, 6, false, false)
            }
        };

        var svg = await Compose(composer, data);

        // Half the structure left, so the structure pips are drawn at half opacity.
        svg.ShouldContain("opacity=\"0.5\"");
    }

    [Fact]
    public async Task ComposeAsync_GeneratesFallbackPips_WhenArmourExceedsTheClusterRange()
    {
        // 60 points of centre torso armour is past the largest canon cluster (51), so the sheet
        // draws its own pips rather than losing them.
        var composer = CreateComposer(out var assets);
        assets.GetPipClusterAsync(Arg.Is<string>(name => name.Contains("_CT_")))
            .Returns(Task.FromResult<Stream?>(null));
        var data = RecordSheetDiagramData.Create(20,
            [new(new ArmourRegion(PartLocation.CenterTorso, ArmourFace.Front), 60)]);

        var svg = await Compose(composer, data);

        svg.ShouldContain("data-generated-fallback");
        svg.ShouldContain("data-generated-pip");
    }

    [Fact]
    public async Task ComposeAsync_ReturnsNull_WhenAClusterWithinRangeIsMissing()
    {
        // Inside the canon range there is no fallback: a missing cluster means no sheet, which is
        // what makes the view fall back to text.
        var composer = CreateComposer(out var assets);
        assets.GetPipClusterAsync(Arg.Any<string>()).Returns(Task.FromResult<Stream?>(null));

        var svg = await composer.ComposeAsync(RecordSheetSamples.LightMech);

        svg.ShouldBeNull();
    }

    [Fact]
    public async Task ComposeAsync_EmbedsFluffArtwork_WhenItIsSupplied()
    {
        var composer = CreateComposer(out _);
        var data = RecordSheetDiagramData.FromUnit(CreateMech()) with
        {
            FluffArtworkName = "TestMech TestModel"
        };
        var artwork = new byte[] { 137, 80, 78, 71 };

        var withArtwork = await Compose(composer, data, artwork);
        var without = await Compose(composer, data);

        withArtwork.ShouldContain("data-record-sheet-artwork=\"TestMech TestModel\"");
        withArtwork.ShouldContain(Convert.ToBase64String(artwork));
        without.ShouldNotContain("data-record-sheet-artwork");
    }

    [Fact]
    public async Task ComposeAsync_IgnoresArtwork_WhenTheUnitNamesNone()
    {
        var composer = CreateComposer(out _);
        var data = RecordSheetDiagramData.FromUnit(CreateMech()) with { FluffArtworkName = null };

        var svg = await Compose(composer, data, [137, 80, 78, 71]);

        svg.ShouldNotContain("data-record-sheet-artwork");
    }

    [Fact]
    public async Task ComposeAsync_DrawsCriticalSlots_AndMarksLocationsWithout()
    {
        var composer = CreateComposer(out _);
        var data = RecordSheetDiagramData.FromUnit(CreateMech()) with
        {
            CriticalSlotsByLocation = new Dictionary<PartLocation, IReadOnlyList<RecordSheetCriticalSlotData>>
            {
                [PartLocation.CenterTorso] =
                [
                    new RecordSheetCriticalSlotData(1, "Engine", CriticalSlotState.Intact),
                    new RecordSheetCriticalSlotData(2, "Gyro", CriticalSlotState.Hit),
                    new RecordSheetCriticalSlotData(3, null, CriticalSlotState.Empty),
                    new RecordSheetCriticalSlotData(4, "Sensors", CriticalSlotState.Destroyed)
                ]
            }
        };

        var svg = await Compose(composer, data);

        // Assert on the state each cell carries rather than on the label text, which the layout may
        // shorten or drop.
        svg.ShouldContain("data-slot-state=\"Intact\"");
        svg.ShouldContain("data-slot-state=\"Hit\"");
        svg.ShouldContain("data-slot-state=\"Empty\"");
        // The head has a region in the template but no slots in the data, so it is marked instead.
        svg.ShouldContain("crits_HD");
    }

    [Fact]
    public async Task ComposeAsync_DropsTheLastCriticalSlot_WhichLooksLikeAnOffByOne()
    {
        // Pins current behaviour rather than endorsing it. Cells are placed with
        // column = slot.Slot / rows over 1-based slot numbers, so the highest slot in a location
        // computes column 2 and is skipped: with 12 slots, rows is 6 and 12 / 6 == 2. For 1-based
        // numbering the maths wants (Slot - 1). Flagged for the maintainer rather than changed here,
        // since it alters what the sheet draws.
        var composer = CreateComposer(out _);
        var slots = Enumerable.Range(1, 12)
            .Select(slot => new RecordSheetCriticalSlotData(slot, $"Item{slot}", CriticalSlotState.Intact))
            .ToList();
        var data = RecordSheetDiagramData.FromUnit(CreateMech()) with
        {
            CriticalSlotsByLocation = new Dictionary<PartLocation, IReadOnlyList<RecordSheetCriticalSlotData>>
            {
                [PartLocation.CenterTorso] = slots
            }
        };

        var svg = await Compose(composer, data);

        for (var slot = 1; slot <= 11; slot++)
            svg.ShouldContain($"data-critical-slot=\"{slot}\"");
        // The twelfth slot is currently dropped - see the comment above.
        svg.ShouldNotContain("data-critical-slot=\"12\"");
    }

    [Fact]
    public async Task ComposeAsync_UsesHeavierStructureClusters_AboveOneHundredTons()
    {
        var composer = CreateComposer(out var assets);
        var data = RecordSheetDiagramData.Create(105,
            [new(new ArmourRegion(PartLocation.CenterTorso, ArmourFace.Front), 10)]);

        await composer.ComposeAsync(data);

        await assets.Received().GetPipClusterAsync(
            Arg.Is<string>(name => name.StartsWith("BipedIS105_")));
    }

    [Fact]
    public async Task ComposeAsync_CoversEveryLocation_ForAFullyArmouredAssaultMech()
    {
        // The light sample only carries a centre torso; the assault sample has armour on every
        // location and face, which is what exercises the per-location pip ranges.
        var composer = CreateComposer(out _);

        var svg = await Compose(composer, RecordSheetSamples.AssaultMech);

        foreach (var code in new[] { "HD", "CT", "LT", "RT", "LA", "RA", "LL", "RL" })
            svg.ShouldContain($"armorPips{code}");
        svg.ShouldContain("armorPipsCTR");
    }

    [Fact]
    public async Task ComposeAsync_ReadsATemplateThatIsNotAlreadyBuffered()
    {
        // The provider hands back a MemoryStream today, but a network or file provider need not, and
        // the buffering path has to cope with a stream of unknown length.
        var assets = Substitute.For<IRecordSheetTemplateProvider>();
        assets.GetTemplateAsync("mek_biped_default.svg")
            .Returns(_ => Task.FromResult<Stream?>(new UnbufferedStream(Encoding.UTF8.GetBytes(TemplateSvg))));
        assets.GetPipClusterAsync(Arg.Any<string>())
            .Returns(call => StreamFor(ClusterSvg(call.Arg<string>())));
        var composer = new RecordSheetComposer(assets, new RecordSheetLayout(),
            NullLogger<RecordSheetComposer>.Instance);

        var svg = await composer.ComposeAsync(RecordSheetSamples.LightMech);

        svg.ShouldNotBeNull();
    }

    /// <summary>A read-only stream that is not a MemoryStream and reports no length.</summary>
    private sealed class UnbufferedStream(byte[] payload) : Stream
    {
        private readonly MemoryStream _inner = new(payload);
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }
        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private static async Task<string> Compose(
        RecordSheetComposer composer, RecordSheetDiagramData data, byte[]? artwork = null)
    {
        var svg = await composer.ComposeAsync(data, artwork);
        svg.ShouldNotBeNull();
        return Encoding.UTF8.GetString(svg!);
    }

    private static Mech CreateMech() =>
        new("TestMech", "TestModel", 20, [new CenterTorso("Center Torso", 10, 3, 6)]);

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
