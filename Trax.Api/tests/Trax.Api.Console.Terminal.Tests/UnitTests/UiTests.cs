using AwesomeAssertions;
using Terminal.Gui.Views;
using Trax.Api.Console.Terminal.Screens;
using Trax.Api.Console.Terminal.Tests.Fakes;
using Trax.Client;

namespace Trax.Api.Console.Terminal.Tests.UnitTests;

/// <summary>
/// Work a screen starts off the UI thread: whatever it throws is shown rather than lost, and what follows a bulk
/// operation stops when the window closes.
/// </summary>
[TestFixture]
[NonParallelizable]
public class UiTests
{
    [Test]
    public async Task Any_exception_the_work_throws_is_reported()
    {
        var (terminal, _) = await TerminalHarness.StartAsync(_ => new Window());
        await using var __ = terminal;
        string? failed = null;

        await terminal.OnUi(() =>
            Ui.Start<int>(
                terminal.App,
                () => throw new FormatException("the answer did not parse"),
                _ => { },
                m => failed = m
            )
        );

        await terminal.Until(() => failed is not null, "the failure is reported");
        failed.Should().Be("FormatException: the answer did not parse");
    }

    [Test]
    public async Task A_cancellation_the_caller_asked_for_is_not_reported()
    {
        var (terminal, _) = await TerminalHarness.StartAsync(_ => new Window());
        await using var __ = terminal;
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        var reported = new List<string>();
        var later = false;

        await terminal.OnUi(() =>
            Ui.Start<int>(
                terminal.App,
                () => Task.FromCanceled<int>(cancel.Token),
                _ => reported.Add("done"),
                m => reported.Add(m),
                cancel.Token
            )
        );
        await terminal.OnUi(() =>
            Ui.Start(terminal.App, () => Task.FromResult(1), _ => later = true, _ => { })
        );
        await terminal.Until(() => later, "a later start finishes");
        await Task.Delay(100);
        await terminal.OnUi(() => { });

        reported.Should().BeEmpty();
    }

    [Test]
    public async Task Following_a_bulk_operation_stops_when_the_window_closes()
    {
        BulkProgress.Every = TimeSpan.FromMilliseconds(50);
        try
        {
            var host = new FakeHost().Answer(
                "BulkOperation",
                """
                {"operations":{"bulkOperation":{"id":"6f0b2c1e-0000-4000-8000-000000000001","kind":"CANCEL","status":"RUNNING",
                 "actor":"op","askAfresh":false,"startedAt":"2026-10-22T10:00:00Z","total":2,"done":0,"failed":0,"message":"",
                 "filter":{},"failureReasons":[],"failures":[]}}}
                """
            );
            ConsoleContext context = null!;
            var (terminal, _) = await TerminalHarness.StartAsync(app =>
            {
                context = new ConsoleContext(
                    app,
                    new TraxClient(host),
                    new Uri("https://ops.example.com/trax/graphql")
                );
                return new Window();
            });
            await using var __ = terminal;

            BulkProgress.Follow(context, Guid.NewGuid(), () => { });
            await terminal.Until(
                () => host.Sent("BulkOperation").Count >= 2,
                "the operation is followed"
            );
            context.Close();
            await Task.Delay(200);
            var after = host.Sent("BulkOperation").Count;
            await Task.Delay(300);

            host.Sent("BulkOperation")
                .Count.Should()
                .Be(after, "nothing reads it once the window closed");
        }
        finally
        {
            BulkProgress.Every = TimeSpan.FromSeconds(1);
        }
    }
}
