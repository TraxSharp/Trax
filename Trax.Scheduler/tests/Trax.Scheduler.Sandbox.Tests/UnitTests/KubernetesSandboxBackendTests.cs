using k8s;
using Microsoft.Extensions.Logging.Abstractions;
using Trax.Scheduler.Sandbox.Launcher.Backends;
using Trax.Scheduler.Sandbox.Tests.Fakes;
using Trax.Scheduler.Services.Sandbox;

namespace Trax.Scheduler.Sandbox.Tests.UnitTests;

/// <summary>
/// The cluster backend's calls, against an API that answers as the launcher's Role allows: the
/// run uses no subresource (a first version read <c>jobs/status</c> and was refused on k3s), the
/// output is read from the run's directory after the pod is gone, and the directory and Job are
/// removed whatever happened.
///
/// <para>Enforces <c>docs/adr/0021-a-sandboxs-output-comes-back-on-a-volume-the-launcher-reads-after-it-exits.md</c>.</para>
/// </summary>
[Property(
    "adr",
    "docs/adr/0021-a-sandboxs-output-comes-back-on-a-volume-the-launcher-reads-after-it-exits.md"
)]
[TestFixture]
public class KubernetesSandboxBackendTests
{
    private const string Adr =
        "docs/adr/0021-a-sandboxs-output-comes-back-on-a-volume-the-launcher-reads-after-it-exits.md";
    private const string RunId = "0123456789abcdef0123456789abcdef";
    private string _runs = "";

    [SetUp]
    public void SetUp() => _runs = Directory.CreateTempSubdirectory("trax-sbx-runs-").FullName;

    [TearDown]
    public void TearDown() => Directory.Delete(_runs, recursive: true);

    private async Task<SandboxExecution> RunAsync(FakeKubernetes api)
    {
        var options = LauncherOptions.Kubernetes();
        options.Kubernetes.RunsPath = _runs;
        options.Kubernetes.PollInterval = TimeSpan.FromMilliseconds(10);
        using var client = new Kubernetes(
            new KubernetesClientConfiguration { Host = "http://cluster" },
            api
        );
        var backend = new KubernetesSandboxBackend(
            client,
            options,
            TimeProvider.System,
            NullLogger<KubernetesSandboxBackend>.Instance
        );
        var job = new SandboxJob(
            RunId,
            options.Profiles["escape"],
            new SandboxLimits(500, 192, 8, 30),
            new Dictionary<string, byte[]>(StringComparer.Ordinal)
            {
                [SandboxPaths.RequestFile] = "{}"u8.ToArray(),
            }
        );
        return await backend.RunAsync(job, CancellationToken.None).ConfigureAwait(false);
    }

    [Test]
    public async Task RunAsync_CompletedJob_IsReadFromItsDirectoryWithNoSubresource()
    {
        var api = new FakeKubernetes(_runs);
        api.Output["result.json"] = "{}";
        api.Output["files/a.txt"] = "a";

        var execution = await RunAsync(api);

        execution.End.Should().Be(SandboxEnd.Exited, execution.Detail);
        execution.ExitCode.Should().Be(0);
        execution.Output.Select(f => f.Path).Should().BeEquivalentTo("result.json", "files/a.txt");
        api.Calls.Should()
            .NotContain(
                c =>
                    c.Contains("/status", StringComparison.Ordinal)
                    || c.Contains("/log", StringComparison.Ordinal)
                    || c.Contains("/exec", StringComparison.Ordinal),
                Adr
            );
        api.CreatedJob?["spec"]?["template"]?["spec"]?["runtimeClassName"]?.GetValue<string>()
            .Should()
            .Be("kata");
        Directory
            .Exists(Path.Combine(_runs, RunId))
            .Should()
            .BeFalse("the run's directory goes with it");
        api.Calls.Should().Contain(c => c.StartsWith("DELETE ", StringComparison.Ordinal));
    }

    [Test]
    public async Task RunAsync_TheInputIsWrittenBeforeTheJob()
    {
        var api = new FakeKubernetes(_runs);

        await RunAsync(api);

        api.CreatedJob.Should().NotBeNull();
        api.Calls[0].Should().StartWith("POST ");
    }

    [Test]
    public async Task RunAsync_DeadlineExceeded_IsTimedOut()
    {
        var api = new FakeKubernetes(_runs) { Condition = ("Failed", "DeadlineExceeded") };

        (await RunAsync(api)).End.Should().Be(SandboxEnd.TimedOut);
    }

    [Test]
    public async Task RunAsync_OomKilled_IsOutOfMemory()
    {
        var api = new FakeKubernetes(_runs)
        {
            Condition = ("Failed", "BackoffLimitExceeded"),
            TerminatedReason = "OOMKilled",
            ExitCode = 137,
        };

        (await RunAsync(api)).End.Should().Be(SandboxEnd.OutOfMemory);
    }

    [Test]
    public async Task RunAsync_ALinkInTheOutput_IsRefused()
    {
        var api = new FakeKubernetes(_runs);
        api.Output["result.json"] = "{}";
        var output = Path.Combine(_runs, RunId, "out");
        Directory.CreateDirectory(output);
        File.CreateSymbolicLink(Path.Combine(output, "files"), "/etc");

        var execution = await RunAsync(api);

        execution.OutputRefused.Should().Contain("symbolic link");
    }
}
