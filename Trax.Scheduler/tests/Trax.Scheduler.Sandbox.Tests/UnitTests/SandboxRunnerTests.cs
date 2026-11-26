using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Trax.Scheduler.Sandbox.Launcher.Backends;
using Trax.Scheduler.Sandbox.Launcher.Core;
using Trax.Scheduler.Sandbox.Tests.Fakes;
using Trax.Scheduler.Services.Sandbox;

namespace Trax.Scheduler.Sandbox.Tests.UnitTests;

/// <summary>
/// What the launcher makes of a finished sandbox: a kill fails the run with its reason, and only
/// a result plus the files under <c>files/</c> go back, re-written by the launcher.
///
/// <para>Enforces <c>docs/adr/0021-a-sandboxs-output-comes-back-on-a-volume-the-launcher-reads-after-it-exits.md</c>.</para>
/// </summary>
[Property(
    "adr",
    "docs/adr/0021-a-sandboxs-output-comes-back-on-a-volume-the-launcher-reads-after-it-exits.md"
)]
[TestFixture]
public class SandboxRunnerTests
{
    private const string Adr =
        "docs/adr/0021-a-sandboxs-output-comes-back-on-a-volume-the-launcher-reads-after-it-exits.md";

    private static readonly SandboxRunRequest Request = new(
        "0123456789abcdef0123456789abcdef",
        "escape",
        "Trax.Scheduler.Sandbox.Image.IHostileTrain",
        "{}",
        new SandboxLimits(500, 192, 8, 30)
    );

    private static readonly SandboxFile Result = new(
        SandboxPaths.ResultFile,
        "{\"metadataId\":0}"u8.ToArray()
    );

    private static async Task<(SandboxRunResponse, FakeBackend)> RunAsync(
        SandboxExecution execution,
        Action<Launcher.Configuration.SandboxLauncherOptions>? configure = null
    )
    {
        var options = LauncherOptions.Podman();
        configure?.Invoke(options);
        var backend = new FakeBackend { Run = _ => Task.FromResult(execution) };
        var runner = new SandboxRunner(
            backend,
            options,
            new SandboxGate(1),
            NullLogger<SandboxRunner>.Instance
        );
        return (
            await runner
                .RunAsync(Request, options.Profiles["escape"], CancellationToken.None)
                .ConfigureAwait(false),
            backend
        );
    }

    [Test]
    public async Task RunAsync_ExitedWithAResult_CompletesWithOnlyTheFilesUnderFiles()
    {
        var (response, backend) = await RunAsync(
            new SandboxExecution(
                SandboxEnd.Exited,
                0,
                [Result, new("files/a.txt", "a"u8.ToArray()), new("stray.txt", "s"u8.ToArray())],
                OutputRefused: null
            )
        );

        response.Outcome.Should().Be(SandboxOutcome.Completed);
        response.ResultJson.Should().Be("{\"metadataId\":0}");
        using var tar = new MemoryStream(Convert.FromBase64String(response.Archive ?? ""));
        var (files, refused) = await SandboxArchive.ReadAsync(
            tar,
            new SandboxArchiveLimits(1024, 10),
            "",
            CancellationToken.None
        );
        refused.Should().BeNull();
        files.Select(f => f.Path).Should().Equal("a.txt");
        backend.Jobs.Single().Input.Keys.Should().Equal(SandboxPaths.RequestFile);
        backend.Jobs.Single().Profile.Image.Should().Be(LauncherOptions.Image);
    }

    [Test]
    public async Task RunAsync_NoFiles_HasNoArchive()
    {
        var (response, _) = await RunAsync(
            new SandboxExecution(SandboxEnd.Exited, 0, [Result], OutputRefused: null)
        );

        response.Outcome.Should().Be(SandboxOutcome.Completed);
        response.Archive.Should().BeNull();
    }

    [TestCase(nameof(SandboxEnd.TimedOut), SandboxOutcome.TimedOut, "30 second timeout")]
    [TestCase(nameof(SandboxEnd.OutOfMemory), SandboxOutcome.OutOfMemory, "192 MiB of memory")]
    [TestCase(nameof(SandboxEnd.DiskExceeded), SandboxOutcome.Exited, "8 MiB of output")]
    [TestCase(nameof(SandboxEnd.Failed), SandboxOutcome.Failed, "could not run the sandbox")]
    [TestCase(
        nameof(SandboxEnd.TooManyEntries),
        SandboxOutcome.OutputRefused,
        "more than 10000 entries"
    )]
    public async Task RunAsync_Killed_FailsWithTheReasonAndNoOutput(
        string ended,
        SandboxOutcome outcome,
        string reason
    )
    {
        var end = Enum.Parse<SandboxEnd>(ended);
        // Even when the sandbox left a result behind, a killed run hands nothing back.
        var (response, _) = await RunAsync(
            new SandboxExecution(end, ExitCode: null, [Result], OutputRefused: null)
        );

        response.Outcome.Should().Be(outcome);
        response.Reason.Should().Contain(reason);
        response.ResultJson.Should().BeNull(Adr);
        response.Archive.Should().BeNull();
    }

    [Test]
    public async Task RunAsync_OutputRefused_SaysWhy()
    {
        var (response, _) = await RunAsync(
            new SandboxExecution(SandboxEnd.Exited, 0, [], "'files/link' is a symbolic link")
        );

        response.Outcome.Should().Be(SandboxOutcome.OutputRefused);
        response.Reason.Should().Contain("symbolic link");
    }

    [Test]
    public async Task RunAsync_NonZeroExit_IsExitedEvenWithAResult()
    {
        var (response, _) = await RunAsync(
            new SandboxExecution(SandboxEnd.Exited, 137, [Result], OutputRefused: null)
        );

        response.Outcome.Should().Be(SandboxOutcome.Exited);
        response.Reason.Should().Contain("code 137").And.Contain("under Kata");
    }

    [Test]
    public async Task RunAsync_NoResult_IsExited()
    {
        var (response, _) = await RunAsync(
            new SandboxExecution(SandboxEnd.Exited, 0, [], OutputRefused: null)
        );

        response.Outcome.Should().Be(SandboxOutcome.Exited);
        response.Reason.Should().Contain("left no result");
    }

    [Test]
    public async Task RunAsync_ResultNotJson_IsExited()
    {
        var (response, _) = await RunAsync(
            new SandboxExecution(
                SandboxEnd.Exited,
                0,
                [new(SandboxPaths.ResultFile, "not json"u8.ToArray())],
                OutputRefused: null
            )
        );

        response.Outcome.Should().Be(SandboxOutcome.Exited);
    }

    [Test]
    public async Task RunAsync_ResultOverItsCap_IsExited()
    {
        var big = new SandboxFile(
            SandboxPaths.ResultFile,
            Encoding.UTF8.GetBytes("\"" + new string('x', 100) + "\"")
        );
        var (response, _) = await RunAsync(
            new SandboxExecution(SandboxEnd.Exited, 0, [big], OutputRefused: null),
            o => o.Caps.MaxResultBytes = 50
        );

        response.Outcome.Should().Be(SandboxOutcome.Exited);
    }

    [Test]
    public async Task RunAsync_FilesOverTheArchiveCap_AreRefused()
    {
        var (response, _) = await RunAsync(
            new SandboxExecution(
                SandboxEnd.Exited,
                0,
                [Result, new("files/a", new byte[100])],
                OutputRefused: null
            ),
            o => o.Caps.MaxArchiveBytes = 50
        );

        response.Outcome.Should().Be(SandboxOutcome.OutputRefused);
    }

    [Test]
    public async Task RunAsync_GateFull_IsBusyAndStartsNothing()
    {
        var options = LauncherOptions.Podman();
        var backend = new FakeBackend();
        var gate = new SandboxGate(1);
        using var taken = gate.TryEnter();
        var runner = new SandboxRunner(backend, options, gate, NullLogger<SandboxRunner>.Instance);

        var response = await runner.RunAsync(
            Request,
            options.Profiles["escape"],
            CancellationToken.None
        );

        response.Outcome.Should().Be(SandboxOutcome.Busy);
        backend.Jobs.Should().BeEmpty();
    }
}
