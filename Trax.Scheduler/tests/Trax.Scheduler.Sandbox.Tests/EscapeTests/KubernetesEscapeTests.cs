using k8s;
using Trax.Scheduler.Configuration;
using Trax.Scheduler.Sandbox.Tests.Fakes;
using Trax.Scheduler.Services.Sandbox;

namespace Trax.Scheduler.Sandbox.Tests.EscapeTests;

/// <summary>
/// The escape suite against a launcher running on a cluster with the Kubernetes backend, as the how-to
/// "Run a train in a sandbox" deploys it, with Trax.Scheduler.Sandbox.LauncherHost as the launcher
/// image and the escape image as the profile: Kata through a
/// RuntimeClass, the namespace's deny-all NetworkPolicy and admission policies applied. Read from
/// the environment: <c>TRAX_SANDBOX_LAUNCHER_URL</c> (a port-forward to the launcher's Service),
/// <c>TRAX_SANDBOX_KEY</c> (the launcher's key, base64), <c>TRAX_SANDBOX_FORBIDDEN</c> (comma-separated
/// <c>host:port</c>s a sandbox must not reach: the hub, the database, the API server, the launcher),
/// <c>TRAX_SANDBOX_MAX_CONCURRENT</c> (the launcher's cap) and <c>TRAX_SANDBOX_NAMESPACE</c>, with the
/// default kubeconfig to count what the launcher left behind.
///
/// <para>The proof behind <c>docs/adr/0022-a-sandbox-runs-in-a-kata-microvm-proved-by-a-startup-canary.md</c>,
/// cited there as a file: a suite CI does not run cannot be claimed as its guard.</para>
/// </summary>
[TestFixture]
public class KubernetesEscapeTests : EscapeSuite
{
    private static string? Url => Environment.GetEnvironmentVariable("TRAX_SANDBOX_LAUNCHER_URL");
    private static string? KeyText => Environment.GetEnvironmentVariable("TRAX_SANDBOX_KEY");
    private static string Namespace =>
        Environment.GetEnvironmentVariable("TRAX_SANDBOX_NAMESPACE") ?? "trax-sandbox";

    protected override Task StartLauncherAsync()
    {
        if (string.IsNullOrEmpty(Url) || string.IsNullOrEmpty(KeyText))
            Assert.Ignore(
                "The cluster escape suite needs a launcher on a cluster: set TRAX_SANDBOX_LAUNCHER_URL "
                    + "and TRAX_SANDBOX_KEY (see the how-to, Proving it on your host). CI and trax-test have no cluster."
            );
        return Task.CompletedTask;
    }

    protected override Task StopLauncherAsync() => Task.CompletedTask;

    protected override byte[] Key => Convert.FromBase64String(KeyText ?? "");

    protected override bool ReportsMemoryKill => false;

    protected override ITraxSandbox Client() =>
        SandboxHosts.Client(
            Hub,
            new SocketsHttpHandler(),
            new SandboxClientOptions { LauncherUrl = Url, SigningKey = Key }
        );

    protected override HttpClient Raw() => new() { BaseAddress = new Uri(Url ?? "") };

    protected override async Task<int> LeftoversAsync()
    {
        using var cluster = new Kubernetes(
            KubernetesClientConfiguration.BuildConfigFromConfigFile()
        );
        const string Label = "app.kubernetes.io/managed-by=trax-sandbox-launcher";
        var jobs = await cluster
            .BatchV1.ListNamespacedJobAsync(Namespace, labelSelector: Label)
            .ConfigureAwait(false);
        var pods = await cluster
            .CoreV1.ListNamespacedPodAsync(Namespace, labelSelector: Label)
            .ConfigureAwait(false);
        return jobs.Items.Count + pods.Items.Count;
    }

    protected override IEnumerable<string> ForbiddenTargets() =>
        (Environment.GetEnvironmentVariable("TRAX_SANDBOX_FORBIDDEN") ?? "").Split(
            ',',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
        );

    protected override Task<(
        ITraxSandbox Client,
        int Cap,
        IAsyncDisposable? Owned
    )> CappedAsync() =>
        Task.FromResult<(ITraxSandbox, int, IAsyncDisposable?)>(
            (
                Client(),
                int.Parse(
                    Environment.GetEnvironmentVariable("TRAX_SANDBOX_MAX_CONCURRENT") ?? "4",
                    System.Globalization.CultureInfo.InvariantCulture
                ),
                null
            )
        );
}
