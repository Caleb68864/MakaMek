using Microsoft.Extensions.Logging;
using NSubstitute;
using Sanet.MakaMek.Assets.ResourceProviders;
using Sanet.MakaMek.Assets.Services;
using Shouldly;

namespace Sanet.MakaMek.Assets.Tests.Services;

public class RecordSheetTemplateProviderTests
{
    [Fact]
    public async Task FetchesTemplateThroughResourceProvider()
    {
        var resourceProvider = Substitute.For<IResourceStreamProvider>();
        resourceProvider.GetAvailableResourceIds().Returns(["https://example.test/templates/mek_biped_default.svg"]);
        resourceProvider.GetResourceStream("https://example.test/templates/mek_biped_default.svg")
            .Returns(_ => new MemoryStream([1, 2, 3]));
        var sut = new RecordSheetTemplateProvider([resourceProvider],
            Substitute.For<ILogger<RecordSheetTemplateProvider>>());

        await using var stream = await sut.GetTemplateAsync("mek_biped_default.svg");

        stream.ShouldNotBeNull();
        stream.ReadByte().ShouldBe(1);
        await resourceProvider.Received(1).GetResourceStream("https://example.test/templates/mek_biped_default.svg");
    }

    [Fact]
    public async Task FetchesPipClusterThroughResourceProvider()
    {
        var resourceProvider = Substitute.For<IResourceStreamProvider>();
        resourceProvider.GetAvailableResourceIds().Returns(["https://example.test/pips/Armor_CT_10_Humanoid.svg"]);
        resourceProvider.GetResourceStream("https://example.test/pips/Armor_CT_10_Humanoid.svg")
            .Returns(_ => new MemoryStream([4, 5, 6]));
        var sut = new RecordSheetTemplateProvider([resourceProvider],
            Substitute.For<ILogger<RecordSheetTemplateProvider>>());

        await using var stream = await sut.GetPipClusterAsync("Armor_CT_10_Humanoid.svg");

        stream.ShouldNotBeNull();
        stream.ReadByte().ShouldBe(4);
    }

    [Fact]
    public async Task MissingTemplateReturnsNullAndLogsOnce()
    {
        var resourceProvider = Substitute.For<IResourceStreamProvider>();
        resourceProvider.GetAvailableResourceIds().Returns([]);
        var logger = Substitute.For<ILogger<RecordSheetTemplateProvider>>();
        var sut = new RecordSheetTemplateProvider([resourceProvider], logger);

        (await sut.GetPipClusterAsync("absent.svg")).ShouldBeNull();
        (await sut.GetPipClusterAsync("absent.svg")).ShouldBeNull();

        logger.Received(1).Log(
            LogLevel.Warning,
            Arg.Any<EventId>(),
            Arg.Is<object>(state => state.ToString()!.Contains("absent.svg")),
            Arg.Any<Exception?>(),
            Arg.Any<Func<object, Exception?, string>>());
    }

    [Fact]
    public async Task LocalCheckoutLoadsUnmodifiedTemplate()
    {
        var root = Path.Combine(Path.GetTempPath(), $"makamek-record-sheet-{Guid.NewGuid():N}");
        var templates = Path.Combine(root, "data", "images", "recordsheets", "templates_us");
        var pips = Path.Combine(root, "data", "images", "recordsheets", "biped_pips");
        Directory.CreateDirectory(templates);
        Directory.CreateDirectory(pips);
        try
        {
            var source = "<svg id=\"original\"/>";
            var pipSource = "<svg id=\"pip-original\"/>";
            await File.WriteAllTextAsync(Path.Combine(templates, "mek_biped_default.svg"), source);
            await File.WriteAllTextAsync(Path.Combine(pips, "Armor_CT_1_Humanoid.svg"), pipSource);
            var sut = RecordSheetTemplateProvider.FromLocalCheckout(root,
                Substitute.For<ILogger<RecordSheetTemplateProvider>>());

            await using var stream = await sut.GetTemplateAsync("mek_biped_default.svg");

            stream.ShouldNotBeNull();
            using var reader = new StreamReader(stream);
            (await reader.ReadToEndAsync()).ShouldBe(source);

            await using var pipStream = await sut.GetPipClusterAsync("Armor_CT_1_Humanoid.svg");
            pipStream.ShouldNotBeNull();
            using var pipReader = new StreamReader(pipStream);
            (await pipReader.ReadToEndAsync()).ShouldBe(pipSource);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}
