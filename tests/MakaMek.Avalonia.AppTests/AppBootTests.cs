using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Media.Imaging;
using global::Avalonia.Headless;
using global::Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using Sanet.MakaMek.Avalonia;
using Shouldly;

namespace MakaMek.Avalonia.AppTests;

/// <summary>First question: does the assembled application start at all headlessly?</summary>
public class AppBootTests
{
    [Fact]
    public Task App_Boots_AndBuildsItsServiceProvider() => Run(() =>
    {
        var app = Application.Current as App;
        app.ShouldNotBeNull("the headless platform should have started the real App");
        app.ServiceProvider.ShouldNotBeNull("OnFrameworkInitializationCompleted builds the graph");
    });

    [Fact]
    public Task MainWindow_Shows_AndRenders() => Run(() =>
    {
        var window = new Sanet.MakaMek.Avalonia.Views.MainWindow();
        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();

        using var frame = window.CaptureRenderedFrame();
        frame.ShouldNotBeNull("a real window with real drawing should produce a frame");

        // "Not null" is not evidence of rendering: the headless no-op backend also returns a
        // surface, just an empty one. Assert the pixels actually vary, which is what distinguishes
        // a drawn frame from a blank one.
        DistinctPixelCount(frame).ShouldBeGreaterThan(1,
            "a blank frame means UseHeadlessDrawing was left on and nothing was really drawn");
    });


    /// <summary>How many distinct pixel values a captured frame contains.</summary>
    private static int DistinctPixelCount(WriteableBitmap frame)
    {
        using var buffer = frame.Lock();
        var pixels = new byte[buffer.RowBytes * buffer.Size.Height];
        System.Runtime.InteropServices.Marshal.Copy(buffer.Address, pixels, 0, pixels.Length);
        var seen = new HashSet<uint>();
        for (var i = 0; i + 3 < pixels.Length; i += 4)
            seen.Add(BitConverter.ToUInt32(pixels, i));
        return seen.Count;
    }

    /// <summary>
    /// Only accepts a synchronous body, and has a Func&lt;Task&gt; sibling below. The pair exists
    /// because HeadlessUnitTestSession.Dispatch has no Func&lt;Task&gt; overload: an async lambda
    /// binds to Action, the returned task is dropped, and every assertion inside it is discarded
    /// while the test still reports green. That cost this repo 23 silently passing tests once.
    /// </summary>
    private static Task Run(Action body)
    {
        var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(AppBootTests).Assembly);
        return session.Dispatch(body, CancellationToken.None);
    }

    private static Task Run(Func<Task> body)
    {
        var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(AppBootTests).Assembly);
        return session.Dispatch(body, CancellationToken.None);
    }
}
