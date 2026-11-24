using System.Formats.Tar;
using Microsoft.Extensions.Logging.Abstractions;
using Trax.Scheduler.Sandbox.Launcher.Backends;
using Trax.Scheduler.Sandbox.Tests.Fakes;
using Trax.Scheduler.Services.Sandbox;

namespace Trax.Scheduler.Sandbox.Tests.UnitTests;

/// <summary>
/// The podman backend's calls: the sandbox it creates is shaped only by the launcher, its output
/// is read through the holder after it exits, and every container and volume of the run is gone
/// afterwards, whatever happened.
///
/// <para>Enforces <c>docs/adr/0021-a-sandboxs-output-comes-back-on-a-volume-the-launcher-reads-after-it-exits.md</c>.</para>
/// </summary>
[Property(
    "adr",
    "docs/adr/0021-a-sandboxs-output-comes-back-on-a-volume-the-launcher-reads-after-it-exits.md"
)]
[TestFixture]
public class PodmanSandboxBackendTests
{
    private const string Adr =
        "docs/adr/0021-a-sandboxs-output-comes-back-on-a-volume-the-launcher-reads-after-it-exits.md";

    private const string RunId = "0123456789abcdef0123456789abcdef";

    private static SandboxJob Job(int timeout = 30) =>
        new(
            RunId,
            LauncherOptions.Podman().Profiles["escape"],
            new SandboxLimits(500, 192, 8, timeout),
            new Dictionary<string, byte[]>(StringComparer.Ordinal)
            {
                [SandboxPaths.RequestFile] = "{}"u8.ToArray(),
            }
        );

    private static PodmanSandboxBackend Backend(FakePodman podman) =>
        new(
            new PodmanApi(new HttpClient(podman) { BaseAddress = new Uri("http://podman") }),
            LauncherOptions.Podman(),
            "kata",
            TimeProvider.System,
            NullLogger<PodmanSandboxBackend>.Instance
        );

    private static async Task<byte[]> OutputAsync(TarBuilder tar)
    {
        var stream = await tar.BuildAsync().ConfigureAwait(false);
        await using (stream.ConfigureAwait(false))
            return stream.ToArray();
    }

    private static void RemovedEverything(FakePodman podman)
    {
        podman
            .Calls.Should()
            .Contain($"DELETE /v5.0.0/libpod/containers/trax-sbx-{RunId}?force=true&v=true&t=0");
        podman
            .Calls.Should()
            .Contain(
                $"DELETE /v5.0.0/libpod/containers/trax-sbx-{RunId}-holder?force=true&v=true&t=0"
            );
        podman
            .Calls.Should()
            .Contain($"DELETE /v5.0.0/libpod/volumes/trax-sbx-{RunId}-in?force=true");
        podman
            .Calls.Should()
            .Contain($"DELETE /v5.0.0/libpod/volumes/trax-sbx-{RunId}-out?force=true");
    }

    [Test]
    public async Task RunAsync_ExitedSandbox_IsReadThroughTheHolderAndRemoved()
    {
        var podman = new FakePodman
        {
            Output = await OutputAsync(
                new TarBuilder()
                    .Directory("out/")
                    .File("out/result.json", "{}")
                    .File("out/files/a.txt", "a")
            ),
        };

        var execution = await Backend(podman).RunAsync(Job(), CancellationToken.None);

        execution.End.Should().Be(SandboxEnd.Exited);
        execution.ExitCode.Should().Be(0);
        execution.Output.Select(f => f.Path).Should().Equal("result.json", "files/a.txt");
        podman
            .Calls.Should()
            .Contain($"GET /v5.0.0/libpod/containers/trax-sbx-{RunId}-holder/archive?path=%2Fout");
        podman
            .Calls.Should()
            .Contain($"PUT /v5.0.0/libpod/containers/trax-sbx-{RunId}-holder/archive?path=%2Fin");
        RemovedEverything(podman);
    }

    [Test]
    public async Task RunAsync_TheSandboxSpec_IsLockedDown()
    {
        var podman = new FakePodman();

        await Backend(podman).RunAsync(Job(), CancellationToken.None);

        var spec = podman.Created[$"trax-sbx-{RunId}"];
        spec["image"]?.GetValue<string>().Should().Be(LauncherOptions.Image);
        spec["oci_runtime"]?.GetValue<string>().Should().Be("kata");
        spec["netns"]?["nsmode"]?.GetValue<string>().Should().Be("none", Adr);
        spec["read_only_filesystem"]?.GetValue<bool>().Should().BeTrue();
        spec["no_new_privileges"]?.GetValue<bool>().Should().BeTrue();
        spec["privileged"]?.GetValue<bool>().Should().BeFalse();
        spec["cap_drop"]?.AsArray().Select(c => c?.GetValue<string>()).Should().Equal("all");
        spec["user"]?.GetValue<string>().Should().Be("65532:65532");
        spec["resource_limits"]
            ?["memory"]?["limit"]?.GetValue<long>()
            .Should()
            .Be(192L * 1024 * 1024);
        spec["resource_limits"]
            ?["memory"]?["swap"]?.GetValue<long>()
            .Should()
            .Be(192L * 1024 * 1024, "no swap beyond the memory limit");
        spec["resource_limits"]?["cpu"]?["quota"]?.GetValue<long>().Should().Be(50_000);
        spec["resource_limits"]?["pids"]?["limit"]?.GetValue<int>().Should().Be(512);
        spec.ContainsKey("cap_add").Should().BeFalse();
        spec["log_configuration"]
            ?["driver"]?.GetValue<string>()
            .Should()
            .Be("none", "what a sandbox prints must not fill the host's disk");
        spec.ContainsKey("devices").Should().BeFalse();
        var volumes = spec["volumes"]
            ?.AsArray()
            .Select(v => $"{v?["Dest"]}:{v?["Options"]?[0]}")
            .ToList();
        volumes.Should().BeEquivalentTo("/sandbox/in:ro", "/sandbox/out:rw");
    }

    [Test]
    public async Task RunAsync_TheHolder_RunsUnderTheDefaultRuntimeWithTheOutputReadOnly()
    {
        var podman = new FakePodman();

        await Backend(podman).RunAsync(Job(), CancellationToken.None);

        var spec = podman.Created[$"trax-sbx-{RunId}-holder"];
        spec["image"]?.GetValue<string>().Should().Be(LauncherOptions.Holder);
        spec.ContainsKey("oci_runtime").Should().BeFalse();
        spec["netns"]?["nsmode"]?.GetValue<string>().Should().Be("none");
        spec["volumes"]
            ?.AsArray()
            .Select(v => $"{v?["Dest"]}:{v?["Options"]?[0]}")
            .Should()
            .BeEquivalentTo("/in:rw", "/out:ro");
    }

    [Test]
    public async Task RunAsync_RuntimeMissing_FailsAndRemovesEverything()
    {
        var podman = new FakePodman { RuntimeMissing = true };

        var execution = await Backend(podman).RunAsync(Job(), CancellationToken.None);

        execution.End.Should().Be(SandboxEnd.Failed);
        execution.Detail.Should().Contain("kata");
        RemovedEverything(podman);
    }

    [Test]
    public async Task RunAsync_PastItsTimeout_IsKilled()
    {
        var podman = new FakePodman { Hang = true };

        var execution = await Backend(podman).RunAsync(Job(timeout: 1), CancellationToken.None);

        execution.End.Should().Be(SandboxEnd.TimedOut);
        podman
            .Calls.Should()
            .Contain($"POST /v5.0.0/libpod/containers/trax-sbx-{RunId}/kill?signal=SIGKILL");
        RemovedEverything(podman);
    }

    [Test]
    public async Task RunAsync_OomKilled_IsOutOfMemory()
    {
        var podman = new FakePodman { OomKilled = true, ExitCode = 137 };

        (await Backend(podman).RunAsync(Job(), CancellationToken.None))
            .End.Should()
            .Be(SandboxEnd.OutOfMemory);
    }

    [Test]
    public async Task RunAsync_OutputWithALink_IsRefused()
    {
        var podman = new FakePodman
        {
            Output = await OutputAsync(
                new TarBuilder()
                    .File("out/result.json", "{}")
                    .Entry(TarEntryType.SymbolicLink, "out/files/l", "/etc/shadow")
            ),
        };

        var execution = await Backend(podman).RunAsync(Job(), CancellationToken.None);

        execution.OutputRefused.Should().Contain("symbolic link");
        execution.Output.Should().BeEmpty();
    }

    [Test]
    public async Task RunAsync_CallerGivesUp_StillRemovesEverything()
    {
        var podman = new FakePodman { Hang = true };
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        await FluentActions
            .Awaiting(() => Backend(podman).RunAsync(Job(), cancel.Token))
            .Should()
            .ThrowAsync<OperationCanceledException>();

        RemovedEverything(podman);
    }

    [Test]
    public async Task SweepAsync_RemovesWhatCarriesTheLauncherLabel()
    {
        var podman = new FakePodman();

        await Backend(podman).SweepAsync(CancellationToken.None);

        podman
            .Calls.Should()
            .Contain(c =>
                c.StartsWith(
                    "GET /v5.0.0/libpod/containers/json?all=true&filters=",
                    StringComparison.Ordinal
                ) && c.Contains("trax.sandbox.launcher", StringComparison.Ordinal)
            );
        podman
            .Calls.Should()
            .Contain(c =>
                c.StartsWith("GET /v5.0.0/libpod/volumes/json?filters=", StringComparison.Ordinal)
            );
    }
}
