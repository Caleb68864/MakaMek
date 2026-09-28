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
    public async Task ComposeAsync_LaysOutAllTwelveCriticalSlots_AsTwoGroupsOfSix()
    {
        // Slot numbers are 1-based. Dividing them directly put slot 1 in the second row, split the
        // columns at 6/7 rather than 7/8, and computed column 2 for slot 12, which dropped it.
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

        for (var slot = 1; slot <= 12; slot++)
            svg.ShouldContain($"data-critical-slot=\"{slot}\"");

        // Slots 1 and 7 head their columns, so they share the topmost y of the table.
        var cells = System.Xml.Linq.XDocument.Parse(svg)
            .Descendants()
            .Where(e => e.Attribute("data-critical-slot") is not null)
            .ToDictionary(
                e => int.Parse((string)e.Attribute("data-critical-slot")!),
                e => (X: (string)e.Attribute("x")!, Y: (string)e.Attribute("y")!));

        cells[1].Y.ShouldBe(cells[7].Y, "slot 1 and slot 7 are both first in their column");
        cells[1].X.ShouldNotBe(cells[7].X, "and they are in different columns");
        cells[6].X.ShouldBe(cells[1].X, "slot 6 closes the first column");
        cells[12].X.ShouldBe(cells[7].X, "slot 12 closes the second");
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

    [Fact]
    public async Task ComposeAsync_NeverResolvesAnExternalEntity()
    {
        // Assets come from a third-party repository over the network. Real pip clusters carry a
        // public DTD, so declaring one is not itself refused - but an external entity must never be
        // fetched, and its target must never reach the composed output.
        var secret = Path.Combine(Path.GetTempPath(), $"makamek-xxe-{Guid.NewGuid():N}.txt");
        await File.WriteAllTextAsync(secret, "TOP-SECRET");
        try
        {
            // Built from the real fixture so composition would otherwise succeed: if the entity
            // were ever resolved, the file's contents would reach the composed sheet.
            var hostile = TemplateSvg
                .Replace("<svg ", $"<!DOCTYPE svg [<!ENTITY leak SYSTEM \"file://{secret}\">]>\n<svg ")
                .Replace("</svg>", "<text id=\"leaked\">&leak;</text></svg>");
            var assets = Substitute.For<IRecordSheetTemplateProvider>();
            assets.GetTemplateAsync("mek_biped_default.svg").Returns(_ => StreamFor(hostile));
            assets.GetPipClusterAsync(Arg.Any<string>())
                .Returns(call => StreamFor(ClusterSvg(call.Arg<string>())));
            var composer = new RecordSheetComposer(assets, new RecordSheetLayout(),
                NullLogger<RecordSheetComposer>.Instance);

            var svg = await composer.ComposeAsync(RecordSheetSamples.LightMech);

            // Either the document is refused outright or the entity is dropped; what must never
            // happen is the file's contents appearing in the sheet.
            if (svg is not null)
                Encoding.UTF8.GetString(svg).ShouldNotContain("TOP-SECRET");
        }
        finally
        {
            File.Delete(secret);
        }
    }

    [Fact]
    public async Task ComposeAsync_AcceptsClustersWithAPublicDtdAndInternalEntities()
    {
        // The real pip clusters are Illustrator exports: a public DTD plus an internal subset whose
        // entities their own markup then references. Refusing those broke the feature outright.
        var cluster = """
            <?xml version="1.0" encoding="utf-8"?>
            <!DOCTYPE svg PUBLIC "-//W3C//DTD SVG 1.1//EN" "http://www.w3.org/Graphics/SVG/1.1/DTD/svg11.dtd" [
                <!ENTITY ns_ai "http://ns.adobe.com/AdobeIllustrator/10.0/">
            ]>
            <svg xmlns="http://www.w3.org/2000/svg" xmlns:i="&ns_ai;">
              <switch><g><path d="M20 80h40v40h-40z"/></g></switch>
            </svg>
            """;
        var assets = Substitute.For<IRecordSheetTemplateProvider>();
        assets.GetTemplateAsync("mek_biped_default.svg").Returns(_ => StreamFor(TemplateSvg));
        assets.GetPipClusterAsync(Arg.Any<string>()).Returns(_ => StreamFor(cluster));
        var composer = new RecordSheetComposer(assets, new RecordSheetLayout(),
            NullLogger<RecordSheetComposer>.Instance);

        var svg = await composer.ComposeAsync(RecordSheetSamples.LightMech);

        svg.ShouldNotBeNull("a public DTD with a used internal subset is how the real assets ship");
    }

    [Fact]
    public async Task ComposeAsync_ReturnsNull_WhenTheTemplateIsNotXml()
    {
        var assets = Substitute.For<IRecordSheetTemplateProvider>();
        assets.GetTemplateAsync("mek_biped_default.svg").Returns(_ => StreamFor("404: not found"));
        assets.GetPipClusterAsync(Arg.Any<string>())
            .Returns(call => StreamFor(ClusterSvg(call.Arg<string>())));
        var composer = new RecordSheetComposer(assets, new RecordSheetLayout(),
            NullLogger<RecordSheetComposer>.Instance);

        var svg = await composer.ComposeAsync(RecordSheetSamples.LightMech);

        svg.ShouldBeNull("a provider answering with something that is not SVG must not throw");
    }

    [Fact]
    public async Task ComposeAsync_ReturnsNull_WhenAPipClusterIsNotXml()
    {
        var assets = Substitute.For<IRecordSheetTemplateProvider>();
        assets.GetTemplateAsync("mek_biped_default.svg").Returns(_ => StreamFor(TemplateSvg));
        assets.GetPipClusterAsync(Arg.Any<string>()).Returns(_ => StreamFor("<not-svg"));
        var composer = new RecordSheetComposer(assets, new RecordSheetLayout(),
            NullLogger<RecordSheetComposer>.Instance);

        var svg = await composer.ComposeAsync(RecordSheetSamples.LightMech);

        svg.ShouldBeNull();
    }

    [Fact]
    public async Task ComposeAsync_StopsWhenCancelled()
    {
        // A player switching units quickly supersedes renders. Without cancellation the abandoned
        // work still composes and rasterises a whole sheet before its result is thrown away.
        var composer = CreateComposer(out var assets);
        using var cancellation = new CancellationTokenSource();
        assets.GetPipClusterAsync(Arg.Any<string>()).Returns(call =>
        {
            cancellation.Cancel();
            return StreamFor(ClusterSvg(call.Arg<string>()));
        });

        await Should.ThrowAsync<OperationCanceledException>(
            () => composer.ComposeAsync(RecordSheetSamples.AssaultMech, null, cancellation.Token));
    }

    [Fact]
    public async Task ComposeAsync_StopsBeforeAnyWork_WhenAlreadyCancelled()
    {
        var composer = CreateComposer(out var assets);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(
            () => composer.ComposeAsync(RecordSheetSamples.LightMech, null, cancellation.Token));

        await assets.DidNotReceive().GetTemplateAsync(Arg.Any<string>());
    }

    [Fact]
    public async Task ComposeAsync_ReturnsNull_WhenTheTemplateHasNoPipOverlayLayers()
    {
        var composer = CreateComposer(out var assets);
        assets.GetTemplateAsync(Arg.Any<string>()).Returns(_ => StreamFor("""
            <svg xmlns="http://www.w3.org/2000/svg" width="576" height="756" viewBox="0 0 576 756">
              <rect width="576" height="756" fill="white"/>
            </svg>
            """));

        var svg = await composer.ComposeAsync(RecordSheetSamples.LightMech);

        svg.ShouldBeNull("without canonArmorPips and canonStructurePips there is nothing to draw into");
    }

    [Theory]
    [InlineData("armorPipsCT")]
    [InlineData("isPipsCT")]
    public async Task ComposeAsync_ReturnsNull_WhenATemplateRegionIsMissing(string missingRegionId)
    {
        var composer = CreateComposer(out var assets);
        assets.GetTemplateAsync(Arg.Any<string>()).Returns(_ => StreamFor(TemplateWithout(missingRegionId)));

        var svg = await composer.ComposeAsync(RecordSheetSamples.LightMech);

        svg.ShouldBeNull($"a sheet missing {missingRegionId} would silently omit that location's pips");
    }

    [Fact]
    public async Task ComposeAsync_StillComposes_WhenTheTemplateHasNoFluffRegion()
    {
        var composer = CreateComposer(out var assets);
        assets.GetTemplateAsync(Arg.Any<string>()).Returns(_ => StreamFor(TemplateWithout("fluffSinglePilot")));
        var data = RecordSheetDiagramData.FromUnit(CreateMech());

        var svg = await Compose(composer, data, [1, 2, 3, 4]);

        svg.ShouldNotContain("<image", Case.Insensitive);
        svg.ShouldContain("armorPipsCT", Case.Insensitive,
            "missing artwork is cosmetic - the sheet itself must still render");
    }

    [Fact]
    public async Task ComposeAsync_ReturnsNull_WhenAnOversizedRegionHasNoRowsForGeneratedPips()
    {
        // Left arm front armour above the 34-pip cluster ceiling forces the generated fallback,
        // and armorPipsLA is an empty group with no rows to place pips into.
        var composer = CreateComposer(out var assets);
        assets.GetPipClusterAsync(Arg.Is<string>(name => name.StartsWith("Armor_")))
            .Returns(Task.FromResult<Stream?>(null));
        var data = RecordSheetDiagramData.Create(20,
            [new KeyValuePair<ArmourRegion, int>(new ArmourRegion(PartLocation.LeftArm, ArmourFace.Front), 40)]);

        var svg = await composer.ComposeAsync(data);

        svg.ShouldBeNull();
    }

    [Fact]
    public async Task ComposeAsync_ReturnsNull_WhenAPipClusterHasNoSwitchLayer()
    {
        var composer = CreateComposer(out var assets);
        assets.GetPipClusterAsync(Arg.Any<string>()).Returns(_ => StreamFor("""
            <svg xmlns="http://www.w3.org/2000/svg" width="576" height="756" viewBox="0 0 576 756">
              <g><path d="M20 80h40v40h-40z"/></g>
            </svg>
            """));

        var svg = await composer.ComposeAsync(RecordSheetSamples.LightMech);

        svg.ShouldBeNull("a cluster without its switch layer carries no pips to import");
    }

    [Fact]
    public async Task ComposeAsync_ReturnsNull_WhenAPipClusterCannotBeRead()
    {
        var composer = CreateComposer(out var assets);
        assets.GetPipClusterAsync(Arg.Any<string>()).Returns(_ => Task.FromResult<Stream?>(new ThrowingStream()));

        var svg = await composer.ComposeAsync(RecordSheetSamples.LightMech);

        svg.ShouldBeNull("a failed cluster read must not produce a sheet with pips silently missing");
    }

    [Fact]
    public async Task ComposeAsync_FadesStructurePips_InProportionToRemainingStructure()
    {
        var composer = CreateComposer(out _);
        var data = RecordSheetSamples.LightMech with
        {
            PartStates = new Dictionary<PartLocation, RecordSheetPartData>
            {
                [PartLocation.CenterTorso] = new(10, 10, 2, 2,
                    CurrentStructure: 3, MaxStructure: 6, IsDestroyed: false, IsBlownOff: false)
            }
        };

        var svg = await Compose(composer, data);

        svg.ShouldContain("opacity=\"0.5\"", Case.Insensitive,
            "half the structure remaining should render at half opacity");
    }

    [Theory]
    [InlineData(true, false, "0.04")]
    [InlineData(false, true, "0.3")]
    public async Task ComposeAsync_DimsGeneratedFallbackPips_ForPartsThatAreGone(
        bool isBlownOff, bool isDestroyed, string expectedOpacity)
    {
        var composer = CreateComposer(out var assets);
        assets.GetPipClusterAsync(Arg.Is<string>(name => name.StartsWith("Armor_")))
            .Returns(Task.FromResult<Stream?>(null));
        var data = OversizedCentreTorso() with
        {
            PartStates = new Dictionary<PartLocation, RecordSheetPartData>
            {
                [PartLocation.CenterTorso] = new(0, 60, 0, 0, 0, 6, isDestroyed, isBlownOff)
            }
        };

        var svg = await Compose(composer, data);

        svg.ShouldContain("data-generated-fallback");
        svg.ShouldContain($"opacity=\"{expectedOpacity}\"");
    }

    [Fact]
    public async Task ComposeAsync_FadesGeneratedStructurePips_InProportionToRemainingStructure()
    {
        // Structure only falls back above 100 tons, where MegaMek ships no cluster.
        var composer = CreateComposer(out var assets);
        assets.GetPipClusterAsync(Arg.Is<string>(name => name.StartsWith("BipedIS")))
            .Returns(Task.FromResult<Stream?>(null));
        var data = RecordSheetDiagramData.Create(105,
            [new KeyValuePair<ArmourRegion, int>(new ArmourRegion(PartLocation.CenterTorso, ArmourFace.Front), 10)])
            with
            {
                PartStates = new Dictionary<PartLocation, RecordSheetPartData>
                {
                    [PartLocation.CenterTorso] = new(10, 10, 0, 0,
                        CurrentStructure: 3, MaxStructure: 6, IsDestroyed: false, IsBlownOff: false)
                }
            };

        var svg = await Compose(composer, data);

        svg.ShouldContain("data-generated-fallback=\"internal structure\"");
        svg.ShouldContain("opacity=\"0.5\"", Case.Insensitive,
            "half the structure remaining should render at half opacity");
    }

    [Fact]
    public async Task ComposeAsync_ReturnsNull_WhenStructureHasNeitherAClusterNorRowsToFallBackOn()
    {
        // Above 100 tons MegaMek ships no structure cluster, so the left arm falls back - and
        // isPipsLA is an empty group with no rows to draw into.
        var composer = CreateComposer(out var assets);
        assets.GetPipClusterAsync(Arg.Is<string>(name => name.StartsWith("BipedIS")))
            .Returns(Task.FromResult<Stream?>(null));
        var data = RecordSheetSamples.LightMech with
        {
            Tonnage = 105,
            PartStates = new Dictionary<PartLocation, RecordSheetPartData>
            {
                [PartLocation.LeftArm] = new(4, 4, 0, 0,
                    CurrentStructure: 5, MaxStructure: 5, IsDestroyed: false, IsBlownOff: false)
            }
        };

        var svg = await composer.ComposeAsync(data);

        svg.ShouldBeNull();
    }

    [Fact]
    public async Task ComposeAsync_CarriesAncestorTransforms_OntoGeneratedPips()
    {
        var composer = CreateComposer(out var assets);
        assets.GetPipClusterAsync(Arg.Is<string>(name => name.StartsWith("Armor_")))
            .Returns(Task.FromResult<Stream?>(null));
        assets.GetTemplateAsync(Arg.Any<string>()).Returns(_ => StreamFor(CompleteTemplate("""
            <svg xmlns="http://www.w3.org/2000/svg" width="576" height="756" viewBox="0 0 576 756">
              <g id="canonArmorPips"/><g id="canonStructurePips"/>
              <g transform="translate(12,7)">
                <g id="armorPipsCT">
                  <rect id="armorCTRow00" x="0" y="0" width="33" height="6"/>
                  <rect id="armorCTRow01" x="0" y="5.3" width="33" height="6"/>
                </g>
              </g>
              <text id="textArmor_CT" x="10" y="20"/>
            </svg>
            """)));

        var svg = await Compose(composer, OversizedCentreTorso());

        svg.ShouldContain("transform=\"translate(12,7)\"", Case.Insensitive,
            "generated pips sit at the root, so an ancestor transform has to be copied onto them");
    }

    /// <summary>Centre torso armour above the 51-pip cluster ceiling, which forces the fallback.</summary>
    private static RecordSheetDiagramData OversizedCentreTorso() =>
        RecordSheetDiagramData.Create(20,
            [new KeyValuePair<ArmourRegion, int>(new ArmourRegion(PartLocation.CenterTorso, ArmourFace.Front), 60)]);

    /// <summary>Returns the standard template with one element removed, by id.</summary>
    private static string TemplateWithout(string id)
    {
        var document = System.Xml.Linq.XDocument.Parse(TemplateSvg);
        document.Descendants()
            .Single(element => (string?)element.Attribute("id") == id)
            .Remove();
        return document.ToString();
    }

    /// <summary>A stream that fails on read, standing in for a truncated or broken asset fetch.</summary>
    private sealed class ThrowingStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new IOException("asset read failed");
        public override void Flush() => throw new NotSupportedException();
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
