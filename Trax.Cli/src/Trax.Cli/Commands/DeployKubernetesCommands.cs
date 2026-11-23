using System.Globalization;
using Trax.Cli.Deploy;
using Trax.Cli.Deploy.Aws;
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

    /// <summary>How an environment on EKS reaches its cluster's description, by region; tests swap in a fake.</summary>
    internal static Func<string, IEksClient> Eks { get; set; } = region => new EksClient(region);

    /// <summary>
    /// Where a podman environment's Secrets are written with their values: <c>$XDG_CONFIG_HOME</c>, else
    /// <c>~/.config</c>. Tests point it at a folder of their own.
    /// </summary>
    internal static Func<string> ConfigHome { get; set; } =
        () =>
            Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") is { Length: > 0 } xdg
                ? xdg
                : Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    ".config"
                );

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
                    if (OnPodman(file, environment))
                    {
                        await DeployPodmanAsync(file, filePath, environment, exporter, stdout)
                            .ConfigureAwait(false);
                        return;
                    }
                    var exports = await DeployCommand
                        .ExportAsync(file, filePath, exporter, stdout)
                        .ConfigureAwait(false);
                    var tag = await ImageTags
                        .CreateAsync(
                            Path.GetDirectoryName(Path.GetFullPath(filePath)).NotNull(),
                            Clock.GetUtcNow().UtcDateTime
                        )
                        .ConfigureAwait(false);
                    if (file.Environment(environment).Eks is { } aws)
                        await new EksDeployer(
                            DeployAwsCommands.Client(aws.Region.NotNull()),
                            Eks(aws.Region.NotNull()),
                            Client(),
                            DeployAwsCommands.Images,
                            stdout.WriteLine
                        )
                            .DeployAsync(file, filePath, environment, exports, tag, ct)
                            .ConfigureAwait(false);
                    else
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
                    if (OnPodman(file, environment))
                    {
                        await WritePodmanAsync(
                                PodmanTarget.Render(
                                    file,
                                    environment,
                                    exports,
                                    tag,
                                    PodmanTarget.DeployDirectory(root, file.StackName(environment)),
                                    PodmanTarget.SecretPath(
                                        ConfigHome(),
                                        file.StackName(environment)
                                    )
                                ),
                                PodmanTarget.DeployDirectory(root, file.StackName(environment)),
                                secretYaml: null,
                                secretPath: null,
                                stdout,
                                ct
                            )
                            .ConfigureAwait(false);
                        return;
                    }
                    var dir = Path.Combine(
                        root,
                        ".trax",
                        "deploy",
                        "kubernetes",
                        file.StackName(environment)
                    );
                    var eks = PlannedEks(file, environment, tag);
                    var manifests = KubernetesTarget.Render(file, environment, exports, tag, eks);
                    await WritePlanAsync(dir, manifests, stdout, ct).ConfigureAwait(false);
                    if (eks is not null)
                        await WriteEksFoundationPlanAsync(file, environment, dir, stdout, ct)
                            .ConfigureAwait(false);
                }
            )
            .ConfigureAwait(false);

    /// <summary>
    /// What an EKS plan renders the manifests with, before the foundation exists: each value a deploy reads from AWS
    /// written as the place it comes from.
    /// </summary>
    private static EksTarget? PlannedEks(DeployFile file, string environment, string tag)
    {
        if (file.Environment(environment).Eks is not { } aws)
            return null;
        var stack = file.StackName(environment);
        return new EksTarget(
            file.Services.Keys.ToDictionary(
                s => s,
                s =>
                    $"<account>.dkr.ecr.{aws.Region}.amazonaws.com/{AwsDeployer.Repository(stack, s)}:{tag}",
                StringComparer.Ordinal
            ),
            aws.Region.NotNull(),
            "<foundation DatabaseAddress>",
            "<foundation DatabasePort>",
            "<foundation DatabaseSecretArn>",
            "<foundation DatabaseClientSecurityGroup>",
            "<cluster security group>",
            PodSecurityGroups: !string.Equals(
                aws.DatabaseAccess,
                DeployEnvironmentAws.Nodes,
                StringComparison.Ordinal
            )
        );
    }

    /// <summary>The foundation an EKS deploy creates or updates, beside the manifests.</summary>
    private static async Task WriteEksFoundationPlanAsync(
        DeployFile file,
        string environment,
        string dir,
        TextWriter stdout,
        CancellationToken ct
    )
    {
        var aws = file.Environment(environment).Eks.NotNull();
        var template = AwsStack.EksFoundation(
            file,
            environment,
            Namespace(file, environment),
            string.Equals(aws.DatabaseAccess, DeployEnvironmentAws.Nodes, StringComparison.Ordinal)
                ? "<cluster security group>"
                : null
        );
        var path = Path.Combine(dir, $"{file.FoundationStackName(environment)}.template.json");
        await File.WriteAllTextAsync(path, AwsStack.Indented(template), ct).ConfigureAwait(false);
        await stdout.WriteLineAsync($"Wrote {path}").ConfigureAwait(false);
    }

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
                    NotOnPodman(file, environment);
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
                    NotOnPodman(file, environment);
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
                    NotOnPodman(file, environment);
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
                    // On EKS the foundation goes after the pods, which wore its database client group.
                    if (file.Environment(environment).Eks is { } aws)
                        await new AwsDeployer(
                            DeployAwsCommands.Client(aws.Region.NotNull()),
                            DeployAwsCommands.Images,
                            stdout.WriteLine
                        )
                            .DestroyAsync(file, environment, deleteData, ct)
                            .ConfigureAwait(false);
                }
            )
            .ConfigureAwait(false);

    private static bool OnPodman(DeployFile file, string environment) =>
        string.Equals(
            file.Environment(environment).Runtime,
            DeployEnvironment.Podman,
            StringComparison.Ordinal
        );

    /// <summary>
    /// A podman environment's pods run under systemd on the host it was installed on, which status, logs and destroy
    /// do not reach; each says what does.
    /// </summary>
    private static void NotOnPodman(DeployFile file, string environment)
    {
        if (!OnPodman(file, environment))
            return;
        var stack = file.StackName(environment);
        throw new InvalidOperationException(
            $"'{environment}' runs under podman, through the Quadlet units trax deploy wrote. On that host: "
                + $"'systemctl --user status {stack}-*' for its state, 'journalctl --user -u {stack}-<service>' "
                + $"for a service's log, and to take it down, stop the units, delete them from "
                + $"~/.config/containers/systemd/ and run 'systemctl --user daemon-reload'; 'podman volume rm "
                + $"{stack}-data' then deletes its database."
        );
    }

    /// <summary>
    /// Deploys a podman environment: asks each host what it needs, fills the Secrets from the <c>.env</c> beside
    /// trax.deploy.json (refusing before anything is built when a key has no value), builds the images into this
    /// host's podman, and writes the pod YAML and Quadlet units. It starts nothing; it says how.
    /// </summary>
    private static async Task DeployPodmanAsync(
        DeployFile file,
        string filePath,
        string environment,
        IHostExporter exporter,
        TextWriter stdout
    )
    {
        var root = Path.GetDirectoryName(Path.GetFullPath(filePath)).NotNull();
        var stackName = file.StackName(environment);
        var exports = await DeployCommand
            .ExportAsync(file, filePath, exporter, stdout)
            .ConfigureAwait(false);
        var tag = await ImageTags
            .CreateAsync(root, Clock.GetUtcNow().UtcDateTime)
            .ConfigureAwait(false);
        var dir = PodmanTarget.DeployDirectory(root, stackName);
        var secretPath = PodmanTarget.SecretPath(ConfigHome(), stackName);
        var stack = PodmanTarget.Render(file, environment, exports, tag, dir, secretPath);
        var envFile = Path.Combine(root, ".env");
        var secretYaml = PodmanTarget.SecretYaml(
            stack,
            await DotEnv.ReadAsync(envFile).ConfigureAwait(false),
            envFile
        );
        foreach (var (name, service) in file.Services)
        {
            await stdout
                .WriteLineAsync(
                    $"Building {PodmanTarget.FullImage($"{ComposeTarget.ImageName(file, name)}:{tag}")}..."
                )
                .ConfigureAwait(false);
            await Images
                .PublishAsync(
                    DeployFile.ProjectPath(filePath, service),
                    registry: null,
                    ComposeTarget.ImageName(file, name),
                    tag,
                    DeployEnvironment.Podman
                )
                .ConfigureAwait(false);
        }
        await WritePodmanAsync(stack, dir, secretYaml, secretPath, stdout, CancellationToken.None)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Writes the stack's files under <paramref name="dir"/>, replacing what an earlier run wrote there, and the
    /// Secrets, when given, to <paramref name="secretPath"/> as a file only the user can read.
    /// </summary>
    private static async Task WritePodmanAsync(
        PodmanStack stack,
        string dir,
        string? secretYaml,
        string? secretPath,
        TextWriter stdout,
        CancellationToken ct
    )
    {
        Directory.CreateDirectory(dir);
        foreach (
            var old in Directory
                .EnumerateFiles(dir)
                .Where(f => Path.GetExtension(f) is ".yaml" or ".kube" or ".network")
        )
            File.Delete(old);
        foreach (var (name, content) in stack.Files)
            await File.WriteAllTextAsync(Path.Combine(dir, name), content, ct)
                .ConfigureAwait(false);
        await stdout.WriteLineAsync($"Wrote {dir}.").ConfigureAwait(false);
        if (secretYaml is null || secretPath is null)
        {
            await stdout
                .WriteLineAsync(
                    "A plan builds no image and writes no secret: 'trax deploy --env' does both, then says how to "
                        + "start the units."
                )
                .ConfigureAwait(false);
            return;
        }
        await WritePrivateAsync(secretPath, secretYaml, ct).ConfigureAwait(false);
        await stdout.WriteLineAsync($"Wrote {secretPath} (mode 0600).").ConfigureAwait(false);
        var units = string.Join(' ', stack.Units.Select(u => $"{u}.service"));
        await stdout
            .WriteLineAsync(
                "Install and start it on this host:\n"
                    + $"  mkdir -p ~/.config/containers/systemd && cp {dir}/*.kube {dir}/*.network "
                    + "~/.config/containers/systemd/\n"
                    + "  systemctl --user daemon-reload\n"
                    + $"  systemctl --user restart {units}"
            )
            .ConfigureAwait(false);
    }

    /// <summary>Writes <paramref name="content"/> where only this user can read it: the folder 0700, the file 0600.</summary>
    private static async Task WritePrivateAsync(string path, string content, CancellationToken ct)
    {
        var folder = Path.GetDirectoryName(path).NotNull();
        Directory.CreateDirectory(folder);
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(
                folder,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
            );
        var options = new FileStreamOptions { Mode = FileMode.Create, Access = FileAccess.Write };
        if (!OperatingSystem.IsWindows())
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        var stream = new FileStream(path, options);
        await using (stream.ConfigureAwait(false))
        {
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            var writer = new StreamWriter(stream);
            await using (writer.ConfigureAwait(false))
                await writer.WriteAsync(content.AsMemory(), ct).ConfigureAwait(false);
        }
    }

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
