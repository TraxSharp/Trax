using System.IO.Pipelines;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Trax.Scheduler.Sandbox.Launcher.Backends;
using Trax.Scheduler.Sandbox.Launcher.Core;
using Trax.Scheduler.Sandbox.Tests.Fakes;
using Trax.Scheduler.Services.Sandbox;

namespace Trax.Scheduler.Sandbox.Tests.UnitTests;

/// <summary>
/// The hostile-hub cases against the launcher's one route: an unauthenticated caller gets a bare
/// 401; a hub with the key can ask but not shape, and not exceed the concurrency cap. In no
/// refused case does the backend start anything.
///
/// <para>Enforces <c>docs/adr/0020-a-sandbox-is-started-only-by-the-launcher-from-a-fixed-request.md</c>,
/// and <c>docs/adr/0022-a-sandbox-runs-in-a-kata-microvm-proved-by-a-startup-canary.md</c> for the start.</para>
/// </summary>
[Property("adr", "docs/adr/0020-a-sandbox-is-started-only-by-the-launcher-from-a-fixed-request.md")]
[Property("adr", "docs/adr/0022-a-sandbox-runs-in-a-kata-microvm-proved-by-a-startup-canary.md")]
[TestFixture]
public class LauncherEndpointTests
{
    private const string CanaryAdr =
        "docs/adr/0022-a-sandbox-runs-in-a-kata-microvm-proved-by-a-startup-canary.md";
    private const string Adr =
        "docs/adr/0020-a-sandbox-is-started-only-by-the-launcher-from-a-fixed-request.md";

    private static SandboxRunResponse Read(byte[] body) =>
        JsonSerializer.Deserialize<SandboxRunResponse>(body, SandboxJson.Options)
        ?? throw new InvalidOperationException();

    [Test]
    public async Task Post_SignedValidRequest_RunsAndAnswersSigned()
    {
        await using var server = await LauncherServer.StartAsync();

        var (response, body, signature) = await server.PostAsync(LauncherServer.Request());

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        Read(body).Outcome.Should().Be(SandboxOutcome.Completed);
        SandboxChannelSignature
            .VerifyResponse(
                SandboxHosts.Key,
                signature,
                response.Headers.GetValues(SandboxChannelSignature.ResponseHeaderName).Single(),
                body
            )
            .Should()
            .BeTrue();
        server.Backend.Jobs.Should().ContainSingle();
    }

    [Test]
    public async Task Start_SweepsAndRunsTheCanaryBeforeListening()
    {
        await using var server = await LauncherServer.StartAsync();

        server.Backend.Sweeps.Should().Be(1);
    }

    [Test]
    public async Task Start_CanaryOnTheHostsKernel_RefusesToStart()
    {
        var backend = new FakeBackend
        {
            Canary = new SandboxCanaryReport(LauncherServer.HostKernel, ["lo"], []),
        };

        await FluentActions
            .Awaiting(() => LauncherServer.StartAsync(backend))
            .Should()
            .ThrowAsync<SandboxRuntimeRefusedException>(CanaryAdr);
    }

    [Test]
    public async Task Post_Unsigned_IsABare401()
    {
        await using var server = await LauncherServer.StartAsync();

        var (response, body, _) = await server.PostAsync(LauncherServer.Request(), signature: "");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        body.Should().BeEmpty();
        response.Headers.Contains(SandboxChannelSignature.ResponseHeaderName).Should().BeFalse();
        server.Backend.Jobs.Should().BeEmpty();
    }

    [Test]
    public async Task Post_WrongKey_IsA401()
    {
        await using var server = await LauncherServer.StartAsync();

        var (response, _, _) = await server.PostAsync(
            LauncherServer.Request(),
            key: Enumerable.Repeat((byte)3, 32).ToArray()
        );

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        server.Backend.Jobs.Should().BeEmpty();
    }

    [Test]
    public async Task Post_Stale_IsA401()
    {
        await using var server = await LauncherServer.StartAsync();
        var body = LauncherServer.Request();
        var stale = SandboxChannelSignature.Sign(
            SandboxHosts.Key,
            System.Text.Encoding.UTF8.GetBytes(body),
            TimeProvider.System.GetUtcNow().AddMinutes(-5).ToUnixTimeSeconds(),
            "00112233445566778899aabbccddeeff"
        );

        var (response, _, _) = await server.PostAsync(body, signature: stale);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task Post_Replayed_IsA401AndStartsOneSandbox()
    {
        await using var server = await LauncherServer.StartAsync();
        var (first, _, signature) = await server.PostAsync(LauncherServer.Request());

        var (second, _, _) = await server.PostAsync(LauncherServer.Request(), signature: signature);

        first.StatusCode.Should().Be(HttpStatusCode.OK);
        second.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        server.Backend.Jobs.Should().ContainSingle();
    }

    [TestCase("image", "\"docker.io/library/alpine:latest\"")]
    [TestCase("privileged", "true")]
    [TestCase("command", "[\"/bin/sh\",\"-c\",\"id\"]")]
    [TestCase("hostNetwork", "true")]
    [TestCase("runtimeClassName", "\"runc\"")]
    [TestCase("serviceAccountName", "\"trax-sandbox-launcher\"")]
    [TestCase("mounts", "[{\"hostPath\":\"/\"}]")]
    [TestCase("env", "{\"A\":\"1\"}")]
    [TestCase("capabilities", "[\"ALL\"]")]
    public async Task Post_HubShapingTheSandbox_IsRefusedSignedAndStartsNothing(
        string member,
        string value
    )
    {
        await using var server = await LauncherServer.StartAsync();
        var request =
            JsonNode.Parse(LauncherServer.Request())?.AsObject()
            ?? throw new InvalidOperationException();
        request[member] = JsonNode.Parse(value);

        var (response, body, signature) = await server.PostAsync(request.ToJsonString());

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var answer = Read(body);
        answer.Outcome.Should().Be(SandboxOutcome.Refused);
        answer.Reason.Should().Contain(member);
        answer.RunId.Should().Be("0123456789abcdef0123456789abcdef");
        SandboxChannelSignature
            .VerifyResponse(
                SandboxHosts.Key,
                signature,
                response.Headers.GetValues(SandboxChannelSignature.ResponseHeaderName).Single(),
                body
            )
            .Should()
            .BeTrue();
        server.Backend.Jobs.Should().BeEmpty(Adr);
    }

    [Test]
    public async Task Post_OverTheCaps_IsRefused()
    {
        await using var server = await LauncherServer.StartAsync();

        var (response, body, _) = await server.PostAsync(LauncherServer.Request(timeout: 999_999));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        Read(body).Reason.Should().Contain("timeout");
        server.Backend.Jobs.Should().BeEmpty();
    }

    [Test]
    public async Task Post_BodyOverTheLimit_Is413Unread()
    {
        var options = LauncherOptions.Podman();
        options.Caps.MaxInputBytes = 16;
        await using var server = await LauncherServer.StartAsync(options: options);

        var (response, _, _) = await server.PostAsync(new string(' ', 200_000));

        response.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);
    }

    [Test]
    public async Task Post_MoreThanTheConcurrencyCap_IsBusy()
    {
        var release = new TaskCompletionSource<SandboxExecution>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var backend = new FakeBackend
        {
            Run = _ =>
            {
                started.TrySetResult();
                return release.Task;
            },
        };
        var options = LauncherOptions.Podman();
        options.Caps.MaxConcurrent = 1;
        await using var server = await LauncherServer.StartAsync(backend, options);

        var first = server.PostAsync(LauncherServer.Request("00000000000000000000000000000001"));
        await started.Task.WaitAsync(TimeSpan.FromSeconds(15), TimeProvider.System);
        var (second, body, _) = await server.PostAsync(
            LauncherServer.Request("00000000000000000000000000000002")
        );

        second.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        Read(body).Outcome.Should().Be(SandboxOutcome.Busy);
        backend.Jobs.Should().ContainSingle("the second request started nothing");

        release.SetResult(
            new SandboxExecution(
                SandboxEnd.Exited,
                0,
                [new SandboxFile(SandboxPaths.ResultFile, "{}"u8.ToArray())],
                OutputRefused: null
            )
        );
        (await first).Response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Test]
    public async Task Healthz_AnswersOk()
    {
        await using var server = await LauncherServer.StartAsync();

        (await server.Client.GetStringAsync("/healthz")).Should().Be("ok");
    }

    private static (LauncherEndpoint Endpoint, FakeTimeProvider Time) Endpoint()
    {
        var options = LauncherOptions.Podman();
        options.Caps.MaxConcurrent = 1;
        var time = new FakeTimeProvider(TimeProvider.System.GetUtcNow());
        var endpoint = new LauncherEndpoint(
            options,
            new NonceCache(time),
            new SandboxRunner(
                new FakeBackend(),
                options,
                new SandboxGate(options.Caps.MaxConcurrent),
                NullLogger<SandboxRunner>.Instance
            ),
            time,
            NullLogger<LauncherEndpoint>.Instance
        );
        return (endpoint, time);
    }

    /// <summary>A request whose signature has a fresh, well-formed header anyone could write, and whose body is <paramref name="body"/>.</summary>
    private static DefaultHttpContext Unauthenticated(TimeProvider time, Stream body)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = "POST";
        context.Request.Body = body;
        context.Request.Headers[SandboxChannelSignature.HeaderName] = SandboxChannelSignature.Sign(
            new byte[32],
            "{}"u8.ToArray(),
            time
        );
        return context;
    }

    [Test]
    public async Task Post_PastTheBodiesBeingRead_IsA503Unread()
    {
        var (endpoint, time) = Endpoint();
        var pipes = Enumerable.Range(0, endpoint.MaxPendingReads).Select(_ => new Pipe()).ToList();
        var held = pipes
            .Select(p => endpoint.HandleAsync(Unauthenticated(time, p.Reader.AsStream())))
            .ToList();

        var refused = Unauthenticated(time, new MemoryStream("{}"u8.ToArray()));
        await endpoint.HandleAsync(refused);

        refused.Response.StatusCode.Should().Be(StatusCodes.Status503ServiceUnavailable, Adr);
        foreach (var pipe in pipes)
            await pipe.Writer.CompleteAsync();
        await Task.WhenAll(held);
        var next = Unauthenticated(time, new MemoryStream("{}"u8.ToArray()));
        await endpoint.HandleAsync(next);
        next.Response.StatusCode.Should()
            .Be(StatusCodes.Status401Unauthorized, "a finished read gives its place back");
    }

    [Test]
    public async Task Post_ABodyThatOutlastsTheFreshnessWindow_IsDroppedAndGivesItsPlaceBack()
    {
        var (endpoint, time) = Endpoint();
        var pipe = new Pipe();
        var slow = Unauthenticated(time, pipe.Reader.AsStream());
        var handled = endpoint.HandleAsync(slow);

        time.Advance(NonceCache.Window + TimeSpan.FromSeconds(1));
        await handled.WaitAsync(
            TimeSpan.FromSeconds(10),
            TimeProvider.System,
            CancellationToken.None
        );

        slow.Response.StatusCode.Should().Be(StatusCodes.Status401Unauthorized, Adr);
        await pipe.Writer.CompleteAsync();
    }

    [Test]
    public async Task Post_EverySlowBodyDroppedAtTheWindow_LeavesTheNextRequestRead()
    {
        var (endpoint, time) = Endpoint();
        var pipes = Enumerable.Range(0, endpoint.MaxPendingReads).Select(_ => new Pipe()).ToList();
        var slow = pipes
            .Select(p => endpoint.HandleAsync(Unauthenticated(time, p.Reader.AsStream())))
            .ToList();

        time.Advance(NonceCache.Window + TimeSpan.FromSeconds(1));
        await Task.WhenAll(slow)
            .WaitAsync(TimeSpan.FromSeconds(10), TimeProvider.System, CancellationToken.None);
        var next = Unauthenticated(time, new MemoryStream("{}"u8.ToArray()));
        await endpoint.HandleAsync(next);

        next.Response.StatusCode.Should()
            .Be(StatusCodes.Status401Unauthorized, "a dropped body gives its place back; " + Adr);
        foreach (var pipe in pipes)
            await pipe.Writer.CompleteAsync();
    }
}
