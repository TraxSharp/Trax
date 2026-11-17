using Trax.Cli.Deploy;
using Trax.Cli.Deploy.Kubernetes;

namespace Trax.Cli.Commands;

/// <summary>
/// The Kubernetes half of <c>trax deploy</c>: deploying a kubernetes environment, and its <c>plan</c>, <c>status</c>,
/// <c>logs</c> and <c>destroy</c>. The subcommands are the ones <see cref="DeployAwsCommands"/> defines; they come here
/// when the environment's target is kubernetes. The cluster is the current kubeconfig context's.
/// </summary>
internal static class DeployKubernetesCommands
{
    /// <summary>How the commands reach the cluster; tests swap in a fake.</summary>
    internal static Func<IKubernetesDeployClient> Client { get; set; } =
        () => new KubernetesDeployClient();

    internal static IContainerImagePublisher Images { get; set; } = new DotnetContainerPublisher();

    internal static async Task<int> DeployAsync(
        string filePath,
        string environment,
        IHostExporter exporter,
        CancellationToken ct
    ) =>
        await Guard(async () =>
        {
            var file = DeployFile.Read(filePath);
            file.Environment(environment);
            var exports = DeployCommand.Export(file, filePath, exporter);
            var tag = ImageTags.Create(
                Path.GetDirectoryName(Path.GetFullPath(filePath))!,
                DateTime.UtcNow
            );
            await new KubernetesDeployer(Client(), Images, Console.WriteLine).DeployAsync(
                file,
                filePath,
                environment,
                exports,
                tag,
                ct
            );
            Console.WriteLine(
                $"Deployed {file.App} to {environment}. Until it is exposed, reach a hub with 'kubectl "
                    + $"port-forward -n {Namespace(file, environment)} service/<hub> 8080'."
            );
        });

    /// <summary>
    /// Writes the manifests a deploy would apply under <c>.trax/deploy/kubernetes/&lt;app&gt;-&lt;env&gt;/</c>,
    /// without reaching a cluster. The images are named with the tag a deploy would give them now.
    /// </summary>
    internal static async Task<int> PlanAsync(
        string filePath,
        string environment,
        IHostExporter exporter,
        CancellationToken ct
    ) =>
        await Guard(() =>
        {
            ct.ThrowIfCancellationRequested();
            var file = DeployFile.Read(filePath);
            file.Environment(environment);
            var exports = DeployCommand.Export(file, filePath, exporter);
            var root = Path.GetDirectoryName(Path.GetFullPath(filePath))!;
            var manifests = KubernetesTarget.Render(
                file,
                environment,
                exports,
                ImageTags.Create(root, DateTime.UtcNow)
            );
            var dir = Path.Combine(
                root,
                ".trax",
                "deploy",
                "kubernetes",
                file.StackName(environment)
            );
            Directory.CreateDirectory(dir);
            foreach (var old in Directory.EnumerateFiles(dir, "*.yaml"))
                File.Delete(old);
            foreach (var (name, content) in KubernetesTarget.Yaml(manifests))
                File.WriteAllText(Path.Combine(dir, name), content);
            Console.WriteLine($"Wrote {dir}:");
            foreach (var o in manifests.All)
                Console.WriteLine($"  {o.Kind, -14}{o.Metadata.Name}");
            if (manifests.SecretKeys.Count > 0)
                Console.WriteLine(
                    $"They read {string.Join(", ", manifests.SecretKeys)} from the Secret "
                        + $"{manifests.SecretName} in namespace {manifests.NamespaceName}, which trax does not write."
                );
            return Task.CompletedTask;
        });

    internal static async Task<int> StatusAsync(
        string filePath,
        string environment,
        CancellationToken ct
    ) =>
        await Guard(async () =>
        {
            var file = DeployFile.Read(filePath);
            file.Environment(environment);
            var ns = Namespace(file, environment);
            var cluster = Client();
            if (await cluster.NamespaceLabelsAsync(ns, ct) is null)
            {
                Console.WriteLine($"Namespace {ns} does not exist in context {cluster.Context}.");
                return;
            }
            Console.WriteLine($"{ns} in context {cluster.Context}:");
            foreach (var w in await cluster.WorkloadsAsync(ns, file.App, ct))
                Console.WriteLine($"  {w.Name, -16}{w.Ready}/{w.Desired} ready  {w.Image}");
        });

    internal static async Task<int> LogsAsync(
        string filePath,
        string environment,
        string service,
        bool follow,
        int sinceMinutes,
        CancellationToken ct
    ) =>
        await Guard(async () =>
        {
            var file = DeployFile.Read(filePath);
            file.Environment(environment);
            string[] readable =
            [
                .. file.Services.Keys,
                KubernetesTarget.MigrateName,
                KubernetesTarget.Database,
                KubernetesTarget.Broker,
            ];
            if (!readable.Contains(service))
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
            )
                Console.WriteLine(line);
        });

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
        CancellationToken ct
    ) =>
        await Guard(async () =>
        {
            var file = DeployFile.Read(filePath);
            file.Environment(environment);
            if (!yes)
            {
                Console.Write(DestroyPrompt(file, environment, deleteData));
                if (readLine()?.Trim() != Namespace(file, environment))
                    throw new InvalidOperationException("Nothing was deleted.");
            }
            await new KubernetesDeployer(Client(), Images, Console.WriteLine).DestroyAsync(
                file,
                environment,
                deleteData,
                ct
            );
        });

    private static string Namespace(DeployFile file, string environment) =>
        KubernetesTarget.NamespaceName(file, environment, file.Environment(environment));

    private static async Task<int> Guard(Func<Task> action)
    {
        try
        {
            await action();
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
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
        catch (k8s.Autorest.HttpOperationException ex)
        {
            Console.Error.WriteLine(
                $"The cluster refused: {ex.Response.StatusCode} {ex.Response.Content}".TrimEnd()
            );
            return 1;
        }
    }
}
