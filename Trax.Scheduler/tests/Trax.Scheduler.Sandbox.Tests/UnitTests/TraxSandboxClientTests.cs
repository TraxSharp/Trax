using System.Formats.Tar;
using System.Net;
using System.Text.Json;
using Trax.Core.Exceptions;
using Trax.Scheduler.Sandbox.Image;
using Trax.Scheduler.Sandbox.Tests.Fakes;
using Trax.Scheduler.Services.RunExecutor;
using Trax.Scheduler.Services.Sandbox;

namespace Trax.Scheduler.Sandbox.Tests.UnitTests;

/// <summary>
/// The hub's side: what it sends is the train's declaration and nothing else, and it reads only
/// an answer the launcher signed for that request.
///
/// <para>Enforces <c>docs/adr/0020-a-sandbox-is-started-only-by-the-launcher-from-a-fixed-request.md</c>.</para>
/// </summary>
[Property("adr", "docs/adr/0020-a-sandbox-is-started-only-by-the-launcher-from-a-fixed-request.md")]
[TestFixture]
public class TraxSandboxClientTests
{
    private const string Adr =
        "docs/adr/0020-a-sandbox-is-started-only-by-the-launcher-from-a-fixed-request.md";

    private static readonly HostileInput Echo = new("echo", ["hi"]);

    private static async Task<SandboxRun<HostileOutput>> RunAsync(FakeLauncher launcher)
    {
        var hub = SandboxHosts.Trains();
        await using (hub.ConfigureAwait(false))
            return await SandboxHosts
                .Client(hub, launcher)
                .RunAsync<IHostileTrain, HostileInput, HostileOutput>(Echo)
                .ConfigureAwait(false);
    }

    private static string Result(object output) =>
        JsonSerializer.Serialize(
            new RemoteRunResponse(
                0,
                OutputJson: JsonSerializer.Serialize(
                    output,
                    Trax.Effect.Utils.TraxJsonSerializationOptions.ManifestProperties
                ),
                OutputType: output.GetType().FullName
            ),
            RemoteRunJson.Write
        );

    [Test]
    public async Task RunAsync_SendsOnlyTheDeclaredProfileLimitsTrainAndInput()
    {
        var launcher = new FakeLauncher(SandboxHosts.Key);

        await RunAsync(launcher);

        using var sent = JsonDocument.Parse(launcher.RequestBodies.Single());
        sent.RootElement.EnumerateObject()
            .Select(p => p.Name)
            .Should()
            .BeEquivalentTo(["runId", "profile", "trainName", "inputJson", "limits"], Adr);
        sent.RootElement.GetProperty("profile").GetString().Should().Be("escape");
        sent.RootElement.GetProperty("trainName")
            .GetString()
            .Should()
            .Be(typeof(IHostileTrain).FullName);
        sent.RootElement.GetProperty("runId").GetString().Should().MatchRegex("^[0-9a-f]{32}$");
        var limits = sent.RootElement.GetProperty("limits");
        limits.GetProperty("cpuMillicores").GetInt32().Should().Be(500);
        limits.GetProperty("memoryMiB").GetInt32().Should().Be(192);
        limits.GetProperty("diskMiB").GetInt32().Should().Be(8);
        limits.GetProperty("timeoutSeconds").GetInt32().Should().Be(30);
    }

    [Test]
    public async Task RunAsync_Completed_ReturnsTheOutputAndTheFiles()
    {
        var tar = await SandboxArchive.WriteAsync(
            [new SandboxFile("echo.txt", "hi"u8.ToArray())],
            CancellationToken.None
        );
        var launcher = new FakeLauncher(SandboxHosts.Key)
        {
            Answer = r => new SandboxRunResponse(
                r.RunId,
                SandboxOutcome.Completed,
                ResultJson: Result(new HostileOutput([], [], "hi")),
                Archive: Convert.ToBase64String(tar)
            ),
        };

        var run = await RunAsync(launcher);

        run.Output.Should().NotBeNull();
        run.Output?.Echo.Should().Be("hi");
        run.Files.Should().ContainSingle(f => f.Path == "echo.txt");
    }

    [TestCase(SandboxOutcome.Refused)]
    [TestCase(SandboxOutcome.Busy)]
    [TestCase(SandboxOutcome.TimedOut)]
    [TestCase(SandboxOutcome.OutOfMemory)]
    [TestCase(SandboxOutcome.Exited)]
    [TestCase(SandboxOutcome.OutputRefused)]
    [TestCase(SandboxOutcome.Failed)]
    public async Task RunAsync_NotCompleted_ThrowsWithTheOutcomeAndTheReason(SandboxOutcome outcome)
    {
        var launcher = new FakeLauncher(SandboxHosts.Key)
        {
            Answer = r => new SandboxRunResponse(r.RunId, outcome, "because"),
        };

        var thrown = (
            await FluentActions
                .Awaiting(() => RunAsync(launcher))
                .Should()
                .ThrowAsync<SandboxRunException>()
        ).Which;

        thrown.Outcome.Should().Be(outcome);
        thrown
            .Message.Should()
            .Contain(outcome.ToString())
            .And.Contain("because")
            .And.Contain(typeof(IHostileTrain).FullName);
    }

    [Test]
    public async Task RunAsync_RefusalNamingNoRun_StillReportsTheRefusal()
    {
        var launcher = new FakeLauncher(SandboxHosts.Key)
        {
            Answer = _ => new SandboxRunResponse("", SandboxOutcome.Refused, "shape"),
            Status = HttpStatusCode.BadRequest,
        };

        (
            await FluentActions
                .Awaiting(() => RunAsync(launcher))
                .Should()
                .ThrowAsync<SandboxRunException>()
        )
            .Which.Outcome.Should()
            .Be(SandboxOutcome.Refused);
    }

    [Test]
    public async Task RunAsync_TrainFailedInTheSandbox_ThrowsItsFailure()
    {
        var result = JsonSerializer.Serialize(
            new RemoteRunResponse(
                0,
                IsError: true,
                ErrorMessage: "bad input",
                ExceptionType: nameof(TrainException)
            )
            {
                PublicMessage = "bad input",
            },
            RemoteRunJson.Write
        );
        var launcher = new FakeLauncher(SandboxHosts.Key)
        {
            Answer = r => new SandboxRunResponse(
                r.RunId,
                SandboxOutcome.Completed,
                ResultJson: result
            ),
        };

        var thrown = (
            await FluentActions
                .Awaiting(() => RunAsync(launcher))
                .Should()
                .ThrowAsync<RemoteRunException>()
        ).Which;

        thrown.PublicMessage.Should().Be("bad input");
    }

    [Test]
    public async Task RunAsync_UnsignedAnswer_IsNotRead()
    {
        var launcher = new FakeLauncher(SandboxHosts.Key)
        {
            Unsigned = true,
            Answer = r => new SandboxRunResponse(
                r.RunId,
                SandboxOutcome.Completed,
                ResultJson: Result(new HostileOutput(["forged"], [], ""))
            ),
        };

        var thrown = (
            await FluentActions
                .Awaiting(() => RunAsync(launcher))
                .Should()
                .ThrowAsync<SandboxRunException>()
        ).Which;

        thrown.Outcome.Should().Be(SandboxOutcome.Failed);
        thrown.Message.Should().Contain("not signed");
    }

    [Test]
    public async Task RunAsync_AnswerSignedWithAnotherKey_IsNotRead()
    {
        var launcher = new FakeLauncher(SandboxHosts.Key)
        {
            AnswerKey = Enumerable.Repeat((byte)9, 32).ToArray(),
        };

        (
            await FluentActions
                .Awaiting(() => RunAsync(launcher))
                .Should()
                .ThrowAsync<SandboxRunException>()
        )
            .Which.Message.Should()
            .Contain("not signed");
    }

    [Test]
    public async Task RunAsync_AnswerSignedForAnotherRequest_IsNotRead()
    {
        var launcher = new FakeLauncher(SandboxHosts.Key) { SignForAnotherRequest = true };

        (
            await FluentActions
                .Awaiting(() => RunAsync(launcher))
                .Should()
                .ThrowAsync<SandboxRunException>()
        )
            .Which.Message.Should()
            .Contain("not signed");
    }

    [Test]
    public async Task RunAsync_CompletedForAnotherRun_IsRefused()
    {
        var launcher = new FakeLauncher(SandboxHosts.Key)
        {
            Answer = _ => new SandboxRunResponse(
                "0123456789abcdef0123456789abcdef",
                SandboxOutcome.Completed,
                ResultJson: "{}"
            ),
        };

        (
            await FluentActions
                .Awaiting(() => RunAsync(launcher))
                .Should()
                .ThrowAsync<SandboxRunException>()
        )
            .Which.Message.Should()
            .Contain("another run");
    }

    [Test]
    public async Task RunAsync_ArchiveWithALink_IsRefusedByTheHubToo()
    {
        // A launcher bug that passed a link on: the hub reads the archive with the same checks.
        await using var tar = await new TarBuilder()
            .Entry(TarEntryType.SymbolicLink, "link", "/etc/shadow")
            .BuildAsync();
        var launcher = new FakeLauncher(SandboxHosts.Key)
        {
            Answer = r => new SandboxRunResponse(
                r.RunId,
                SandboxOutcome.Completed,
                ResultJson: Result(new HostileOutput([], [], "")),
                Archive: Convert.ToBase64String(tar.ToArray())
            ),
        };

        (
            await FluentActions
                .Awaiting(() => RunAsync(launcher))
                .Should()
                .ThrowAsync<SandboxRunException>()
        )
            .Which.Outcome.Should()
            .Be(SandboxOutcome.OutputRefused);
    }

    [Test]
    public async Task RunAsync_ResultThatIsNotARunResponse_FailsAsExited()
    {
        var launcher = new FakeLauncher(SandboxHosts.Key)
        {
            Answer = r => new SandboxRunResponse(
                r.RunId,
                SandboxOutcome.Completed,
                ResultJson: "[1,2]"
            ),
        };

        (
            await FluentActions
                .Awaiting(() => RunAsync(launcher))
                .Should()
                .ThrowAsync<SandboxRunException>()
        )
            .Which.Outcome.Should()
            .Be(SandboxOutcome.Exited);
    }

    [Test]
    public async Task RunAsync_AnswerLargerThanTheCap_IsNotRead()
    {
        var launcher = new FakeLauncher(SandboxHosts.Key) { RawBody = new byte[2048] };
        await using var hub = SandboxHosts.Trains();
        var options = SandboxHosts.Options();
        options.MaxResponseBodyBytes = 1024;

        var thrown = (
            await FluentActions
                .Awaiting(() =>
                    SandboxHosts
                        .Client(hub, launcher, options)
                        .RunAsync<IHostileTrain, HostileInput, HostileOutput>(Echo)
                )
                .Should()
                .ThrowAsync<SandboxRunException>()
        ).Which;

        thrown.Message.Should().Contain("larger than 1024 bytes");
    }

    [Test]
    public async Task RunAsync_AnswerThatIsNotTheShape_IsRefused()
    {
        var launcher = new FakeLauncher(SandboxHosts.Key)
        {
            RawBody = "{\"runId\":\"x\",\"outcome\":\"Completed\",\"extra\":1}"u8.ToArray(),
        };

        (
            await FluentActions
                .Awaiting(() => RunAsync(launcher))
                .Should()
                .ThrowAsync<SandboxRunException>()
        )
            .Which.Message.Should()
            .Contain("not a sandbox run response");
    }

    [Test]
    public async Task RunAsync_LauncherUnreachable_FailsWithTheReason()
    {
        var launcher = new FakeLauncher(SandboxHosts.Key)
        {
            Throw = new HttpRequestException("connection refused"),
        };

        (
            await FluentActions
                .Awaiting(() => RunAsync(launcher))
                .Should()
                .ThrowAsync<SandboxRunException>()
        )
            .Which.Message.Should()
            .Contain("could not be reached");
    }

    [Test]
    public async Task RunAsync_TrainWithoutTheAttribute_IsRefusedBeforeSending()
    {
        var launcher = new FakeLauncher(SandboxHosts.Key);
        await using var hub = SandboxHosts.Trains();

        await FluentActions
            .Awaiting(() =>
                SandboxHosts
                    .Client(hub, launcher)
                    .RunAsync<IUnsandboxedTrain, HostileInput, HostileOutput>(Echo)
            )
            .Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*does not carry [TraxSandbox]*");
        launcher.RequestBodies.Should().BeEmpty();
    }
}
