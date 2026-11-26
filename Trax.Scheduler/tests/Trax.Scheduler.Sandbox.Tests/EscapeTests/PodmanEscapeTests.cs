using Trax.Scheduler.Sandbox.Launcher.Core;
using Trax.Scheduler.Sandbox.Tests.Fakes;
using Trax.Scheduler.Services.Sandbox;

namespace Trax.Scheduler.Sandbox.Tests.EscapeTests;

/// <summary>
/// The escape suite on one host, against a real podman (see <see cref="PodmanEscape"/>), plus the
/// canary refusing a plain runtime and a missing one. Podman cannot run Kata 4, which has no OCI
/// command line, so this proves the mechanics under crun or gVisor; one host with Kata is k3s and
/// <see cref="KubernetesEscapeTests"/>.
///
/// <para>The proof behind <c>docs/adr/0022-a-sandbox-runs-in-a-kata-microvm-proved-by-a-startup-canary.md</c>,
/// cited there as a file: a suite CI does not run cannot be claimed as its guard.</para>
/// </summary>
[TestFixture]
public class PodmanEscapeTests : EscapeSuite
{
    private PodmanEscape? _launcher;

    private PodmanEscape Launcher =>
        _launcher ?? throw new InvalidOperationException("not started");

    protected override async Task StartLauncherAsync()
    {
        PodmanEscape.RequireEnvironment();
        _launcher = await PodmanEscape.StartAsync().ConfigureAwait(false);
    }

    protected override async Task StopLauncherAsync()
    {
        if (_launcher is not null)
            await _launcher.DisposeAsync().ConfigureAwait(false);
    }

    protected override byte[] Key => SandboxHosts.Key;

    protected override bool ReportsMemoryKill => true;

    protected override ITraxSandbox Client() => Launcher.Client(Hub);

    protected override HttpClient Raw() => Launcher.Raw();

    protected override Task<int> LeftoversAsync() => Launcher.LeftoversAsync();

    protected override IEnumerable<string> ForbiddenTargets() => ["10.0.2.2:22", "10.88.0.1:22"];

    protected override async Task<(
        ITraxSandbox Client,
        int Cap,
        IAsyncDisposable? Owned
    )> CappedAsync()
    {
        var options = PodmanEscape.DefaultOptions();
        options.Caps.MaxConcurrent = 1;
        var capped = await PodmanEscape.StartAsync(options).ConfigureAwait(false);
        return (capped.Client(Hub), 1, capped);
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
}
