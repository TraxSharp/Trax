using System.Diagnostics;
using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.Testing;
using Terminal.Gui.Time;
using Terminal.Gui.Views;

namespace Trax.Api.Console.Terminal.Tests.Fakes;

/// <summary>
/// Runs a window in a headless terminal on a thread of its own, with a clock the test moves, and lets the test act
/// on the UI thread and read the screen. Every wait is on a condition the UI thread checks, with a deadline.
/// </summary>
internal sealed class TerminalHarness : IAsyncDisposable
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(15);

    private readonly Thread _loop;

    private TerminalHarness(IApplication app, VirtualTimeProvider time, Thread loop, Task stopped)
    {
        App = app;
        Time = time;
        _loop = loop;
        Stopped = stopped;
    }

    /// <summary>Completes when the window has stopped running, by itself or by the test.</summary>
    public Task Stopped { get; }

    public IApplication App { get; }

    public VirtualTimeProvider Time { get; }

    /// <summary>Starts <paramref name="build"/>'s window on a 140 by 40 terminal and waits until it runs.</summary>
    public static async Task<(TerminalHarness Harness, T Window)> StartAsync<T>(
        Func<IApplication, T> build
    )
        where T : Runnable
    {
        var time = new VirtualTimeProvider();
        var app = Application.Create(time);
        var started = new TaskCompletionSource<T>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        // Init and Run on one thread: Terminal.Gui takes the thread that initialises it for its main thread, and
        // runs on the spot whatever is invoked from that thread.
        var loop = new Thread(() =>
        {
            T window;
            try
            {
                app.Init("ansi");
                app.Driver!.SetScreenSize(140, 40);
                window = build(app);
            }
            catch (Exception ex)
            {
                started.TrySetException(ex);
                return;
            }
            window.IsRunningChanged += (_, e) =>
            {
                if (e.Value)
                    started.TrySetResult(window);
            };
            app.Run(window);
            stopped.TrySetResult();
            window.Dispose();
        })
        {
            IsBackground = true,
            Name = "terminal-harness",
        };
        loop.Start();
        var window = await started.Task.WaitAsync(Deadline);
        return (new TerminalHarness(app, time, loop, stopped.Task), window);
    }

    /// <summary>Runs <paramref name="action"/> on the UI thread and waits for it.</summary>
    public Task OnUi(Action action) =>
        OnUi(() =>
        {
            action();
            return true;
        });

    /// <summary>Reads <paramref name="read"/> on the UI thread.</summary>
    public async Task<T> OnUi<T>(Func<T> read)
    {
        var done = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        App.Invoke(() =>
        {
            try
            {
                done.TrySetResult(read());
            }
            catch (Exception ex)
            {
                done.TrySetException(ex);
            }
        });
        return await done.Task.WaitAsync(Deadline);
    }

    /// <summary>Presses <paramref name="key"/> and waits until the UI thread has handled it.</summary>
    public Task Press(Key key) => OnUi(() => App.InjectKey(key));

    /// <summary>The screen as text, one line per row, trailing spaces trimmed.</summary>
    public Task<string> Screen() =>
        OnUi(() =>
        {
            App.LayoutAndDraw(true);
            var driver = App.Driver!;
            var cells = driver.Contents!;
            return string.Join(
                "\n",
                Enumerable
                    .Range(0, driver.Rows)
                    .Select(r =>
                        string.Concat(
                                Enumerable
                                    .Range(0, driver.Cols)
                                    .Select(c =>
                                        cells[r, c].Grapheme is { Length: > 0 } g ? g : " "
                                    )
                            )
                            .TrimEnd()
                    )
            );
        });

    /// <summary>Waits until <paramref name="condition"/>, checked on the UI thread, holds.</summary>
    public async Task Until(Func<bool> condition, string because)
    {
        var clock = Stopwatch.StartNew();
        while (!await OnUi(condition))
            if (clock.Elapsed > Deadline)
                Assert.Fail($"Timed out waiting until {because}.");
    }

    /// <summary>Waits until the screen shows <paramref name="text"/>.</summary>
    public async Task UntilShown(string text)
    {
        var clock = Stopwatch.StartNew();
        while (!(await Screen()).Contains(text, StringComparison.Ordinal))
            if (clock.Elapsed > Deadline)
                Assert.Fail($"Timed out waiting for '{text}' on the screen:\n{await Screen()}");
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (!Stopped.IsCompleted)
                await OnUi(() => App.RequestStop());
        }
        catch (TimeoutException)
        {
            // The loop is gone already.
        }
        _loop.Join(Deadline);
        App.Dispose();
    }
}
