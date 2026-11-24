using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Trax.Core.Exceptions;
using Trax.Effect.Utils;
using Trax.Scheduler.Sandbox.Image;
using Trax.Scheduler.Sandbox.Launcher.Core;
using Trax.Scheduler.Sandbox.Tests.Fakes;
using Trax.Scheduler.Services.RunExecutor;
using Trax.Scheduler.Services.Sandbox;

namespace Trax.Scheduler.Sandbox.Tests.EscapeTests;

/// <summary>
/// The escape suite: hostile trains in real sandboxes under podman, and a hostile hub against the
/// real launcher. Each attempt is blocked or killed, and the run fails with the reason. Runs only
/// where a podman socket and the escape image are provided (see <see cref="PodmanEscape"/>); CI
/// and trax-test have neither, and say so as a skip.
///
/// <para>The proof behind <c>docs/adr/0022-a-sandbox-runs-in-a-kata-microvm-proved-by-a-startup-canary.md</c>,
/// cited there as a file: a suite CI does not run cannot be claimed as its guard.</para>
/// </summary>
[TestFixture]
[NonParallelizable]
public class PodmanEscapeTests
{
    private PodmanEscape? _launcher;
    private Microsoft.Extensions.DependencyInjection.ServiceProvider? _hub;

    private PodmanEscape Launcher =>
        _launcher ?? throw new InvalidOperationException("not started");

    [OneTimeSetUp]
    public async Task StartAsync()
    {
        PodmanEscape.RequireEnvironment();
        _launcher = await PodmanEscape.StartAsync();
        _hub = SandboxHosts.Trains();
    }

    [OneTimeTearDown]
    public async Task StopAsync()
    {
        if (_launcher is not null)
            await _launcher.DisposeAsync();
        if (_hub is not null)
            await _hub.DisposeAsync();
    }

    [TearDown]
    public async Task NothingLeftBehind() =>
        (await Launcher.LeftoversAsync())
            .Should()
            .Be(0, "every container and volume of a run is removed, whatever it did");

    private Task<SandboxRun<HostileOutput>> RunAsync(HostileInput input) =>
        Launcher
            .Client(_hub ?? throw new InvalidOperationException())
            .RunAsync<IHostileTrain, HostileInput, HostileOutput>(input);

    [Test]
    public async Task Echo_RoundTripsItsOutputAndFiles()
    {
        var run = await RunAsync(new HostileInput("echo", ["hello from the sandbox"]));

        run.Output?.Echo.Should().Be("hello from the sandbox");
        run.Files.Select(f => f.Path).Should().BeEquivalentTo("echo.txt", "nested/again.txt");
        Encoding
            .UTF8.GetString(
                run.Files.Single(f =>
                    string.Equals(f.Path, "echo.txt", StringComparison.Ordinal)
                ).Content.Span
            )
            .Should()
            .Be("hello from the sandbox");
    }

    [Test]
    public async Task Network_ReachesNothing()
    {
        // A listener in this process stands in for the hub, the launcher and the database: an
        // address on this machine, open and accepting, that a sandbox must not reach.
        using var hub = new TcpListener(IPAddress.Any, 0);
        hub.Start();
        var port = ((IPEndPoint)hub.LocalEndpoint).Port;
        var here = (await Dns.GetHostAddressesAsync(Dns.GetHostName()))
            .Where(a => a.AddressFamily == AddressFamily.InterNetwork)
            .Select(a =>
                string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{a}:{port}")
            );
        string[] targets =
        [
            .. here,
            string.Create(System.Globalization.CultureInfo.InvariantCulture, $"127.0.0.1:{port}"),
            "169.254.169.254:80",
            "169.254.170.23:80",
            "169.254.170.2:80",
            "[fd00:ec2::254]:80",
            "10.0.2.2:22",
            "10.88.0.1:22",
            "1.1.1.1:443",
            "8.8.8.8:53",
            "kubernetes.default.svc:443",
            "example.com",
            "api.anthropic.com",
        ];

        var run = await RunAsync(new HostileInput("probe", targets));

        run.Output?.Reached.Should()
            .BeEmpty("a sandbox has no network path at all: {0}", string.Join(", ", targets));
    }

    [Test]
    public async Task WriteOutside_WritesNothing()
    {
        string[] targets =
        [
            "/trax-escape",
            "/etc/trax-escape",
            "/usr/bin/trax-escape",
            "/app/trax-escape",
            "/sandbox/trax-escape",
            "/sandbox/in/trax-escape",
            "/sandbox/in/request.json",
            "/var/trax-escape",
            "/run/trax-escape",
        ];

        var run = await RunAsync(new HostileInput("write-outside", targets));

        run.Output?.Written.Should()
            .BeEmpty("the root is read-only and the input is mounted read-only");
    }

    [Test]
    public async Task ForkBomb_IsHeldByThePidsLimitAndKilledAtTheTimeout()
    {
        var thrown = (
            await FluentActions
                .Awaiting(() => RunAsync(new HostileInput("fork-bomb", [])))
                .Should()
                .ThrowAsync<SandboxRunException>()
        ).Which;

        thrown.Outcome.Should().Be(SandboxOutcome.TimedOut);
        thrown.Message.Should().Contain("30 second timeout");
        (await RunAsync(new HostileInput("echo", ["still here"])))
            .Output?.Echo.Should()
            .Be("still here", "the launcher and the host are unaffected");
    }

    [Test]
    public async Task Memory_IsKilledOverItsLimit()
    {
        var thrown = (
            await FluentActions
                .Awaiting(() => RunAsync(new HostileInput("memory", [])))
                .Should()
                .ThrowAsync<SandboxRunException>()
        ).Which;

        thrown.Outcome.Should().Be(SandboxOutcome.OutOfMemory, thrown.Message);
        thrown.Message.Should().Contain("192 MiB");
    }

    [Test]
    public async Task Sleep_IsKilledAtTheTimeout()
    {
        var thrown = (
            await FluentActions
                .Awaiting(() => RunAsync(new HostileInput("sleep", [])))
                .Should()
                .ThrowAsync<SandboxRunException>()
        ).Which;

        thrown.Outcome.Should().Be(SandboxOutcome.TimedOut);
    }

    [Test]
    public async Task FillDisk_StopsAtTheVolumeSizeAndFailsTheRun()
    {
        var thrown = (
            await FluentActions
                .Awaiting(() => RunAsync(new HostileInput("fill-disk", [])))
                .Should()
                .ThrowAsync<TrainException>()
        ).Which;

        thrown
            .Message.Should()
            .MatchRegex(
                "(?i)no space|exited|left no result",
                "the 8 MiB volume fills and the write fails"
            );
    }

    [Test]
    public async Task Symlink_IsRefused()
    {
        var thrown = (
            await FluentActions
                .Awaiting(() => RunAsync(new HostileInput("symlink", ["/etc/shadow"])))
                .Should()
                .ThrowAsync<SandboxRunException>()
        ).Which;

        thrown.Outcome.Should().Be(SandboxOutcome.OutputRefused);
        thrown.Message.Should().Contain("symbolic link");
    }

    [Test]
    public async Task ManyFiles_AreRefused()
    {
        var thrown = (
            await FluentActions
                .Awaiting(() => RunAsync(new HostileInput("many-files", [], Count: 60)))
                .Should()
                .ThrowAsync<SandboxRunException>()
        ).Which;

        thrown.Outcome.Should().Be(SandboxOutcome.OutputRefused);
        thrown.Message.Should().Contain("entries");
    }

    [Test]
    public async Task UndeclaredTrain_IsNotRunInsideTheSandbox()
    {
        var (status, answer) = await PostAsync(Request(typeof(IUnsandboxedTrain).FullName ?? ""));

        status.Should().Be(HttpStatusCode.OK);
        answer.Outcome.Should().Be(SandboxOutcome.Completed);
        var result = JsonSerializer.Deserialize<RemoteRunResponse>(
            answer.ResultJson ?? "",
            RemoteRunJson.Read
        );
        result?.IsError.Should().BeTrue();
        result?.ErrorMessage.Should().Contain("not a registered train marked [TraxSandbox]");
    }

    [TestCase("privileged", "true")]
    [TestCase("image", "\"docker.io/library/alpine:latest\"")]
    [TestCase("command", "[\"/bin/sh\",\"-c\",\"cat /etc/shadow\"]")]
    [TestCase("ociRuntime", "\"crun\"")]
    public async Task HostileHub_ShapingTheSandbox_StartsNoContainer(string member, string value)
    {
        var request =
            JsonNode.Parse(Request(typeof(IHostileTrain).FullName ?? ""))?.AsObject()
            ?? throw new InvalidOperationException();
        request[member] = JsonNode.Parse(value);

        var (status, answer) = await PostAsync(request.ToJsonString());

        status.Should().Be(HttpStatusCode.BadRequest);
        answer.Outcome.Should().Be(SandboxOutcome.Refused);
        (await Launcher.LeftoversAsync()).Should().Be(0);
    }

    [Test]
    public async Task HostileHub_PastTheConcurrencyCap_IsBusy()
    {
        var options = PodmanEscape.DefaultOptions();
        options.Caps.MaxConcurrent = 1;
        await using var capped = await PodmanEscape.StartAsync(options);
        var hub = _hub ?? throw new InvalidOperationException();

        var first = capped
            .Client(hub)
            .RunAsync<IHostileTrain, HostileInput, HostileOutput>(new HostileInput("sleep", []));
        var busy = await WaitForBusyAsync(capped, hub);

        busy.Outcome.Should().Be(SandboxOutcome.Busy);
        (await FluentActions.Awaiting(() => first).Should().ThrowAsync<SandboxRunException>())
            .Which.Outcome.Should()
            .Be(SandboxOutcome.TimedOut);
    }

    [Test]
    public async Task Canary_UnderAPlainRuntime_RefusesToStart()
    {
        // crun shares this machine's kernel; the real canary must see that and refuse.
        await FluentActions
            .Awaiting(async () =>
                await PodmanEscape
                    .StartAsync(runtime: "crun", assumeIsolated: false)
                    .ConfigureAwait(false)
            )
            .Should()
            .ThrowAsync<SandboxRuntimeRefusedException>()
            .WithMessage("*node's own kernel*");
    }

    [Test]
    public async Task Canary_RuntimeNotInstalled_RefusesToStart()
    {
        await FluentActions
            .Awaiting(async () =>
                await PodmanEscape
                    .StartAsync(runtime: "trax-no-such-runtime", assumeIsolated: false)
                    .ConfigureAwait(false)
            )
            .Should()
            .ThrowAsync<SandboxRuntimeRefusedException>()
            .WithMessage("*runtime may be missing*");
    }

    private static async Task<SandboxRunException> WaitForBusyAsync(
        PodmanEscape launcher,
        Microsoft.Extensions.DependencyInjection.ServiceProvider hub
    )
    {
        // The first run holds the only slot for 30 seconds; ask until the launcher says busy.
        for (var attempt = 0; attempt < 40; attempt++)
        {
            try
            {
                await launcher
                    .Client(hub)
                    .RunAsync<IHostileTrain, HostileInput, HostileOutput>(
                        new HostileInput("echo", ["x"])
                    )
                    .ConfigureAwait(false);
            }
            catch (SandboxRunException ex) when (ex.Outcome == SandboxOutcome.Busy)
            {
                return ex;
            }
            // allowed-delay: polling for the first run to take its slot.
            await Task.Delay(TimeSpan.FromMilliseconds(250), TimeProvider.System)
                .ConfigureAwait(false);
        }
        throw new InvalidOperationException("The launcher never said busy.");
    }

    private static string Request(string trainName) =>
        JsonSerializer.Serialize(
            new SandboxRunRequest(
                Convert.ToHexStringLower(
                    System.Security.Cryptography.RandomNumberGenerator.GetBytes(16)
                ),
                "escape",
                trainName,
                JsonSerializer.Serialize(
                    new HostileInput("echo", ["x"]),
                    TraxJsonSerializationOptions.ManifestProperties
                ),
                new SandboxLimits(500, 192, 8, 30)
            ),
            SandboxJson.Options
        );

    private async Task<(HttpStatusCode, SandboxRunResponse)> PostAsync(string body)
    {
        var bytes = Encoding.UTF8.GetBytes(body);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/trax/sandbox/run")
        {
            Content = new ByteArrayContent(bytes),
        };
        request.Headers.TryAddWithoutValidation(
            SandboxChannelSignature.HeaderName,
            SandboxChannelSignature.Sign(SandboxHosts.Key, bytes, TimeProvider.System)
        );
        using var client = Launcher.Raw();
        using var response = await client
            .SendAsync(request, CancellationToken.None)
            .ConfigureAwait(false);
        var answer =
            JsonSerializer.Deserialize<SandboxRunResponse>(
                await response
                    .Content.ReadAsByteArrayAsync(CancellationToken.None)
                    .ConfigureAwait(false),
                SandboxJson.Options
            ) ?? throw new InvalidOperationException();
        return (response.StatusCode, answer);
    }
}
