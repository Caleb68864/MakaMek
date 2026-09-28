using Avalonia;
using Avalonia.Headless;

namespace MakaMek.Avalonia.Tests;

/// <summary>
/// Headless setup with real drawing, for tests that rasterise a control and compare the pixels.
/// The default <see cref="AvaloniaHeadlessPlatformOptions"/> uses a no-op drawing backend, so
/// RenderTargetBitmap produces bytes no decoder accepts and any two renders compare equal —
/// which silently makes every "these renders must differ" assertion pass.
/// Kept separate from <see cref="HeadlessTestSetup"/> so the rest of the suite is unaffected.
/// </summary>
public static class SkiaHeadlessTestSetup
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<TestApp>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
