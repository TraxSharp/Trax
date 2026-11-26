using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Trax.Effect.Utils;
using Trax.Scheduler.Extensions;
using Trax.Scheduler.Sandbox.Image;
using Trax.Scheduler.Sandbox.Tests.Fakes;
using Trax.Scheduler.Services.RunExecutor;
using Trax.Scheduler.Services.Sandbox;

namespace Trax.Scheduler.Sandbox.Tests.UnitTests;

/// <summary>
/// Inside the sandbox: the entry point runs only a train declared for a sandbox, by its exact
/// name, and always leaves a result the launcher can read.
///
/// <para>Enforces <c>docs/adr/0020-a-sandbox-is-started-only-by-the-launcher-from-a-fixed-request.md</c>.</para>
/// </summary>
[Property("adr", "docs/adr/0020-a-sandbox-is-started-only-by-the-launcher-from-a-fixed-request.md")]
[TestFixture]
public class TraxSandboxEntryPointTests
{
    private const string Adr =
        "docs/adr/0020-a-sandbox-is-started-only-by-the-launcher-from-a-fixed-request.md";

    private string _root = "";

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "trax-sbx-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_root, SandboxPaths.Input));
        Directory.CreateDirectory(Path.Combine(_root, SandboxPaths.Output));
        Directory.CreateDirectory(Path.Combine(_root, "scratch"));
    }

    [TearDown]
    public void TearDown() => Directory.Delete(_root, recursive: true);

    private async Task WriteRequestAsync(string trainName, HostileInput input)
    {
        var request = new SandboxRunRequest(
            "0123456789abcdef0123456789abcdef",
            "escape",
            trainName,
            JsonSerializer.Serialize(input, TraxJsonSerializationOptions.ManifestProperties),
            new SandboxLimits(500, 192, 8, 30)
        );
        await File.WriteAllBytesAsync(
                Path.Combine(_root, SandboxPaths.Input, SandboxPaths.RequestFile),
                JsonSerializer.SerializeToUtf8Bytes(request, SandboxJson.Options)
            )
            .ConfigureAwait(false);
    }

    private async Task<(int Exit, RemoteRunResponse Result)> RunAsync()
    {
        var image = SandboxHosts.Image(_root);
        int exit;
        await using (image.ConfigureAwait(false))
            exit = await image.RunTraxSandboxAsync().ConfigureAwait(false);
        var bytes = await File.ReadAllBytesAsync(
                Path.Combine(_root, SandboxPaths.Output, SandboxPaths.ResultFile)
            )
            .ConfigureAwait(false);
        return (
            exit,
            JsonSerializer.Deserialize<RemoteRunResponse>(bytes, RemoteRunJson.Read)
                ?? throw new InvalidOperationException()
        );
    }

    [Test]
    public async Task RunAsync_SandboxedTrain_RunsAndWritesItsOutputAndFiles()
    {
        await WriteRequestAsync(
            typeof(IHostileTrain).FullName ?? "",
            new HostileInput("echo", ["hello"])
        );

        var (exit, result) = await RunAsync();

        exit.Should().Be(0);
        result.IsError.Should().BeFalse(result.ErrorMessage);
        RemoteRunOutput
            .Read(result.OutputJson, typeof(HostileOutput), result.OutputType)
            .Should()
            .BeEquivalentTo(new HostileOutput([], [], "hello"));
        (
            await File.ReadAllTextAsync(
                Path.Combine(_root, SandboxPaths.Output, SandboxPaths.Files, "nested", "again.txt")
            )
        )
            .Should()
            .Be("hello");
    }

    [Test]
    public async Task RunAsync_TrainWithoutTheAttribute_IsRefused()
    {
        await WriteRequestAsync(
            typeof(IUnsandboxedTrain).FullName ?? "",
            new HostileInput("echo", ["hello"])
        );

        var (exit, result) = await RunAsync();

        exit.Should().Be(0);
        result.IsError.Should().BeTrue();
        result.ErrorMessage.Should().Contain("not a registered train marked [TraxSandbox]", Adr);
        File.Exists(Path.Combine(_root, SandboxPaths.Output, SandboxPaths.Files, "echo.txt"))
            .Should()
            .BeFalse("the train never ran");
    }

    [Test]
    public async Task RunAsync_ShortName_IsRefused()
    {
        await WriteRequestAsync(nameof(IHostileTrain), new HostileInput("echo", ["hello"]));

        var (_, result) = await RunAsync();

        result.IsError.Should().BeTrue("a sandbox runs a train by its exact name only");
    }

    [Test]
    public async Task RunAsync_TrainFails_ResultCarriesTheAuthorsMessage()
    {
        await WriteRequestAsync(
            typeof(IHostileTrain).FullName ?? "",
            new HostileInput("fail", ["the input was wrong"])
        );

        var (exit, result) = await RunAsync();

        exit.Should().Be(0);
        result.IsError.Should().BeTrue();
        result.PublicMessage.Should().Be("the input was wrong");
    }

    [Test]
    public async Task RunAsync_NoRequest_ExitsTwoWithAnErrorResult()
    {
        var (exit, result) = await RunAsync();

        exit.Should().Be(2);
        result.IsError.Should().BeTrue();
        result.ErrorMessage.Should().Contain("could not be read");
    }

    [Test]
    public async Task RunAsync_RequestWithAnExtraMember_ExitsTwo()
    {
        await File.WriteAllTextAsync(
            Path.Combine(_root, SandboxPaths.Input, SandboxPaths.RequestFile),
            "{\"runId\":\"0123456789abcdef0123456789abcdef\",\"profile\":\"escape\",\"trainName\":\"x\",\"inputJson\":\"{}\",\"limits\":{\"cpuMillicores\":1,\"memoryMiB\":1,\"diskMiB\":1,\"timeoutSeconds\":1},\"command\":[\"sh\"]}"
        );

        var (exit, _) = await RunAsync();

        exit.Should().Be(2);
    }

    [Test]
    public async Task RunAsync_Canary_ReportsKernelInterfacesAndWhatItReached()
    {
        using var open = new TcpListener(IPAddress.Loopback, 0);
        open.Start();
        var reachable = string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"127.0.0.1:{((IPEndPoint)open.LocalEndpoint).Port}"
        );
        using var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var closed = string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"127.0.0.1:{((IPEndPoint)probe.LocalEndpoint).Port}"
        );
        probe.Stop();
        await File.WriteAllBytesAsync(
            Path.Combine(_root, SandboxPaths.Input, SandboxPaths.CanaryFile),
            JsonSerializer.SerializeToUtf8Bytes(
                new SandboxCanaryRequest([reachable, closed, "not-a-probe"]),
                SandboxJson.Options
            )
        );

        await using var image = SandboxHosts.Image(_root);
        var exit = await image.RunTraxSandboxAsync();

        exit.Should().Be(0);
        var report =
            JsonSerializer.Deserialize<SandboxCanaryReport>(
                await File.ReadAllBytesAsync(
                    Path.Combine(_root, SandboxPaths.Output, SandboxPaths.CanaryReportFile)
                ),
                SandboxJson.Options
            ) ?? throw new InvalidOperationException();
        report.Kernel.Should().Be((await File.ReadAllTextAsync("/proc/version")).Trim());
        report.Interfaces.Should().Contain("lo");
        report.Reached.Should().Equal(reachable);
    }
}
