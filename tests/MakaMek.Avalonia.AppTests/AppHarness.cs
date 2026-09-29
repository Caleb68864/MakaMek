using global::Avalonia;
using global::Avalonia.Headless;
using Microsoft.Extensions.DependencyInjection;
using Sanet.MakaMek.Avalonia;
using Sanet.MakaMek.Avalonia.Desktop.DependencyInjection;
using Sanet.MVVM.DI.Avalonia.Extensions;

[assembly: AvaloniaTestApplication(typeof(MakaMek.Avalonia.AppTests.AppHarness))]

namespace MakaMek.Avalonia.AppTests;

/// <summary>
/// Boots the real <see cref="App"/> headlessly, with the real desktop service graph.
///
/// This lives in its own test project on purpose. AvaloniaTestApplication is assembly-wide and
/// MakaMek.Avalonia.Tests already claims it for a stub TestApp, and only one headless platform can
/// exist per assembly - so the real application cannot be booted alongside those tests.
///
/// Why bother: every existing view test constructs a view directly (new BattleMapView()), which
/// never exercises navigation, DI, or App startup. A defect that only appears when the assembled
/// application runs - a banner queued in the wrong order, a binding that silently resolves to
/// nothing - is invisible to all of them.
/// </summary>
public static class AppHarness
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UseDependencyInjection(services => services.RegisterDesktopServices())
            .WithInterFont()
            .UseSkia()
            // Real drawing, so CaptureRenderedFrame returns pixels rather than an empty surface.
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
