using Avalonia;
using Avalonia.Headless;

[assembly: AvaloniaTestApplication(typeof(MakaMek.Avalonia.Tests.HeadlessTestSetup))]

namespace MakaMek.Avalonia.Tests;

public static class HeadlessTestSetup
{
    // Real drawing rather than the default no-op backend: tests that rasterise a control and
    // compare pixels get identical, undecodable bytes without it, which makes any "these renders
    // differ" assertion pass without testing anything.
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<TestApp>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
