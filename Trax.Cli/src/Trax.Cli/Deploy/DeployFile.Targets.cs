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
        CheckKubernetesDatabase(env, problem);
        foreach (var secret in Secrets)
            if (!SecretKey.IsMatch(secret))
                throw new InvalidOperationException(
                    problem($"secret '{secret}' cannot be a key of a Kubernetes Secret.")
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
