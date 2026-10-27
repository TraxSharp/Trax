using AwesomeAssertions;
using Trax.Api.Console.Terminal.Screens;
using Trax.Api.Console.Terminal.Tests.Fakes;
using Trax.Client;

namespace Trax.Api.Console.Terminal.Tests.UnitTests;

/// <summary>A host newer than the console may send an enum value the console lacks; the list still shows.</summary>
[TestFixture]
[NonParallelizable]
public class NewerHostTests
{
    [Test]
    public async Task A_run_in_a_state_the_console_does_not_know_is_listed_with_it()
    {
        var host = HostData
            .Console()
            .Answer(
                "Executions",
                HostData.Runs(
                    2,
                    null,
                    HostData.Run(4812, "SendInvoice", state: "PAUSED_FOR_REVIEW"),
                    HostData.Run(4813, "ShipOrder")
                )
            );
        var (terminal, _) = await TerminalHarness.StartAsync(app => new MainWindow(
            new ConsoleContext(
                app,
                new TraxClient(host),
                new Uri("https://ops.example.com/trax/graphql"),
                new ScriptedPrompts()
            ),
            state: null,
            banner: "ready"
        ));
        await using var _ = terminal;

        await terminal.UntilShown("ShipOrder");

        var screen = await terminal.Screen();
        screen.Should().Contain("SendInvoice").And.Contain("PAUSED_FOR_REVIEW");
    }
}
