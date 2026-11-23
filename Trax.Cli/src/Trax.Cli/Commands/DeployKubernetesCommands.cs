using System.Globalization;
using Trax.Cli.Deploy;
using Trax.Cli.Deploy.Kubernetes;
using Trax.Core.Extensions;

namespace Trax.Cli.Commands;

/// <summary>
/// The Kubernetes half of <c>trax deploy</c>: deploying a kubernetes environment, and its <c>plan</c>, <c>status</c>,
/// <c>logs</c> and <c>destroy</c>. The subcommands are the ones <see cref="DeployAwsCommands"/> defines; they come here
/// when the environment's target is kubernetes. The cluster is the current kubeconfig context's.
/// </summary>
internal static class DeployKubernetesCommands
{
    /// <summary>The clock image tags are read from; tests swap in a fixed one.</summary>
    internal static TimeProvider Clock { get; set; } = TimeProvider.System;

    /// <summary>How the commands reach the cluster; tests swap in a fake.</summary>
    internal static Func<IKubernetesDeployClient> Client { get; set; } =
        () => new KubernetesDeployClient(Clock);

    internal static IContainerImagePublisher Images { get; set; } = new DotnetContainerPublisher();

    internal static async Task<int> DeployAsync(
        string filePath,
        string environment,
        IHostExporter exporter,
        TextWriter stdout,
        TextWriter stderr,
        CancellationToken ct
    ) =>
        await GuardAsync(
                stderr,
                async () =>
                {
                    var file = await DeployFile.ReadAsync(filePath).ConfigureAwait(false);
                    file.Environment(environment);
                    var exports = await DeployCommand
                        .ExportAsync(file, filePath, exporter, stdout)
                        .ConfigureAwait(false);
                    var tag = await ImageTags
                        .CreateAsync(
                            Path.GetDirectoryName(Path.GetFullPath(filePath)).NotNull(),
                            Clock.GetUtcNow().UtcDateTime
                        )
                        .ConfigureAwait(false);
                    await new KubernetesDeployer(Client(), Images, stdout.WriteLine)
                        .DeployAsync(file, filePath, environment, exports, tag, ct)
                        .ConfigureAwait(false);
                    await stdout
                        .WriteLineAsync(
                            $"Deployed {file.App} to {environment}. Until it is exposed, reach a hub with 'kubectl "
                                + $"port-forward -n {Namespace(file, environment)} service/<hub> 8080'."
                        )
                        .ConfigureAwait(false);
                }
            )
            .ConfigureAwait(false);

    /// <summary>
    /// Writes the manifests a deploy would apply under <c>.trax/deploy/kubernetes/&lt;app&gt;-&lt;env&gt;/</c>,
    /// without reaching a cluster. The images are named with the tag a deploy would give them now.
    /// </summary>
    internal static async Task<int> PlanAsync(
        string filePath,
        string environment,
        IHostExporter exporter,
        TextWriter stdout,
        TextWriter stderr,
        CancellationToken ct
    ) =>
        await GuardAsync(
                stderr,
                async () =>
                {
                    var file = await DeployFile.ReadAsync(filePath).ConfigureAwait(false);
                    file.Environment(environment);
                    var exports = await DeployCommand
                        .ExportAsync(file, filePath, exporter, stdout)
                        .ConfigureAwait(false);
                    var root = Path.GetDirectoryName(Path.GetFullPath(filePath)).NotNull();
                    var tag = await ImageTags
                        .CreateAsync(root, Clock.GetUtcNow().UtcDateTime)
                        .ConfigureAwait(false);
                    var manifests = KubernetesTarget.Render(file, environment, exports, tag);
                    var dir = Path.Combine(
                        root,
                        ".trax",
                        "deploy",
                        "kubernetes",
                        file.StackName(environment)
                    );
                    await WritePlanAsync(dir, manifests, stdout, ct).ConfigureAwait(false);
                }
            )
            .ConfigureAwait(false);

    private static async Task WritePlanAsync(
        string dir,
        KubernetesManifests manifests,
        TextWriter stdout,
        CancellationToken ct
    )
    {
        Directory.CreateDirectory(dir);
        foreach (var old in Directory.EnumerateFiles(dir, "*.yaml"))
            File.Delete(old);
        foreach (var (name, content) in KubernetesTarget.Yaml(manifests))
            await File.WriteAllTextAsync(Path.Combine(dir, name), content, ct)
                .ConfigureAwait(false);
        await stdout.WriteLineAsync($"Wrote {dir}:").ConfigureAwait(false);
        foreach (var o in manifests.All)
            await stdout.WriteLineAsync($"  {o.Kind, -14}{o.Metadata.Name}").ConfigureAwait(false);
        if (manifests.SecretKeys.Count > 0)
            await stdout
                .WriteLineAsync(
                    $"They read {string.Join(", ", manifests.SecretKeys)} from the Secret "
                        + $"{manifests.SecretName} in namespace {manifests.NamespaceName}, which trax does not write."
                )
                .ConfigureAwait(false);
    }

    internal static async Task<int> StatusAsync(
        string filePath,
        string environment,
        TextWriter stdout,
        TextWriter stderr,
        CancellationToken ct
    ) =>
        await GuardAsync(
                stderr,
                async () =>
                {
                    var file = await DeployFile.ReadAsync(filePath).ConfigureAwait(false);
                    file.Environment(environment);
                    var ns = Namespace(file, environment);
                    var cluster = Client();
                    if (await cluster.NamespaceLabelsAsync(ns, ct).ConfigureAwait(false) is null)
                    {
                        await stdout
                            .WriteLineAsync(
                                $"Namespace {ns} does not exist in context {cluster.Context}."
                            )
                            .ConfigureAwait(false);
                        return;
                    }
                    await stdout
                        .WriteLineAsync($"{ns} in context {cluster.Context}:")
                        .ConfigureAwait(false);
                    foreach (
                        var w in await cluster
                            .WorkloadsAsync(ns, file.App, ct)
                            .ConfigureAwait(false)
                    )
                        await stdout
                            .WriteLineAsync(
                                string.Create(
                                    CultureInfo.InvariantCulture,
                                    $"  {w.Name, -16}{w.Ready}/{w.Desired} ready  {w.Image}"
                                )
                            )
                            .ConfigureAwait(false);
                }
            )
            .ConfigureAwait(false);

    internal static async Task<int> LogsAsync(
        string filePath,
        string environment,
        string service,
        bool follow,
        int sinceMinutes,
        TextWriter stdout,
        TextWriter stderr,
        CancellationToken ct
    ) =>
        await GuardAsync(
                stderr,
                async () =>
                {
                    var file = await DeployFile.ReadAsync(filePath).ConfigureAwait(false);
                    file.Environment(environment);
                    string[] readable =
                    [
                        .. file.Services.Keys,
                        KubernetesTarget.MigrateName,
                        KubernetesTarget.Database,
                        KubernetesTarget.Broker,
                    ];
                    if (!readable.Contains(service, StringComparer.Ordinal))
                        throw new InvalidOperationException(
                            $"'{service}' is not a service. Read one of: {string.Join(", ", readable)}."
                        );
                    await foreach (
                        var line in Client()
                            .LogsAsync(
                                Namespace(file, environment),
                                file.App,
                                service,
                                follow,
                                sinceMinutes,
                                ct
                            )
                            .ConfigureAwait(false)
                    )
                        await stdout.WriteLineAsync(line).ConfigureAwait(false);
                }
            )
            .ConfigureAwait(false);

    internal static string DestroyPrompt(DeployFile file, string environment, bool deleteData)
    {
        var ns = Namespace(file, environment);
        return $"This deletes {file.App}'s services in namespace {ns}"
            + (
                deleteData
                    ? ", and the in-cluster database with every row in it."
                    : ". An in-cluster database's volume is kept; --delete-data deletes it."
            )
            + $" Type {ns} to go on: ";
    }

    internal static async Task<int> DestroyAsync(
        string filePath,
        string environment,
        bool deleteData,
        bool yes,
        Func<string?> readLine,
        TextWriter stdout,
        TextWriter stderr,
        CancellationToken ct
    ) =>
        await GuardAsync(
                stderr,
                async () =>
                {
                    var file = await DeployFile.ReadAsync(filePath).ConfigureAwait(false);
                    file.Environment(environment);
                    if (!yes)
                    {
                        await stdout
                            .WriteAsync(DestroyPrompt(file, environment, deleteData))
                            .ConfigureAwait(false);
                        if (
                            !string.Equals(
                                readLine()?.Trim(),
                                Namespace(file, environment),
                                StringComparison.Ordinal
                            )
                        )
                            throw new InvalidOperationException("Nothing was deleted.");
                    }
                    await new KubernetesDeployer(Client(), Images, stdout.WriteLine)
                        .DestroyAsync(file, environment, deleteData, ct)
                        .ConfigureAwait(false);
                }
            )
            .ConfigureAwait(false);

    private static string Namespace(DeployFile file, string environment) =>
        KubernetesTarget.NamespaceName(file, environment, file.Environment(environment));

    private static async Task<int> GuardAsync(TextWriter stderr, Func<Task> action)
    {
        try
        {
            await action().ConfigureAwait(false);
            return 0;
        }
        catch (Exception ex)
            when (DeployCommand.Reported(ex)
                || ex
                    is k8s.Exceptions.KubeConfigException
                        or k8s.Exceptions.KubernetesClientException
                        or HttpRequestException
            )
        {
            await stderr.WriteLineAsync(ex.Message).ConfigureAwait(false);
            return 1;
        }
        catch (k8s.Autorest.HttpOperationException ex)
        {
            await stderr
                .WriteLineAsync(
                    $"The cluster refused: {ex.Response.StatusCode} {ex.Response.Content}".TrimEnd()
                )
                .ConfigureAwait(false);
            return 1;
        }
    }
}
