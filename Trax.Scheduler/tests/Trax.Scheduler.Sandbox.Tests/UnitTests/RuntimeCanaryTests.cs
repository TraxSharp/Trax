using Microsoft.Extensions.Logging.Abstractions;
using Trax.Scheduler.Sandbox.Launcher.Backends;
using Trax.Scheduler.Sandbox.Launcher.Core;
using Trax.Scheduler.Sandbox.Tests.Fakes;
using Trax.Scheduler.Services.Sandbox;

namespace Trax.Scheduler.Sandbox.Tests.UnitTests;

/// <summary>
/// The launcher starts sandboxes only after one has proved it runs on a kernel other than the
/// node's and reaches nothing.
///
/// <para>Enforces <c>docs/adr/0022-a-sandbox-runs-in-a-kata-microvm-proved-by-a-startup-canary.md</c>.</para>
/// </summary>
[Property("adr", "docs/adr/0022-a-sandbox-runs-in-a-kata-microvm-proved-by-a-startup-canary.md")]
[TestFixture]
public class RuntimeCanaryTests
{
    private const string Adr =
        "docs/adr/0022-a-sandbox-runs-in-a-kata-microvm-proved-by-a-startup-canary.md";

    private const string Host = "Linux version 7.3.4-1-cachyos (host)";

    private static void Judge(SandboxCanaryReport report, bool loopbackOnly = true) =>
        RuntimeCanary.Judge(report, Host, loopbackOnly, LauncherOptions.Podman());

    [Test]
    public void Judge_GuestKernelAndNothingReached_Passes() =>
        FluentActions
            .Invoking(() => Judge(new("Linux version 6.12.0 (kata guest)", ["lo"], [])))
            .Should()
            .NotThrow();

    [Test]
    public void Judge_TheNodesOwnKernel_Refuses() =>
        FluentActions
            .Invoking(() => Judge(new(Host, ["lo"], [])))
            .Should()
            .Throw<SandboxRuntimeRefusedException>(Adr)
            .WithMessage("*node's own kernel*not isolating*");

    [Test]
    public void Judge_TheNodesOwnKernelWithWhitespace_StillRefuses() =>
        FluentActions
            .Invoking(() => Judge(new(Host + "\n", ["lo"], [])))
            .Should()
            .Throw<SandboxRuntimeRefusedException>();

    [Test]
    public void Judge_NoKernel_Refuses() =>
        FluentActions
            .Invoking(() => Judge(new("  ", ["lo"], [])))
            .Should()
            .Throw<SandboxRuntimeRefusedException>();

    [Test]
    public void Judge_AProbeReached_Refuses() =>
        FluentActions
            .Invoking(() => Judge(new("guest", ["lo"], ["169.254.169.254:80"])))
            .Should()
            .Throw<SandboxRuntimeRefusedException>()
            .WithMessage("*169.254.169.254:80*");

    [Test]
    public void Judge_AnInterfaceOnPodman_Refuses() =>
        FluentActions
            .Invoking(() => Judge(new("guest", ["eth0", "lo"], [])))
            .Should()
            .Throw<SandboxRuntimeRefusedException>()
            .WithMessage("*eth0*");

    [Test]
    public void Judge_AnInterfaceOnACluster_PassesWhenNothingIsReached() =>
        FluentActions
            .Invoking(() => Judge(new("guest", ["eth0", "lo"], []), loopbackOnly: false))
            .Should()
            .NotThrow();

    [Test]
    public async Task CheckAsync_ProbesMetadataOutsideBackendAndConfiguredAddresses()
    {
        var options = LauncherOptions.Podman();
        options.CanaryProbes.Add("10.1.2.3:5432");
        var backend = new RecordingCanaryBackend(new FakeBackend());

        await new RuntimeCanary(
            backend,
            options,
            _ => Task.FromResult(Host),
            NullLogger<RuntimeCanary>.Instance
        ).CheckAsync(CancellationToken.None);

        backend
            .Probes.Should()
            .Contain([
                "169.254.169.254:80",
                "169.254.170.23:80",
                "1.1.1.1:443",
                "10.0.0.1:443",
                "10.1.2.3:5432",
            ]);
    }

    [Test]
    public async Task CheckAsync_RuntimeMissing_Refuses()
    {
        var backend = new FakeBackend();
        var canary = new RuntimeCanary(
            new FailingBackend(backend),
            LauncherOptions.Podman(),
            _ => Task.FromResult(Host),
            NullLogger<RuntimeCanary>.Instance
        );

        await FluentActions
            .Awaiting(() => canary.CheckAsync(CancellationToken.None))
            .Should()
            .ThrowAsync<SandboxRuntimeRefusedException>()
            .WithMessage("*runtime may be missing*");
    }
}
