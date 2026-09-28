using System.Text;
using NSubstitute;
using Sanet.MakaMek.Assets.Configuration;
using Sanet.MakaMek.Assets.ResourceProviders;
using Sanet.MakaMek.Assets.Services;
using Shouldly;

namespace MakaMek.Assets.Tests.Services;

public class RecordSheetArtworkProviderTests
{
    [Fact]
    public async Task GetMechArtworkAsync_ReturnsExactPngNameCaseInsensitively()
    {
        const string resourceId = "https://example.test/Atlas%20AS7-D.PNG";
        var provider = Substitute.For<IResourceStreamProvider>();
        provider.GetAvailableResourceIds().Returns([resourceId]);
        provider.GetResourceStream(resourceId).Returns(_ => new MemoryStream(Encoding.UTF8.GetBytes("png")));
        var sut = new RecordSheetArtworkProvider([provider]);

        await using var result = await sut.GetMechArtworkAsync("Atlas AS7-D");

        result.ShouldNotBeNull();
        using var reader = new StreamReader(result!);
        (await reader.ReadToEndAsync()).ShouldBe("png");
        await provider.Received(1).GetResourceStream(resourceId);
    }

    [Fact]
    public async Task GetMechArtworkAsync_ResolvesItsSourceFromConfiguration()
    {
        // Artwork is a configured asset type like any other, so the provider is built by the same
        // factory from the same configuration rather than from a source fixed in code.
        const string resourceId = "https://example.test/Atlas AS7-D.png";
        var configuration = Substitute.For<IAssetProviderConfigurationProvider>();
        configuration.GetActiveProviders(AssetType.UnitFluff).Returns([
            new AssetProviderConfigData("fluff", ProviderType.Filesystem, AssetType.UnitFluff,
                Path.GetTempPath(), IsActive: true, IsDefault: true, SortOrder: 0)
        ]);
        var inner = Substitute.For<IResourceStreamProvider>();
        inner.GetAvailableResourceIds().Returns([resourceId]);
        inner.GetResourceStream(resourceId).Returns(_ => new MemoryStream(Encoding.UTF8.GetBytes("png")));
        var factory = Substitute.For<IResourceStreamProviderFactory>();
        factory.Create(Arg.Any<AssetProviderConfigData>()).Returns(inner);
        var sut = new RecordSheetArtworkProvider(
            new ConfiguredResourceProviders(configuration, factory, AssetType.UnitFluff));

        await using var result = await sut.GetMechArtworkAsync("Atlas AS7-D");

        result.ShouldNotBeNull();
        await configuration.Received(1).GetActiveProviders(AssetType.UnitFluff);
    }

    [Fact]
    public async Task GetMechArtworkAsync_SurvivesAProviderThatThrows()
    {
        // Fluff art is optional, so a source that fails must not surface as an error.
        var broken = Substitute.For<IResourceStreamProvider>();
        broken.GetAvailableResourceIds().Returns<IEnumerable<string>>(_ => throw new IOException("offline"));
        var working = Substitute.For<IResourceStreamProvider>();
        working.GetAvailableResourceIds().Returns(["Atlas AS7-D.png"]);
        working.GetResourceStream("Atlas AS7-D.png")
            .Returns(_ => new MemoryStream(Encoding.UTF8.GetBytes("png")));
        var sut = new RecordSheetArtworkProvider([broken, working]);

        await using var result = await sut.GetMechArtworkAsync("Atlas AS7-D");

        result.ShouldNotBeNull("the second source still answers");
    }

    [Fact]
    public async Task GetMechArtworkAsync_ReturnsNull_WhenAProviderYieldsNoStream()
    {
        var provider = Substitute.For<IResourceStreamProvider>();
        provider.GetAvailableResourceIds().Returns(["Atlas AS7-D.png"]);
        provider.GetResourceStream("Atlas AS7-D.png").Returns(Task.FromResult<Stream?>(null));
        var sut = new RecordSheetArtworkProvider([provider]);

        (await sut.GetMechArtworkAsync("Atlas AS7-D")).ShouldBeNull();
    }

    [Theory]
    // Upstream files a unit's art by chassis, with variants separated by an underscore, so a
    // "Chassis Model" name has to be matched against all three shapes it might be filed under.
    [InlineData("Atlas AS7-D", "Atlas_AS7-D.png")]
    [InlineData("Atlas AS7-D", "Atlas.png")]
    [InlineData("Atlas", "Atlas.png")]
    public async Task GetMechArtworkAsync_MatchesHowUpstreamFilesArtwork(string mechName, string fileName)
    {
        var provider = Substitute.For<IResourceStreamProvider>();
        provider.GetAvailableResourceIds().Returns([fileName]);
        provider.GetResourceStream(fileName).Returns(_ => new MemoryStream(Encoding.UTF8.GetBytes("png")));
        var sut = new RecordSheetArtworkProvider([provider]);

        await using var result = await sut.GetMechArtworkAsync(mechName);

        result.ShouldNotBeNull($"{mechName} should resolve to {fileName}");
    }

    [Fact]
    public async Task GetMechArtworkAsync_PrefersTheVariantOverTheChassis()
    {
        var provider = Substitute.For<IResourceStreamProvider>();
        provider.GetAvailableResourceIds().Returns(["Atlas.png", "Atlas_AS7-D.png"]);
        provider.GetResourceStream(Arg.Any<string>())
            .Returns(_ => new MemoryStream(Encoding.UTF8.GetBytes("png")));
        var sut = new RecordSheetArtworkProvider([provider]);

        await using var result = await sut.GetMechArtworkAsync("Atlas AS7-D");

        result.ShouldNotBeNull();
        await provider.Received(1).GetResourceStream("Atlas_AS7-D.png");
        await provider.DidNotReceive().GetResourceStream("Atlas.png");
    }

    [Fact]
    public async Task GetMechArtworkAsync_WhenNoMatchingImageExists_ReturnsNull()
    {
        var provider = Substitute.For<IResourceStreamProvider>();
        provider.GetAvailableResourceIds().Returns(["https://example.test/Atlas AS7-D alternate.png"]);
        var sut = new RecordSheetArtworkProvider([provider]);

        var result = await sut.GetMechArtworkAsync("Atlas AS7-D");

        result.ShouldBeNull();
        await provider.DidNotReceive().GetResourceStream(Arg.Any<string>());
    }

    [Theory]
    [InlineData("")]
    [InlineData("../Atlas AS7-D")]
    [InlineData("Atlas/AS7-D")]
    public async Task GetMechArtworkAsync_RejectsInvalidUnitName(string name)
    {
        var provider = Substitute.For<IResourceStreamProvider>();
        var sut = new RecordSheetArtworkProvider([provider]);

        var result = await sut.GetMechArtworkAsync(name);

        result.ShouldBeNull();
        await provider.DidNotReceive().GetAvailableResourceIds();
    }
}
