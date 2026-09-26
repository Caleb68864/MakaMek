using System.Text;
using NSubstitute;
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
