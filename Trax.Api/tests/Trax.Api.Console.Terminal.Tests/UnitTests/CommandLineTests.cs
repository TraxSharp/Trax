using System.Text.Json.Nodes;
using AwesomeAssertions;
using Trax.Api.Console.Terminal.Connection;
using Trax.Api.Console.Terminal.Lists;
using Trax.Api.Console.Terminal.Operations;
using Trax.Api.Console.Terminal.Tests.Fakes;
using Trax.Client;

namespace Trax.Api.Console.Terminal.Tests.UnitTests;

/// <summary>
/// What trax-console exits with: 0 when the command did what it said, 1 when the host refused, could not be reached or
/// runs another release, 2 when the command line is wrong. --any-version passes another release and nothing else, and
/// a host that moved to another release mid-session is named, with its install line, wherever its refusal is shown.
/// </summary>
[TestFixture]
public class CommandLineTests
{
    private const string Other = "9.9.9";

    [Test]
    public async Task A_host_of_this_release_runs_the_command()
    {
        var (exit, output, error) = await Hosts.RunAsync(Hosts.Same(), "list", "runs");

        exit.Should().Be(0, error);
        output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Should().HaveCount(2);
    }

    [Test]
    public async Task A_host_of_another_release_is_refused_with_its_install_line()
    {
        var (exit, output, error) = await Hosts.RunAsync(
            Hosts.Same().Answer("ServerVersion", Hosts.Version(Other)),
            "list",
            "runs"
        );

        exit.Should().Be(ConsoleCommandLine.Refused);
        output.Should().BeEmpty();
        error
            .Should()
            .Contain($"dotnet tool update -g Trax.Api.Console.Terminal --version {Other}");
    }

    [Test]
    public async Task Any_version_passes_a_host_of_another_release()
    {
        var (exit, _, error) = await Hosts.RunAsync(
            Hosts.Same().Answer("ServerVersion", Hosts.Version(Other)),
            "list",
            "runs",
            "--any-version"
        );

        exit.Should().Be(0, error);
    }

    [Test]
    public async Task Any_version_does_not_pass_a_check_that_failed()
    {
        var host = new RefusingHost(Hosts.Same()).Refuse(
            "ServerVersion",
            "The API key is not valid.",
            "AUTH_NOT_AUTHENTICATED"
        );

        var (exit, output, error) = await Hosts.RunAsync(host, "list", "runs", "--any-version");

        exit.Should().Be(ConsoleCommandLine.Refused);
        output.Should().BeEmpty();
        error.Should().Contain("AUTH_NOT_AUTHENTICATED: The API key is not valid.");
    }

    [TestCase("list", "nosuch")]
    [TestCase("list", "runs", "--where", "colour=red")]
    [TestCase("list", "runs", "--take", "501")]
    [TestCase("do", "dead-letters", "acknowledge", "7")]
    public async Task A_wrong_command_line_exits_2(params string[] args)
    {
        var (exit, _, error) = await Hosts.RunAsync(Hosts.Same(), args);

        exit.Should().Be(ConsoleCommandLine.Usage);
        error.Should().NotBeEmpty();
    }

    [Test]
    public async Task No_host_exits_2()
    {
        var error = new StringWriter();

        var exit = await ConsoleCommandLine
            .Create(_ => null, _ => throw new AssertionException("nothing to open"), null, error)
            .Parse(["list", "runs"])
            .InvokeAsync();

        exit.Should().Be(ConsoleCommandLine.Usage);
        error.ToString().Should().Contain(ConsoleTarget.HostVariable);
    }

    [Test]
    public async Task Ctrl_C_while_the_host_does_not_answer_ends_the_check()
    {
        using var interrupt = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        var (exit, _, _) = await Hosts
            .RunAsync(new SilentHost(), interrupt.Token, "list", "runs")
            .WaitAsync(TimeSpan.FromSeconds(10));

        exit.Should().Be(ConsoleCommandLine.Interrupted);
    }

    [Test]
    public async Task Check_says_why_it_failed_on_standard_output()
    {
        var host = new RefusingHost(Hosts.Same()).Refuse(
            "ServerVersion",
            "No.",
            "AUTH_NOT_AUTHORIZED"
        );

        var (exit, output, _) = await Hosts.RunAsync(host, "check");

        exit.Should().Be(ConsoleCommandLine.Refused);
        var line = JsonNode.Parse(output)!;
        line["serves"]!.GetValue<bool>().Should().BeFalse();
        line["code"]!.GetValue<string>().Should().Be("AUTH_NOT_AUTHORIZED");
    }

    [Test]
    public async Task Check_names_the_release_to_install()
    {
        var (exit, output, _) = await Hosts.RunAsync(
            Hosts.Same().Answer("ServerVersion", Hosts.Version(Other)),
            "check"
        );

        exit.Should().Be(ConsoleCommandLine.Refused);
        var line = JsonNode.Parse(output)!;
        line["hostVersion"]!.GetValue<string>().Should().Be(Other);
        line["message"]!.GetValue<string>().Should().Contain($"--version {Other}");
    }

    [Test]
    public async Task Check_of_this_release_exits_0()
    {
        var (exit, output, _) = await Hosts.RunAsync(Hosts.Same(), "check");

        exit.Should().Be(0);
        JsonNode.Parse(output)!["serves"]!.GetValue<bool>().Should().BeTrue();
    }

    [Test]
    public async Task A_host_that_moves_to_another_release_mid_session_is_named_in_a_headless_refusal()
    {
        var (exit, _, error) = await Hosts.RunAsync(Deployed(), "list", "runs");

        exit.Should().Be(ConsoleCommandLine.Refused);
        var refusal = JsonNode.Parse(error)!;
        refusal["code"]!.GetValue<string>().Should().Be(OutdatedTransport.Code);
        refusal["error"]!
            .GetValue<string>()
            .Should()
            .StartWith("CLIENT_OUTDATED: ")
            .And.Contain($"dotnet tool update -g Trax.Api.Console.Terminal --version {Other}");
    }

    [Test]
    public async Task A_host_that_moves_mid_session_is_named_in_an_actions_outcome_as_the_console_shows_it()
    {
        await using var session = Hosts.Session(Deployed());
        await session.CheckVersionAsync();

        var outcome = await Catalog.DeadLetters.RunAsync(
            session.Client,
            "requeue",
            [7],
            new ActionOptions(),
            CancellationToken.None
        );

        outcome.Succeeded.Should().BeFalse();
        outcome.Code.Should().Be(OutdatedTransport.Code);
        outcome.Message.Should().StartWith("CLIENT_OUTDATED: ").And.Contain($"--version {Other}");
    }

    [Test]
    public async Task A_stream_the_host_ends_outdated_is_named_too()
    {
        var host = new StreamingHost(Hosts.Same());
        await using var session = Hosts.Session(host);
        await session.CheckVersionAsync();
        var ended = new TaskCompletionSource<TraxStreamEvent<OnDataChangedResult>.Ended>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );

        using var stream = session.Client.OnDataChanged(e =>
        {
            if (e is TraxStreamEvent<OnDataChangedResult>.Ended over)
                ended.TrySetResult(over);
        });
        (await host.StreamAsync("OnDataChanged")).Error(
            new SubscriptionErrorException([
                new JsonObject
                {
                    ["message"] = "Unknown document.",
                    ["extensions"] = new JsonObject { ["code"] = OutdatedTransport.Code },
                },
            ])
        );
        var over = await ended.Task.WaitAsync(TimeSpan.FromSeconds(10));

        over.Errors.Should()
            .ContainSingle()
            .Which.Message.Should()
            .StartWith("CLIENT_OUTDATED: Unknown document.")
            .And.Contain("trax-console check");
    }

    // A host of this release at connect that answers every list and action CLIENT_OUTDATED after, as one deployed
    // mid-session does, and says it runs 9.9.9 when asked again.
    private static ITraxTransport Deployed()
    {
        var asked = 0;
        return new RefusingHost(
            Hosts
                .Same()
                .Answer(
                    "ServerVersion",
                    _ =>
                        (JsonObject)
                            JsonNode.Parse(
                                Hosts.Version(
                                    Interlocked.Increment(ref asked) == 1
                                        ? ConsoleSession.ToolVersion
                                        : Other
                                )
                            )!
                )
        )
            .Refuse("Executions", "Unknown document.", OutdatedTransport.Code)
            .Refuse("RequeueDeadLetters", "Unknown document.", OutdatedTransport.Code);
    }
}
