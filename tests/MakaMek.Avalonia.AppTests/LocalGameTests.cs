using Microsoft.Extensions.DependencyInjection;
using global::Avalonia;
using global::Avalonia.Headless;
using Sanet.MakaMek.Avalonia;
using Shouldly;

namespace MakaMek.Avalonia.AppTests;

/// <summary>Can the harness assemble a real game without a network? Everything else depends on it.</summary>
public class LocalGameTests
{
    [Fact]
    public Task BundledUnits_LoadFromTheRepositoryDataFolder() => Run(async () =>
    {
        var services = ((App)Application.Current!).ServiceProvider!;

        var units = await LocalGameFixture.LoadBundledUnitsAsync(services);

        units.Count.ShouldBeGreaterThan(0, $"no units loaded from {LocalGameFixture.UnitsFolder()}");
        units.ShouldContain(unit => !string.IsNullOrWhiteSpace(unit.Chassis));
    });

    /// <summary>
    /// Runs async work on the headless dispatcher.
    ///
    /// The body is wrapped so that it returns a value, which is what forces the
    /// Func&lt;Task&lt;T&gt;&gt; overload of Dispatch. Passing a Func&lt;Task&gt; - even a typed
    /// variable, not just an inline async lambda - binds to the Action overload instead, making it
    /// async void: the task is dropped and every assertion failure inside is swallowed while the
    /// test still reports green. Verified by mutation: an impossible assertion passed until this
    /// wrapper was added.
    /// </summary>
    private static Task Run(Func<Task> body)
    {
        var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(LocalGameTests).Assembly);
        return session.Dispatch(async () =>
        {
            await body();
            return true;
        }, CancellationToken.None);
    }
}
