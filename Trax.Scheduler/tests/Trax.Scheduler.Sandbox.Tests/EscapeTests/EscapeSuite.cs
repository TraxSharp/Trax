using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Trax.Core.Exceptions;
using Trax.Effect.Utils;
using Trax.Scheduler.Sandbox.Image;
using Trax.Scheduler.Sandbox.Tests.Fakes;
using Trax.Scheduler.Services.RunExecutor;
using Trax.Scheduler.Services.Sandbox;

namespace Trax.Scheduler.Sandbox.Tests.EscapeTests;

/// <summary>
/// The escape suite: hostile trains in real sandboxes, and a hostile hub against the real
/// launcher, on whichever backend a subclass starts. Each attempt is blocked or killed, and the
/// run fails with the reason. A subclass skips unless its environment is provided; CI and
/// trax-test have neither a podman socket nor a cluster, and say so as a skip.
/// </summary>
[NonParallelizable]
public abstract class EscapeSuite
{
    private Microsoft.Extensions.DependencyInjection.ServiceProvider? _hub;

    /// <summary>The hub's services: the escape image's trains, to read their declarations.</summary>
    protected Microsoft.Extensions.DependencyInjection.ServiceProvider Hub =>
        _hub ?? throw new InvalidOperationException("not started");

    /// <summary>Skips when the environment is missing, then starts or finds the launcher.</summary>
    protected abstract Task StartLauncherAsync();

    protected abstract Task StopLauncherAsync();

    /// <summary>The key shared with the launcher.</summary>
    protected abstract byte[] Key { get; }

    /// <summary>
    /// Whether the runtime always reports a memory kill as one. Kata does only some of the time;
    /// otherwise the container only exits 137, which the run's reason then says.
    /// </summary>
    protected abstract bool ReportsMemoryKill { get; }

    /// <summary>The hub's client of the launcher.</summary>
    protected abstract ITraxSandbox Client();

    /// <summary>An HTTP client straight to the launcher, for requests a hub's client never makes.</summary>
    protected abstract HttpClient Raw();

    /// <summary>What the launcher made that still exists: containers and volumes, or Jobs and pods.</summary>
    protected abstract Task<int> LeftoversAsync();

    /// <summary>Addresses on this backend a sandbox must not reach: the hub, the database, the API server, the launcher.</summary>
    protected abstract IEnumerable<string> ForbiddenTargets();

    /// <summary>A client of a launcher whose concurrency cap is <c>Cap</c>, and what to dispose afterwards.</summary>
    protected abstract Task<(ITraxSandbox Client, int Cap, IAsyncDisposable? Owned)> CappedAsync();

    [OneTimeSetUp]
    public async Task StartAsync()
    {
        await StartLauncherAsync();
        _hub = SandboxHosts.Trains();
    }

    [OneTimeTearDown]
    public async Task StopAsync()
    {
        await StopLauncherAsync();
        if (_hub is not null)
            await _hub.DisposeAsync();
    }

    [TearDown]
    public async Task NothingLeftBehind() =>
        (await LeftoversAsync())
            .Should()
            .Be(0, "everything a run makes is removed, whatever it did");

    private Task<SandboxRun<HostileOutput>> RunAsync(HostileInput input) =>
        Client().RunAsync<IHostileTrain, HostileInput, HostileOutput>(input);

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
            .. ForbiddenTargets(),
            string.Create(System.Globalization.CultureInfo.InvariantCulture, $"127.0.0.1:{port}"),
            "169.254.169.254:80",
            "169.254.170.23:80",
            "169.254.170.2:80",
            "[fd00:ec2::254]:80",
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

        if (ReportsMemoryKill)
        {
            thrown.Outcome.Should().Be(SandboxOutcome.OutOfMemory, thrown.Message);
            thrown.Message.Should().Contain("192 MiB");
        }
        else if (thrown.Outcome != SandboxOutcome.OutOfMemory)
        {
            // Kata reports the guest's memory kill as OOMKilled some of the time, and otherwise
            // only as exit 137; either fails the run, and the reason says which.
            thrown.Outcome.Should().Be(SandboxOutcome.Exited, thrown.Message);
            thrown.Message.Should().Contain("code 137").And.Contain("under Kata");
        }
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
    public async Task FillDiskThroughAnUnlinkedFile_StopsAtItsOwnVolumeSize()
    {
        // A file deleted while still open is in no walk of the output; only a size the storage
        // enforces stops it, and then at this run's size, not the shared volume's.
        var run = await RunAsync(new HostileInput("fill-disk-unlinked", []));

        long.Parse(run.Output?.Echo ?? "", System.Globalization.CultureInfo.InvariantCulture)
            .Should()
            .BeLessThanOrEqualTo(8L * 1024 * 1024, "the run's own 8 MiB volume filled");
        (await RunAsync(new HostileInput("echo", ["still here"])))
            .Output?.Echo.Should()
            .Be("still here", "other runs are unaffected");
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
        (await LeftoversAsync()).Should().Be(0);
    }

    [Test]
    public async Task HostileHub_PastTheConcurrencyCap_IsBusy()
    {
        // One more than the cap, all at once: whichever arrives last is refused, the rest run
        // to their timeout. Which one is refused is the network's choice; how many is not.
        var (client, cap, owned) = await CappedAsync();
        try
        {
            var runs = Enumerable
                .Range(0, cap + 1)
                .Select(_ =>
                    client.RunAsync<IHostileTrain, HostileInput, HostileOutput>(
                        new HostileInput("sleep", [])
                    )
                )
                .ToList();
            var outcomes = new List<SandboxOutcome>();
            foreach (var run in runs)
                outcomes.Add(
                    (
                        await FluentActions
                            .Awaiting(() => run)
                            .Should()
                            .ThrowAsync<SandboxRunException>()
                    )
                        .Which
                        .Outcome
                );

            outcomes
                .Count(o => o == SandboxOutcome.Busy)
                .Should()
                .Be(1, "one request past the cap is refused as busy");
            outcomes
                .Count(o => o == SandboxOutcome.TimedOut)
                .Should()
                .Be(cap, "the rest run to their timeout");
        }
        finally
        {
            if (owned is not null)
                await owned.DisposeAsync();
        }
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
            SandboxChannelSignature.Sign(Key, bytes, TimeProvider.System)
        );
        using var client = Raw();
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
