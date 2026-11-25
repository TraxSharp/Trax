using System.Globalization;
using k8s;
using Microsoft.Extensions.Logging.Abstractions;
using Trax.Scheduler.Sandbox.Launcher.Backends;
using Trax.Scheduler.Sandbox.Launcher.Configuration;
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
    private const int DiskMiB = 8;
    private static readonly byte[] Request = "{\"train\":\"t\"}"u8.ToArray();
    private string _runs = "";
    private ShiftedClock _clock = new();
    private long _available = 100;

    [SetUp]
    public void SetUp()
    {
        _runs = Directory.CreateTempSubdirectory("trax-sbx-runs-").FullName;
        _clock = new ShiftedClock();
        _available = 100;
    }

    [TearDown]
    public void TearDown()
    {
        if (!OperatingSystem.IsWindows())
            foreach (
                var directory in Directory.EnumerateDirectories(
                    _runs,
                    "*",
                    new EnumerationOptions
                    {
                        RecurseSubdirectories = true,
                        IgnoreInaccessible = true,
                    }
                )
            )
                File.SetUnixFileMode(directory, (UnixFileMode)0b111_000_000);
        Directory.Delete(_runs, recursive: true);
    }

    /// <summary>Root removes a read-only directory anyway; CI runs the tests as a normal user.</summary>
    private static void AsANormalUser()
    {
        if (OperatingSystem.IsWindows() || Environment.IsPrivilegedProcess)
            Assert.Ignore(
                "Only a process without root's override can find a directory it cannot empty."
            );
    }

    [System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
    private static void ReadOnly(string directory) =>
        File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserExecute);

    private async Task SweepAsync(FakeKubernetes api)
    {
        var options = LauncherOptions.Kubernetes();
        options.Kubernetes.RunsPath = _runs;
        options.Kubernetes.PollInterval = TimeSpan.FromMilliseconds(10);
        using var client = new Kubernetes(
            new KubernetesClientConfiguration { Host = "http://cluster" },
            api
        );
        await new KubernetesSandboxBackend(
            client,
            options,
            _clock,
            NullLogger<KubernetesSandboxBackend>.Instance,
            _ => (_available, 100)
        )
            .SweepAsync(CancellationToken.None)
            .ConfigureAwait(false);
    }

    private async Task<SandboxExecution> RunAsync(
        FakeKubernetes api,
        Action<SandboxLauncherOptions>? configure = null,
        CancellationToken ct = default
    )
    {
        var options = LauncherOptions.Kubernetes();
        options.Kubernetes.RunsPath = _runs;
        options.Kubernetes.PollInterval = TimeSpan.FromMilliseconds(10);
        configure?.Invoke(options);
        using var client = new Kubernetes(
            new KubernetesClientConfiguration { Host = "http://cluster" },
            api
        );
        var backend = new KubernetesSandboxBackend(
            client,
            options,
            _clock,
            NullLogger<KubernetesSandboxBackend>.Instance,
            _ => (_available, 100)
        );
        var job = new SandboxJob(
            RunId,
            options.Profiles["escape"],
            new SandboxLimits(500, 192, DiskMiB, 30),
            new Dictionary<string, byte[]>(StringComparer.Ordinal)
            {
                [SandboxPaths.RequestFile] = Request,
            }
        );
        return await backend.RunAsync(job, ct).ConfigureAwait(false);
    }

    private string OutputOf(string path) => Path.Combine(_runs, RunId, SandboxPaths.Output, path);

    private static async Task WriteAsync(string file, long bytes)
    {
        Directory.CreateDirectory(
            Path.GetDirectoryName(file) ?? throw new ArgumentException("no directory", nameof(file))
        );
        await File.WriteAllBytesAsync(file, new byte[bytes]).ConfigureAwait(false);
    }

    private void KilledAndRemoved(FakeKubernetes api)
    {
        api.Calls.Should()
            .Contain(
                c => c.StartsWith("DELETE ", StringComparison.Ordinal),
                "a run the launcher gives up on is deleted"
            );
        Directory.Exists(Path.Combine(_runs, RunId)).Should().BeFalse("its directory goes too");
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
        api.InputAtCreate.Keys.Should()
            .Equal([SandboxPaths.RequestFile], "the sandbox reads its request from its input");
        api.InputAtCreate[SandboxPaths.RequestFile].Should().Equal(Request);
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

    [Test]
    public async Task RunAsync_OutputPastItsDiskWhileRunning_IsKilled()
    {
        var api = new FakeKubernetes(_runs) { RunningPolls = 1000 };
        api.OnPoll = async poll =>
        {
            if (poll == 1)
                await WriteAsync(OutputOf("files/big"), (DiskMiB + 1) * 1024L * 1024)
                    .ConfigureAwait(false);
        };

        var execution = await RunAsync(api);

        execution.End.Should().Be(SandboxEnd.DiskExceeded, Adr);
        execution.Output.Should().BeEmpty("a killed run hands nothing back");
        KilledAndRemoved(api);
    }

    [Test]
    public async Task RunAsync_OutputWithinItsDisk_RunsToTheEnd()
    {
        var api = new FakeKubernetes(_runs) { RunningPolls = 3 };
        api.OnPoll = async poll =>
        {
            if (poll == 1)
                await WriteAsync(OutputOf("files/big"), (DiskMiB - 1) * 1024L * 1024)
                    .ConfigureAwait(false);
        };

        var execution = await RunAsync(api);

        execution.End.Should().Be(SandboxEnd.Exited, "the disk is counted in MiB");
        execution.Output.Select(f => f.Path).Should().Equal("files/big");
    }

    [Test]
    public async Task RunAsync_TooManyEntriesWhileRunning_IsKilled()
    {
        var api = new FakeKubernetes(_runs) { RunningPolls = 1000 };
        api.OnPoll = async poll =>
        {
            if (poll == 1)
                for (var i = 0; i < 20; i++)
                    await WriteAsync(
                            OutputOf("files/" + i.ToString(CultureInfo.InvariantCulture)),
                            0
                        )
                        .ConfigureAwait(false);
        };

        var execution = await RunAsync(api, o => o.Caps.MaxArchiveFiles = 10);

        execution.End.Should().Be(SandboxEnd.TooManyEntries, Adr);
        KilledAndRemoved(api);
    }

    [Test]
    public async Task RunAsync_AJobThatNeverStarts_IsGivenUpOn()
    {
        var api = new FakeKubernetes(_runs) { RunningPolls = 1000 };
        api.OnPoll = poll =>
        {
            if (poll == 2)
                _clock.Shift = KubernetesSandboxBackend.StartGrace + TimeSpan.FromHours(2);
            return Task.CompletedTask;
        };

        var execution = await RunAsync(api);

        execution.End.Should().Be(SandboxEnd.Failed);
        execution.Detail.Should().Be("the Job did not start in time");
        KilledAndRemoved(api);
    }

    [Test]
    public async Task RunAsync_CallerGivesUp_StillDeletesTheJobAndTheDirectory()
    {
        using var cancel = new CancellationTokenSource();
        var api = new FakeKubernetes(_runs) { RunningPolls = 1000 };
        api.OnPoll = async poll =>
        {
            if (poll == 1)
                await cancel.CancelAsync().ConfigureAwait(false);
        };

        await FluentActions
            .Awaiting(() => RunAsync(api, ct: cancel.Token))
            .Should()
            .ThrowAsync<OperationCanceledException>();

        KilledAndRemoved(api);
    }

    [Test]
    public async Task RunAsync_RunsVolumeBelowItsFloor_StartsNothing()
    {
        var api = new FakeKubernetes(_runs);
        _available = 10;

        var execution = await RunAsync(api);

        execution.End.Should().Be(SandboxEnd.Failed);
        execution.Detail.Should().Be("the runs volume has less than 15% free");
        api.CreatedJob.Should().BeNull(Adr);
    }

    [Test]
    public async Task RunAsync_RunsVolumeFallsBelowItsFloorWhileRunning_IsKilled()
    {
        // A file deleted while still open is not in any walk of the output; the filesystem's
        // free space still counts it.
        var api = new FakeKubernetes(_runs) { RunningPolls = 1000 };
        api.OnPoll = poll =>
        {
            if (poll == 1)
                _available = 14;
            return Task.CompletedTask;
        };

        var execution = await RunAsync(api);

        execution.End.Should().Be(SandboxEnd.Failed, Adr);
        execution.Detail.Should().Be("the runs volume has less than 15% free");
        execution.Output.Should().BeEmpty();
        KilledAndRemoved(api);
    }

    [Test]
    public void FreeSpace_IsTheFilesystemsOwnCount()
    {
        var (available, total) = KubernetesSandboxBackend.FreeSpace(_runs);

        total.Should().BePositive();
        available.Should().BeInRange(0, total);
    }

    [Test]
    public async Task RunAsync_ADirectoryTheSandboxMadeReadOnly_IsStillRemoved()
    {
        AsANormalUser();
        var api = new FakeKubernetes(_runs) { RunningPolls = 1 };
        api.OnPoll = async poll =>
        {
            if (poll == 1 && !OperatingSystem.IsWindows())
            {
                await WriteAsync(OutputOf("files/x/f"), 1).ConfigureAwait(false);
                ReadOnly(OutputOf("files/x"));
            }
        };

        await RunAsync(api);

        Directory.Exists(Path.Combine(_runs, RunId)).Should().BeFalse();
    }

    [Test]
    public async Task SweepAsync_ARunDirectoryTheSandboxMadeReadOnly_IsRemovedAndTheStartGoesOn()
    {
        AsANormalUser();
        await WriteAsync(OutputOf("x/f"), 1);
        if (!OperatingSystem.IsWindows())
            ReadOnly(OutputOf("x"));
        var other = Path.Combine(_runs, "lost+found");
        Directory.CreateDirectory(other);

        await SweepAsync(new FakeKubernetes(_runs));

        Directory.Exists(Path.Combine(_runs, RunId)).Should().BeFalse();
        Directory.Exists(other).Should().BeTrue("only a run's directory is the launcher's");
    }
}
