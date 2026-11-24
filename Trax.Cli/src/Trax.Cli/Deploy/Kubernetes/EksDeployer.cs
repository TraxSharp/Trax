using Trax.Cli.Deploy.Aws;
using Trax.Core.Extensions;
using Trax.Effect.Configuration.Deploy;

namespace Trax.Cli.Deploy.Kubernetes;

/// <summary>
/// Puts a kubernetes environment on an EKS cluster it does not create: checks every host reads its secrets from
/// Secrets Manager and the current kubeconfig context is the named cluster, pushes the images to ECR, deploys the AWS
/// foundation (network, database, secrets, the pods' role and their Pod Identity associations), then applies the
/// manifests as on any cluster. Nothing is changed until every check passes.
/// </summary>
internal sealed class EksDeployer(
    IAwsDeployClient aws,
    IEksClient eks,
    IKubernetesDeployClient cluster,
    IImagePublisher images,
    Action<string> say
)
{
    public async Task DeployAsync(
        DeployFile file,
        string filePath,
        string environment,
        IReadOnlyDictionary<string, DeployManifest> exports,
        string tag,
        CancellationToken ct,
        bool force = false
    )
    {
        var env = file.Environment(environment);
        var target = env.Eks.NotNull();
        var name = target.Cluster.NotNull();
        var ns = KubernetesTarget.NamespaceName(file, environment, env);
        CheckHostsReadAwsSecrets(file, exports, environment);

        var found = await eks.DescribeClusterAsync(name, ct).ConfigureAwait(false);
        if (found is null)
        {
            await NoClusterYetAsync(file, environment, target, ns, ct).ConfigureAwait(false);
            return;
        }
        CheckCluster(found, target, environment);
        await CheckSecretsAsync(file, environment, ct).ConfigureAwait(false);

        var stack = file.StackName(environment);
        var pushed = await PushImagesAsync(file, filePath, stack, tag, environment, ct)
            .ConfigureAwait(false);
        var nodes = string.Equals(
            target.DatabaseAccess,
            DeployEnvironmentAws.Nodes,
            StringComparison.Ordinal
        );
        var outputs = await DeployFoundationAsync(
                file,
                environment,
                ns,
                nodes ? found.ClusterSecurityGroup : null,
                ct
            )
            .ConfigureAwait(false);
        if (!string.Equals(Output(outputs, "VpcId"), found.VpcId, StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"Cluster {name} is in {found.VpcId}, and the foundation's database is in {Output(outputs, "VpcId")}, "
                    + "which its pods cannot reach. Give aws.network the cluster's VPC and private subnets."
            );

        var eksTarget = new EksTarget(
            pushed,
            target.Region.NotNull(),
            Output(outputs, "DatabaseAddress"),
            Output(outputs, "DatabasePort"),
            Output(outputs, "DatabaseSecretArn"),
            Output(outputs, "DatabaseClientSecurityGroup"),
            found.ClusterSecurityGroup,
            PodSecurityGroups: !nodes
        );
        await new KubernetesDeployer(cluster, NoImages.Instance, say)
            .DeployAsync(file, filePath, environment, exports, tag, ct, eksTarget, force)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// On EKS the host's secrets reach it only through <c>AddTraxAwsSecretsAsync</c>, so a host that does not call it
    /// would start without its database password; it is refused before anything is pushed or deployed.
    /// </summary>
    internal static void CheckHostsReadAwsSecrets(
        DeployFile file,
        IReadOnlyDictionary<string, DeployManifest> exports,
        string environment
    )
    {
        var missing = file
            .Services.Keys.Where(s =>
                !exports[s].SecretSources.Contains("aws-secrets", StringComparer.Ordinal)
            )
            .ToList();
        if (missing.Count > 0)
            throw new InvalidOperationException(
                $"Environment '{environment}' is on EKS, where a host reads its database password and secrets from "
                    + $"Secrets Manager at start, and {string.Join(", ", missing)} does not. Reference "
                    + "Trax.Effect.Configuration.Aws and call 'await builder.Configuration.AddTraxAwsSecretsAsync();' "
                    + "before the host is built. Nothing was deployed."
            );
    }

    /// <summary>
    /// Refuses a context that is not the named cluster, which would put the app on whatever cluster kubeconfig happens
    /// to point at, and a cluster outside the VPC the environment names.
    /// </summary>
    internal void CheckCluster(EksCluster found, DeployEnvironmentAws target, string environment)
    {
        if (!string.Equals(found.Status, "ACTIVE", StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"Cluster {found.Name} is {found.Status}. Deploy again once it is ACTIVE."
            );
        if (!SameServer(cluster.Server, found.Endpoint))
            throw new InvalidOperationException(
                $"The current kubeconfig context, {cluster.Context}, points at {cluster.Server}, not cluster "
                    + $"{found.Name} ({found.Endpoint}). Run 'aws eks update-kubeconfig --region {target.Region} --name "
                    + $"{found.Name}', then deploy --env {environment} again. Nothing was deployed."
            );
        if (
            target.Network is { } network
            && !string.Equals(network.VpcId, found.VpcId, StringComparison.Ordinal)
        )
            throw new InvalidOperationException(
                $"aws.network names {network.VpcId}, and cluster {found.Name} is in {found.VpcId}. Give the "
                    + "cluster's VPC and private subnets. Nothing was deployed."
            );
    }

    private static bool SameServer(string server, string endpoint) =>
        string.Equals(
            server.TrimEnd('/'),
            endpoint.TrimEnd('/'),
            StringComparison.OrdinalIgnoreCase
        );

    /// <summary>
    /// The foundation creates the VPC when the environment names none, and the cluster is created in it after: the
    /// first deploy deploys the foundation alone and says where to put the cluster.
    /// </summary>
    private async Task NoClusterYetAsync(
        DeployFile file,
        string environment,
        DeployEnvironmentAws target,
        string ns,
        CancellationToken ct
    )
    {
        if (target.Network is not null)
            throw new InvalidOperationException(
                $"There is no EKS cluster {target.Cluster} in {target.Region}. trax never creates one: create it in "
                    + $"{target.Network.VpcId}, then deploy --env {environment} again. Nothing was deployed."
            );
        var outputs = await DeployFoundationAsync(
                file,
                environment,
                ns,
                nodeSecurityGroup: null,
                ct
            )
            .ConfigureAwait(false);
        say(
            $"There is no EKS cluster {target.Cluster} in {target.Region} yet, and trax never creates one. The "
                + $"foundation is ready: create the cluster in {Output(outputs, "VpcId")} on the subnets "
                + $"{Output(outputs, "ServiceSubnets")}, with the EKS Pod Identity Agent add-on and Security Groups "
                + "for Pods (ENABLE_POD_ENI on the VPC CNI) unless aws.databaseAccess is nodes, then deploy --env "
                + $"{environment} again."
        );
    }

    private async Task<IReadOnlyDictionary<string, string>> DeployFoundationAsync(
        DeployFile file,
        string environment,
        string ns,
        string? nodeSecurityGroup,
        CancellationToken ct
    )
    {
        var baseStack = file.FoundationStackName(environment);
        var existing = await aws.DescribeStackAsync(baseStack, ct).ConfigureAwait(false);
        if (existing is { Created: true })
            AwsDeployer.CheckOwner(existing, file, baseStack);
        var template = AwsStack.EksFoundation(file, environment, ns, nodeSecurityGroup);
        var parameters = new Dictionary<string, string?>(StringComparer.Ordinal);
        if (template["Parameters"]?[AwsStack.ProtectParameter] is not null)
            parameters[AwsStack.ProtectParameter] = "true";
        say(
            existing is { Created: true } ? $"Updating {baseStack}..." : $"Creating {baseStack}..."
        );
        await aws.DeployAsync(
                baseStack,
                AwsStack.Compact(template),
                parameters,
                AwsDeployer.StackTags(file, environment, AwsDeployer.FoundationPart),
                existing is { Created: true }
                    ? AwsDeployer.NetworkResources(template)
                    : AwsDeployer.AnyReplaced,
                say,
                ct
            )
            .ConfigureAwait(false);
        return (await aws.DescribeStackAsync(baseStack, ct).ConfigureAwait(false))
            .NotNull()
            .Outputs;
    }

    // Nothing is pushed or deployed until every secret the app lists has a value in Secrets Manager.
    private async Task CheckSecretsAsync(DeployFile file, string environment, CancellationToken ct)
    {
        var stack = file.StackName(environment);
        var stored = (
            await aws.ListSecretsAsync(AwsStack.AppSecretName(stack, ""), ct).ConfigureAwait(false)
        )
            .Select(s => s.Name)
            .ToHashSet(StringComparer.Ordinal);
        var missing = file
            .Secrets.Where(s => !stored.Contains(AwsStack.AppSecretName(stack, s)))
            .ToList();
        if (missing.Count > 0)
            throw new InvalidOperationException(
                $"{stack} has no value for {string.Join(", ", missing)} in Secrets Manager. Set each with 'trax "
                    + $"deploy secret set <name> --env {environment}'. Nothing was deployed."
            );
    }

    private async Task<Dictionary<string, string>> PushImagesAsync(
        DeployFile file,
        string filePath,
        string stack,
        string tag,
        string environment,
        CancellationToken ct
    )
    {
        say($"Pushing images tagged {tag}...");
        var login = await aws.RegistryLoginAsync(ct).ConfigureAwait(false);
        var pushed = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (name, service) in file.Services)
        {
            var repository = await aws.EnsureRepositoryAsync(
                    AwsDeployer.Repository(stack, name),
                    AwsDeployer.Tags(file, environment),
                    ct
                )
                .ConfigureAwait(false);
            say($"  {name} -> {repository}:{tag}");
            await images
                .PublishAsync(DeployFile.ProjectPath(filePath, service), repository, tag, login)
                .ConfigureAwait(false);
            pushed[name] = $"{repository}:{tag}";
        }
        return pushed;
    }

    private static string Output(IReadOnlyDictionary<string, string> outputs, string name) =>
        outputs.TryGetValue(name, out var value)
            ? value
            : throw new InvalidOperationException(
                $"The foundation stack has no output {name}. Deploy again."
            );

    /// <summary>The publisher the Kubernetes deployer is given on EKS, where the images are already in ECR.</summary>
    private sealed class NoImages : IContainerImagePublisher
    {
        public static readonly NoImages Instance = new();

        public Task PublishAsync(
            string projectPath,
            string? registry,
            string repository,
            string tag,
            string? localRuntime = null
        ) =>
            throw new InvalidOperationException(
                "On EKS the images are pushed to ECR before the manifests are applied."
            );
    }
}
