using System.Reactive.Concurrency;
using global::Avalonia.Threading;
using Sanet.MakaMek.Services;

namespace MakaMek.Avalonia.AppTests;

/// <summary>
/// A dispatcher service whose scheduler belongs to this instance rather than to the process.
///
/// The application's <c>AvaloniaDispatcherService</c> returns the static
/// <c>AvaloniaScheduler.Instance</c>, which binds to whichever dispatcher first uses it. Headless
/// tests isolate the application per test, so the second test to construct a view model subscribes
/// through a scheduler bound to a dispatcher that is already gone, and receives nothing at all.
/// That failure is silent: a view model with no commands looks exactly like a game that published
/// none.
///
/// The scheduler here is a real <see cref="EventLoopScheduler"/>, so delivery stays genuinely
/// deferred. That matters: the defect this harness found was caused by deferred delivery, and an
/// immediate scheduler would have hidden it.
/// </summary>
internal sealed class TestDispatcherService : IDispatcherService, IDisposable
{
    private readonly EventLoopScheduler _scheduler = new();

    public IScheduler Scheduler => _scheduler;

    public void RunOnUIThread(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess()) action();
        else Dispatcher.UIThread.Post(action);
    }

    public async Task InvokeOnUIThread(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess()) action();
        else await Dispatcher.UIThread.InvokeAsync(action);
    }

    public void RunOnUIThread<TResult>(Func<TResult> callback)
    {
        if (Dispatcher.UIThread.CheckAccess()) callback();
        else Dispatcher.UIThread.InvokeAsync(callback);
    }

    public void Dispose() => _scheduler.Dispose();
}
