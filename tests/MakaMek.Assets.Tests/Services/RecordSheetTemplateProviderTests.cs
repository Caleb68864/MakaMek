using System.Text;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Sanet.MakaMek.Assets.Configuration;
using Sanet.MakaMek.Services;
using Sanet.MakaMek.Assets.ResourceProviders;
using Sanet.MakaMek.Assets.Services;
using Shouldly;

namespace Sanet.MakaMek.Assets.Tests.Services;

public class RecordSheetTemplateProviderTests
{
    [Fact]
    public async Task CachedAssetsRemainReadableWhenProviderGoesOffline()
    {
        var provider = Substitute.For<IResourceStreamProvider>();
        provider.GetAvailableResourceIds().Returns(["mek_biped_default.svg"]);
        provider.GetResourceStream("mek_biped_default.svg").Returns(_ => new MemoryStream([1, 2, 3]));
        var sut = new RecordSheetTemplateProvider([provider],
            Substitute.For<ILogger<RecordSheetTemplateProvider>>());
        await using (var first = await sut.GetTemplateAsync("mek_biped_default.svg"))
            first!.ReadByte().ShouldBe(1);
        provider.GetResourceStream("mek_biped_default.svg").Returns(Task.FromException<Stream?>(new IOException("offline")));

        await using var cached = await sut.GetTemplateAsync("mek_biped_default.svg");
        cached.ShouldNotBeNull();
        cached.ReadByte().ShouldBe(1);
        cached.CanWrite.ShouldBeFalse();
        await provider.Received(1).GetResourceStream("mek_biped_default.svg");
    }

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
    public async Task ATransientListingFailureIsRetried_NotCachedForTheSession()
    {
        // The source can be offline or rate limited when the app starts. Remembering that empty
        // answer would leave the player with no record sheets until they restart.
        var provider = Substitute.For<IResourceStreamProvider>();
        var attempts = 0;
        provider.GetAvailableResourceIds().Returns(_ =>
            ++attempts == 1
                ? throw new HttpRequestException("offline")
                : Task.FromResult<IEnumerable<string>>(["mek_biped_default.svg"]));
        provider.GetResourceStream("mek_biped_default.svg")
            .Returns(_ => new MemoryStream(Encoding.UTF8.GetBytes("<svg/>")));
        var sut = new RecordSheetTemplateProvider([provider],
            Substitute.For<ILogger<RecordSheetTemplateProvider>>());

        (await sut.GetTemplateAsync("mek_biped_default.svg")).ShouldBeNull("the source was offline");

        await using var recovered = await sut.GetTemplateAsync("mek_biped_default.svg");

        recovered.ShouldNotBeNull("the listing is tried again once the source is back");
        attempts.ShouldBe(2);
    }

    [Fact]
    public async Task ConfiguredFilesystemProvidersLoadUnmodifiedTemplatesAndPips()
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
            // Exercises the real wiring: the source comes from the asset provider configuration,
            // built by the same factory the app uses, rather than from a hardcoded path.
            var configuration = Substitute.For<IAssetProviderConfigurationProvider>();
            configuration.GetActiveProviders(AssetType.RecordSheetTemplates).Returns([
                new AssetProviderConfigData("templates", ProviderType.Filesystem,
                    AssetType.RecordSheetTemplates, Path.Combine(root, "data"),
                    IsActive: true, IsDefault: true, SortOrder: 0)
            ]);
            configuration.GetActiveProviders(AssetType.RecordSheetPips).Returns([
                new AssetProviderConfigData("pips", ProviderType.Filesystem,
                    AssetType.RecordSheetPips, Path.Combine(root, "data"),
                    IsActive: true, IsDefault: true, SortOrder: 0)
            ]);
            var factory = new ResourceStreamProviderFactory(
                Substitute.For<IFileCachingService>(), Substitute.For<ILoggerFactory>());
            var sut = new RecordSheetTemplateProvider(
                new ConfiguredResourceProviders(configuration, factory,
                    AssetType.RecordSheetTemplates, AssetType.RecordSheetPips),
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
