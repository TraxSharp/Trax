using System.Text.RegularExpressions;

namespace Trax.Cli.Deploy;

internal sealed partial class DeployFile
{
    private void CheckTarget(string name, DeployEnvironment env, Func<string, string> problem)
    {
        switch (env.Target)
        {
            case DeployEnvironment.Aws:
                CheckAws(env, problem);
                CheckAwsNetworkAndAuth(env, problem);
                RefuseEgressOnEcs(problem);
                break;
            case DeployEnvironment.Compose:
                CheckCompose(env, problem);
                break;
            case DeployEnvironment.Kubernetes:
                CheckKubernetes(name, env, problem);
                break;
            case null:
                throw new InvalidOperationException(
                    problem("it names no target. Give it \"target\": compose, aws or kubernetes.")
                );
            default:
                throw new InvalidOperationException(
                    problem(
                        $"target '{env.Target}' is not one trax deploys to. Use compose, aws or kubernetes."
                    )
                );
        }
    }

    private static void CheckAws(DeployEnvironment env, Func<string, string> problem)
    {
        NoExposure(
            env,
            problem,
            "aws serves through its own public load balancer, and a tailnet exposure is for a kubernetes environment"
        );
        NotFor(
            problem,
            "aws",
            ("engine", env.Engine),
            ("namespace", env.Namespace),
            ("runtime", env.Runtime)
        );
        NotFor(problem, "aws", ("registry", env.Registry));
        if (env.Database is not null)
            throw new InvalidOperationException(
                problem(
                    "database is for a kubernetes environment. On aws, name an existing database with "
                        + "databaseSecretArn, or leave it out for the stack to create one."
                )
            );
        if (string.IsNullOrWhiteSpace(env.Region))
            throw new InvalidOperationException(problem("it names no region (us-west-2)."));
        if (
            string.Equals(env.Profile, DeployEnvironment.Production, StringComparison.Ordinal)
            && string.IsNullOrWhiteSpace(env.Domain)
        )
            throw new InvalidOperationException(
                problem(
                    "the production profile needs a domain: HTTPS needs a certificate, and a certificate needs a "
                        + "domain. Name one, or use the try profile, which serves on a CloudFront address."
                )
            );
        if (
            string.Equals(env.Profile, DeployEnvironment.Try, StringComparison.Ordinal)
            && env.Domain is not null
        )
            throw new InvalidOperationException(
                problem("the try profile serves on a CloudFront address and takes no domain.")
            );
    }

    /// <summary>
    /// ECS refuses <c>egress</c>: Fargate gives a task no NET_ADMIN, so the egress image cannot apply its rules, and a
    /// security group filters by address, not by name. The kubernetes target on EKS limits a service on AWS.
    /// </summary>
    private void RefuseEgressOnEcs(Func<string, string> problem)
    {
        foreach (var (service, entry) in Services)
            if (entry.Egress is not null)
                throw new InvalidOperationException(
                    problem(
                        $"service '{service}' names egress, which aws (ECS Fargate) cannot hold it to: Fargate "
                            + "gives a task no NET_ADMIN, so the egress rules cannot be applied, and a security "
                            + "group filters by address, not by domain. Deploy it with \"target\": \"kubernetes\" "
                            + "on EKS (\"aws\": { \"cluster\": ... }), or leave egress out of a file that deploys "
                            + "to ECS."
                    )
                );
    }

    private static void CheckAwsNetworkAndAuth(DeployEnvironment env, Func<string, string> problem)
    {
        if (env.Network is { } network)
        {
            if (string.IsNullOrWhiteSpace(network.VpcId) || string.IsNullOrWhiteSpace(network.Cidr))
                throw new InvalidOperationException(
                    problem("network needs vpcId and cidr (the VPC's address range).")
                );
            if (network.PublicSubnets.Count < 2 || network.PrivateSubnets.Count < 2)
                throw new InvalidOperationException(
                    problem(
                        "network needs at least two public and two private subnets, in different availability "
                            + "zones: a load balancer and a database each span two."
                    )
                );
        }
        if (
            env.Auth is { } a
            && !string.Equals(a.Kind, DeployAuth.Cognito, StringComparison.Ordinal)
        )
            throw new InvalidOperationException(
                problem(
                    $"auth kind '{a.Kind}' is not one trax creates. Use cognito, or leave auth out and give an "
                        + "existing issuer's address in settings."
                )
            );
    }

    private static void CheckCompose(DeployEnvironment env, Func<string, string> problem)
    {
        NoExposure(
            env,
            problem,
            "a compose stack publishes its hub on 127.0.0.1, and a tailnet exposure is for a kubernetes environment"
        );
        NotForCloud(env, problem, "compose");
        NotFor(
            problem,
            "compose",
            ("namespace", env.Namespace),
            ("registry", env.Registry),
            ("runtime", env.Runtime)
        );
        if (env.Database is not null)
            throw new InvalidOperationException(
                problem("database is for a kubernetes environment; compose runs its own Postgres.")
            );
        if (env.Engine is not (null or DeployEnvironment.Podman or DeployEnvironment.Docker))
            throw new InvalidOperationException(
                problem($"engine '{env.Engine}' is not podman or docker.")
            );
        if (env.Settings.Count > 0)
            throw new InvalidOperationException(
                problem(
                    "settings are for aws and kubernetes. A compose stack reads its settings from the .env beside "
                        + "it."
                )
            );
        if (env.Counts.Count > 0)
            throw new InvalidOperationException(
                problem(
                    "counts are for aws and kubernetes. A compose stack runs each service's count."
                )
            );
    }

    private void CheckKubernetes(string name, DeployEnvironment env, Func<string, string> problem)
    {
        NotForCloud(env, problem, "kubernetes");
        NotFor(problem, "kubernetes", ("engine", env.Engine));
        CheckRuntime(env, problem);
        CheckExposure(env, problem);
        if (env.Namespace is { } ns && !Dns1123Label.IsMatch(ns))
            throw new InvalidOperationException(
                problem(
                    $"namespace '{ns}' is not a Kubernetes namespace name: at most 63 lower-case letters, digits "
                        + "and dashes, starting and ending with a letter or digit."
                )
            );
        if (env.Namespace is null && !Dns1123Label.IsMatch(StackName(name)))
            throw new InvalidOperationException(
                problem(
                    $"its namespace would be '{StackName(name)}', which is not a Kubernetes namespace name. Name "
                        + "one with \"namespace\"."
                )
            );
        foreach (var service in Services.Keys)
            if (!Dns1123Label.IsMatch(service))
                throw new InvalidOperationException(
                    problem(
                        $"service '{service}' is not a Kubernetes object name: at most 63 characters, ending in a "
                            + "letter or digit."
                    )
                );
        CheckRegistry(env, problem);
        if (env.Eks is { } aws)
            CheckEks(env, aws, problem);
        else
            CheckKubernetesDatabase(env, problem);
        foreach (var secret in Secrets)
            if (!SecretKey.IsMatch(secret))
                throw new InvalidOperationException(
                    problem($"secret '{secret}' cannot be a key of a Kubernetes Secret.")
                );
    }

    private static void NoExposure(DeployEnvironment env, Func<string, string> problem, string why)
    {
        if (env.Exposure is not null || env.TailnetOptions is not null)
            throw new InvalidOperationException(
                problem($"exposure and tailnet are not fields this target reads: {why}.")
            );
    }

    /// <summary>
    /// A kubernetes environment's exposure: none, or <c>tailnet</c>. A tailnet exposure takes the service name
    /// <see cref="Kubernetes.KubernetesTailnet.EdgeName"/> for its proxy, and under podman the Secret key
    /// <see cref="Kubernetes.KubernetesTailnet.AuthKeySecretKey"/> for the Tailscale auth key.
    /// </summary>
    private void CheckExposure(DeployEnvironment env, Func<string, string> problem)
    {
        switch (env.Exposure)
        {
            case null when env.TailnetOptions is not null:
                throw new InvalidOperationException(
                    problem("tailnet names a tailnet exposure, and exposure is not \"tailnet\".")
                );
            case null:
                return;
            case DeployEnvironment.Public:
                throw new InvalidOperationException(
                    problem(
                        "exposure \"public\" is not offered yet. Use \"tailnet\", or leave exposure out and reach the "
                            + "hub with kubectl port-forward."
                    )
                );
            case DeployEnvironment.Tailnet:
                break;
            default:
                throw new InvalidOperationException(
                    problem($"exposure '{env.Exposure}' is not one trax renders. Use \"tailnet\".")
                );
        }
        CheckTailnet(env, problem);
    }

    /// <summary>The names a tailnet exposure takes, and the ones it leaves the app.</summary>
    private void CheckTailnet(DeployEnvironment env, Func<string, string> problem)
    {
        var podman = string.Equals(env.Runtime, DeployEnvironment.Podman, StringComparison.Ordinal);
        var options = env.TailnetOptions ?? new DeployEnvironmentTailnet();
        if (options.Hostname is { } host && !Dns1123Label.IsMatch(host))
            throw new InvalidOperationException(
                problem(
                    $"tailnet.hostname '{host}' is not a machine name: at most 63 lower-case letters, digits and "
                        + "dashes, starting and ending with a letter or digit."
                )
            );
        if (options.OperatorNamespace is { } ns && (podman || !Dns1123Label.IsMatch(ns)))
            throw new InvalidOperationException(
                problem(
                    podman
                        ? "tailnet.operatorNamespace is for a cluster. Under podman the hub's pod runs its own "
                            + "Tailscale sidecar."
                        : $"tailnet.operatorNamespace '{ns}' is not a Kubernetes namespace name."
                )
            );
        if (Services.ContainsKey(Kubernetes.KubernetesTailnet.EdgeName))
            throw new InvalidOperationException(
                problem(
                    $"service '{Kubernetes.KubernetesTailnet.EdgeName}' takes the name of the proxy a tailnet "
                        + "exposure runs. Rename the service."
                )
            );
        // The listeners a cluster deploy applies and deletes again would take over a service of their name.
        foreach (
            var listener in new[]
            {
                Kubernetes.KubernetesTailnet.CanaryName,
                Kubernetes.KubernetesTailnet.ControlName,
            }
        )
            if (!podman && Services.ContainsKey(listener))
                throw new InvalidOperationException(
                    problem(
                        $"service '{listener}' takes the name of a listener a tailnet deploy proves the edge's "
                            + "network policy with. Rename the service."
                    )
                );
        if (
            podman
            && Secrets.Contains(
                Kubernetes.KubernetesTailnet.AuthKeySecretKey,
                StringComparer.Ordinal
            )
        )
            throw new InvalidOperationException(
                problem(
                    $"secrets lists {Kubernetes.KubernetesTailnet.AuthKeySecretKey}, which a tailnet exposure under "
                        + "podman reads for its sidecar and never gives the app. Take it out of secrets; .env still "
                        + "holds it."
                )
            );
    }

    /// <summary>
    /// A podman environment runs on one host: there is no namespace (each app gets a network of its own instead), and
    /// its images are built into that host's podman, so there is no registry either.
    /// </summary>
    private static void CheckRuntime(DeployEnvironment env, Func<string, string> problem)
    {
        if (env.Runtime is not (null or DeployEnvironment.Cluster or DeployEnvironment.Podman))
            throw new InvalidOperationException(
                problem($"runtime '{env.Runtime}' is not cluster or podman.")
            );
        if (!string.Equals(env.Runtime, DeployEnvironment.Podman, StringComparison.Ordinal))
            return;
        if (env.Namespace is not null)
            throw new InvalidOperationException(
                problem(
                    "namespace is for a cluster. A podman environment gets a network of its own, named "
                        + "<app>-<environment>."
                )
            );
        if (env.Registry is not null)
            throw new InvalidOperationException(
                problem(
                    "registry is for a cluster. A podman environment's images are built into this host's podman."
                )
            );
    }

    /// <summary>
    /// A kubernetes environment on EKS: the cluster and region are named, the database is the foundation's (so none
    /// is named here), images go to ECR (so no registry), and a network names the cluster's VPC and two or more
    /// private subnets for the database.
    /// </summary>
    private static void CheckEks(
        DeployEnvironment env,
        DeployEnvironmentAws aws,
        Func<string, string> problem
    )
    {
        if (string.Equals(env.Runtime, DeployEnvironment.Podman, StringComparison.Ordinal))
            throw new InvalidOperationException(
                problem("aws is for a cluster, not a podman environment.")
            );
        if (string.IsNullOrWhiteSpace(aws.Region) || string.IsNullOrWhiteSpace(aws.Cluster))
            throw new InvalidOperationException(
                problem(
                    "aws needs region and cluster: the EKS cluster to apply to, which trax never creates."
                )
            );
        if (env.Database is not null)
            throw new InvalidOperationException(
                problem(
                    "database is the foundation's on EKS: trax creates its RDS Postgres. Leave database out."
                )
            );
        if (env.Registry is not null)
            throw new InvalidOperationException(
                problem(
                    "registry is not read on EKS: images go to the environment's ECR repositories."
                )
            );
        if (
            aws.DatabaseAccess
            is not (null or DeployEnvironmentAws.Pods or DeployEnvironmentAws.Nodes)
        )
            throw new InvalidOperationException(
                problem($"aws.databaseAccess '{aws.DatabaseAccess}' is not pods or nodes.")
            );
        if (
            aws.Network is { } network
            && (
                string.IsNullOrWhiteSpace(network.VpcId)
                || string.IsNullOrWhiteSpace(network.Cidr)
                || network.PrivateSubnets.Count < 2
            )
        )
            throw new InvalidOperationException(
                problem(
                    "aws.network needs vpcId, cidr and at least two private subnets in different availability "
                        + "zones, which the database spans. Public subnets are not needed."
                )
            );
    }

    private static void CheckRegistry(DeployEnvironment env, Func<string, string> problem)
    {
        if (
            env.Registry is { } registry
            && (
                registry.Length == 0
                || registry.Contains("://", StringComparison.Ordinal)
                || registry.EndsWith('/')
                || registry.Any(char.IsWhiteSpace)
            )
        )
            throw new InvalidOperationException(
                problem(
                    $"registry '{registry}' is not an image prefix. Give the registry host and path, without a "
                        + "scheme or a trailing slash (ghcr.io/acme)."
                )
            );
    }

    private static void CheckKubernetesDatabase(DeployEnvironment env, Func<string, string> problem)
    {
        switch (env.Database)
        {
            case null:
                throw new InvalidOperationException(
                    problem(
                        "it names no database. Give \"database\": { \"secret\": ..., \"key\": ... } for an "
                            + "existing Postgres, or { \"inCluster\": true } off production."
                    )
                );
            case { InCluster: true, Secret: not null } or { InCluster: true, Key: not null }:
                throw new InvalidOperationException(
                    problem("database names a secret and inCluster both. Choose one.")
                );
            case { InCluster: true }
                when string.Equals(
                    env.Profile,
                    DeployEnvironment.Production,
                    StringComparison.Ordinal
                ):
                throw new InvalidOperationException(
                    problem(
                        "the production profile does not run its database in the cluster: a StatefulSet's "
                            + "Postgres has no backups and no failover. Name an existing database with "
                            + "database.secret and database.key, or use the try profile."
                    )
                );
            case { InCluster: true }:
                break;
            case { Secret: { Length: > 0 } secret, Key: { Length: > 0 } key }
                when Dns1123Subdomain.IsMatch(secret) && SecretKey.IsMatch(key):
                break;
            default:
                throw new InvalidOperationException(
                    problem(
                        "database needs secret and key: the Secret in the namespace that holds the Postgres "
                            + "connection string, and the key it is under."
                    )
                );
        }
    }

    /// <summary>Refuses the fields only an aws environment reads.</summary>
    /// <summary>
    /// Checks the limits an environment sets on its backing services, and refuses a limit its target has no way to
    /// apply: pids on aws and kubernetes (neither counts processes per container), any backing-service limit on aws
    /// (it runs neither service), and a database limit on kubernetes when the database is not in the cluster.
    /// </summary>
    private void CheckEnvironmentLimits(DeployEnvironment env, Func<string, string> problem)
    {
        foreach (var (name, limits) in env.Limits)
        {
            if (name is not (BackingDatabase or BackingBroker))
                throw new InvalidOperationException(
                    problem(
                        $"limits names '{name}', which is not a service trax runs beside the app. Use "
                            + $"{BackingDatabase} or {BackingBroker}; a service of the app takes its limits in its "
                            + "own entry under services."
                    )
                );
            var limit = CheckLimits($"limits.{name}", limits.Memory, limits.Pids, limits.Cpus);
            if (limit is not null)
                throw new InvalidOperationException(problem(limit));
        }
        var aws = string.Equals(env.Target, DeployEnvironment.Aws, StringComparison.Ordinal);
        var kubernetes = string.Equals(
            env.Target,
            DeployEnvironment.Kubernetes,
            StringComparison.Ordinal
        );
        if (aws && env.Limits.Count > 0)
            throw new InvalidOperationException(
                problem(
                    "limits are for compose and kubernetes. On aws the database is RDS, sized by the profile, and "
                        + "there is no broker to limit."
                )
            );
        if (aws || kubernetes)
        {
            var target = aws ? "aws (Fargate)" : "kubernetes";
            foreach (var (service, entry) in Services)
                if (entry.Pids is not null)
                    throw new InvalidOperationException(
                        problem(
                            $"service '{service}' names pids, which {target} cannot apply: it has no "
                                + "per-container process limit (on kubernetes it is the node's podPidsLimit). pids "
                                + "is for compose; a file that also deploys to this target leaves it out."
                        )
                    );
            foreach (var (name, limits) in env.Limits)
                if (limits.Pids is not null)
                    throw new InvalidOperationException(
                        problem(
                            $"limits.{name} names pids, which kubernetes cannot apply: it has no per-container "
                                + "process limit."
                        )
                    );
        }
        if (
            kubernetes
            && env.Limits.ContainsKey(BackingDatabase)
            && env.Database is not { InCluster: true }
        )
            throw new InvalidOperationException(
                problem(
                    $"limits.{BackingDatabase} is for a database trax runs in the cluster (\"database\": "
                        + "{ \"inCluster\": true }); this environment's database is not one."
                )
            );
    }

    /// <summary>The name <c>limits</c> takes for the Postgres trax runs beside the app.</summary>
    public const string BackingDatabase = "database";

    /// <summary>The name <c>limits</c> takes for the RabbitMQ trax runs beside the app.</summary>
    public const string BackingBroker = "rabbitmq";

    private static void NotForCloud(
        DeployEnvironment env,
        Func<string, string> problem,
        string target
    )
    {
        NotFor(
            problem,
            target,
            ("region", env.Region),
            ("domain", env.Domain),
            ("hostedZoneId", env.HostedZoneId),
            ("databaseSecretArn", env.DatabaseSecretArn)
        );
        if (env.Network is not null)
            throw new InvalidOperationException(
                problem($"network is an aws field, and a {target} environment does not read it.")
            );
        if (env.Auth is not null)
            throw new InvalidOperationException(
                problem(
                    $"auth is an aws field: trax creates a user pool only on AWS. A {target} environment names "
                        + "an existing issuer in settings."
                )
            );
    }

    private static void NotFor(
        Func<string, string> problem,
        string target,
        params (string Field, string? Value)[] fields
    )
    {
        foreach (var (field, value) in fields)
            if (!string.IsNullOrEmpty(value))
                throw new InvalidOperationException(
                    problem($"{field} is not a field a {target} environment reads.")
                );
    }

    [GeneratedRegex(
        "^(?=.{1,63}$)[a-z0-9]([-a-z0-9]*[a-z0-9])?$",
        RegexOptions.ExplicitCapture,
        matchTimeoutMilliseconds: 1000
    )]
    private static partial Regex Dns1123Label { get; }

    [GeneratedRegex(
        "^(?=.{1,253}$)[a-z0-9]([-a-z0-9.]*[a-z0-9])?$",
        RegexOptions.ExplicitCapture,
        matchTimeoutMilliseconds: 1000
    )]
    private static partial Regex Dns1123Subdomain { get; }

    [GeneratedRegex("^[-._a-zA-Z0-9]+$", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex SecretKey { get; }
}
