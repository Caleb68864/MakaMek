using NSubstitute;
using Sanet.MakaMek.Assets.Configuration;
using Sanet.MakaMek.Assets.ResourceProviders;
using Sanet.MakaMek.Assets.Services;
using Shouldly;

namespace MakaMek.Assets.Tests.Services;

public class ConfiguredResourceProvidersTests
{
    [Fact]
    public async Task GetAsync_ResolvesEveryAssetTypeInOrder()
    {
        var configuration = Substitute.For<IAssetProviderConfigurationProvider>();
        configuration.GetActiveProviders(AssetType.RecordSheetTemplates).Returns([Config("a", AssetType.RecordSheetTemplates)]);
        configuration.GetActiveProviders(AssetType.RecordSheetPips).Returns([Config("b", AssetType.RecordSheetPips)]);
        var factory = Substitute.For<IResourceStreamProviderFactory>();
        factory.Create(Arg.Any<AssetProviderConfigData>())
            .Returns(call => Provider(call.Arg<AssetProviderConfigData>().Id));
        var sut = new ConfiguredResourceProviders(configuration, factory,
            AssetType.RecordSheetTemplates, AssetType.RecordSheetPips);

        var providers = await sut.GetAsync();

        providers.Select(p => p.Id).ShouldBe(["a", "b"], "asset types resolve in the order given");
    }

    [Fact]
    public async Task GetAsync_ReadsConfigurationOnce_WhenItYieldedProviders()
    {
        var configuration = Substitute.For<IAssetProviderConfigurationProvider>();
        configuration.GetActiveProviders(AssetType.UnitFluff).Returns([Config("a", AssetType.UnitFluff)]);
        var resolved = Provider("a");
        var factory = Substitute.For<IResourceStreamProviderFactory>();
        factory.Create(Arg.Any<AssetProviderConfigData>()).Returns(resolved);
        var sut = new ConfiguredResourceProviders(configuration, factory, AssetType.UnitFluff);

        await sut.GetAsync();
        await sut.GetAsync();
        await sut.GetAsync();

        await configuration.Received(1).GetActiveProviders(AssetType.UnitFluff);
    }

    [Fact]
    public async Task GetAsync_TriesAgain_WhenNothingWasConfiguredYet()
    {
        // A provider added in Settings after the first lookup should be picked up without a restart,
        // so an empty result is not cached as the answer.
        var configuration = Substitute.For<IAssetProviderConfigurationProvider>();
        var reads = 0;
        configuration.GetActiveProviders(AssetType.UnitFluff).Returns(_ => ++reads == 1
            ? Task.FromResult<IReadOnlyList<AssetProviderConfigData>>([])
            : Task.FromResult<IReadOnlyList<AssetProviderConfigData>>([Config("late", AssetType.UnitFluff)]));
        var late = Provider("late");
        var factory = Substitute.For<IResourceStreamProviderFactory>();
        factory.Create(Arg.Any<AssetProviderConfigData>()).Returns(late);
        var sut = new ConfiguredResourceProviders(configuration, factory, AssetType.UnitFluff);

        (await sut.GetAsync()).ShouldBeEmpty();
        (await sut.GetAsync()).Select(p => p.Id).ShouldBe(["late"]);
    }

    [Fact]
    public async Task GetAsync_ResolvesOnce_UnderConcurrentCallers()
    {
        var gate = new TaskCompletionSource();
        var configuration = Substitute.For<IAssetProviderConfigurationProvider>();
        configuration.GetActiveProviders(AssetType.UnitFluff).Returns(_ => GatedAsync());

        async Task<IReadOnlyList<AssetProviderConfigData>> GatedAsync()
        {
            await gate.Task;
            return [Config("a", AssetType.UnitFluff)];
        }

        var resolved = Provider("a");
        var factory = Substitute.For<IResourceStreamProviderFactory>();
        factory.Create(Arg.Any<AssetProviderConfigData>()).Returns(resolved);
        var sut = new ConfiguredResourceProviders(configuration, factory, AssetType.UnitFluff);

        var callers = Enumerable.Range(0, 8).Select(_ => sut.GetAsync()).ToArray();
        gate.SetResult();
        var results = await Task.WhenAll(callers);

        await configuration.Received(1).GetActiveProviders(AssetType.UnitFluff);
        results.ShouldAllBe(r => r.Count == 1);
    }

    private static AssetProviderConfigData Config(string id, AssetType assetType) =>
        new(id, ProviderType.GitHub, assetType, "https://example.test", IsActive: true,
            IsDefault: true, SortOrder: 0);

    private static IResourceStreamProvider Provider(string id)
    {
        var provider = Substitute.For<IResourceStreamProvider>();
        provider.Id.Returns(id);
        return provider;
    }
}
