using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Trax.Cli.Deploy;

/// <summary>
/// <c>trax.deploy.json</c>: the choices a deployment makes that the host cannot report about itself. Which projects
/// are deployed and as which services, how many of each, the names the host reads its connection strings under, and
/// the settings and secrets the deployment must supply. What each host needs (its database, its roles, its
/// endpoints) comes from the host's own export (<c>--trax-export-deploy</c>), not from here.
/// </summary>
internal sealed partial class DeployFile
{
    public const string FileName = "trax.deploy.json";

    /// <summary>
    /// The version written. Version 1 had no <c>target</c> on an environment; every environment it names is
    /// <c>aws</c>, and it is read as such.
    /// </summary>
    public const int CurrentVersion = 2;

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public int Version { get; set; } = CurrentVersion;

    /// <summary>The application's name: lower case, a letter first, then letters, digits or dashes.</summary>
    public string App { get; set; } = "";

    /// <summary>Each deployed project under the name its service gets, such as <c>hub</c> or <c>worker</c>.</summary>
    public SortedDictionary<string, DeployService> Services { get; set; } =
        new(StringComparer.Ordinal);

    /// <summary>The names under <c>ConnectionStrings:</c> the hosts read their connections from.</summary>
    public DeployConnectionStrings ConnectionStrings { get; set; } = new();

    /// <summary>Configuration the deployment must set that is not secret, as environment names (<c>Auth__Authority</c>).</summary>
    public List<string> Settings { get; set; } = [];

    /// <summary>Configuration the deployment must set that is secret, as environment names (<c>Stripe__ApiKey</c>).</summary>
    public List<string> Secrets { get; set; } = [];

    /// <summary>
    /// The environments this app deploys to, by name (<c>production</c>, <c>staging</c>), each on the target it names:
    /// a compose stack, an AWS stack or a Kubernetes namespace.
    /// </summary>
    public SortedDictionary<string, DeployEnvironment> Environments { get; set; } =
        new(StringComparer.Ordinal);

    /// <summary>
    /// The environment <paramref name="name"/>, checked for a deploy to its target. Throws naming the first problem.
    /// </summary>
    public DeployEnvironment Environment(string name)
    {
        if (!Environments.TryGetValue(name, out var env))
            throw new InvalidOperationException(
                Environments.Count == 0
                    ? $"{FileName} names no environments. Add one under \"environments\", or run "
                        + "'trax deploy init --target aws --force'."
                    : $"{FileName} has no environment '{name}'. It has: {string.Join(", ", Environments.Keys)}."
            );
        string Problem(string what) => $"Environment '{name}': {what}";
        if (!EnvironmentName().IsMatch(name))
            throw new InvalidOperationException(
                Problem(
                    "its name must be lower-case letters and digits only. The stack is <app>-<environment>, and "
                        + "a dash here would give two apps the same stack."
                )
            );
        if (env.Profile is not (DeployEnvironment.Production or DeployEnvironment.Try))
            throw new InvalidOperationException(
                Problem($"profile '{env.Profile}' is not production or try.")
            );
        switch (env.Target)
        {
            case DeployEnvironment.Aws:
                CheckAws(env, Problem);
                break;
            case DeployEnvironment.Compose:
                CheckCompose(env, Problem);
                break;
            case DeployEnvironment.Kubernetes:
                CheckKubernetes(name, env, Problem);
                break;
            case null:
                throw new InvalidOperationException(
                    Problem("it names no target. Give it \"target\": compose, aws or kubernetes.")
                );
            default:
                throw new InvalidOperationException(
                    Problem(
                        $"target '{env.Target}' is not one trax deploys to. Use compose, aws or kubernetes."
                    )
                );
        }
        foreach (var service in env.Counts.Keys)
            if (!Services.ContainsKey(service))
                throw new InvalidOperationException(
                    Problem($"counts names '{service}', which is not a service.")
                );
        if (env.Target == DeployEnvironment.Compose)
            return env;
        var provided = env.Auth is { Kind: DeployAuth.Cognito } auth
            ? new HashSet<string>([auth.AuthorityKey, auth.AudienceKey], StringComparer.Ordinal)
            : [];
        var missing = Settings
            .Where(s => !env.Settings.ContainsKey(s) && !provided.Contains(s))
            .ToList();
        if (missing.Count > 0)
            throw new InvalidOperationException(
                Problem(
                    $"settings has no value for {string.Join(", ", missing)}, which the app lists as settings."
                )
            );
        var unknown = env.Settings.Keys.Where(k => !Settings.Contains(k)).ToList();
        if (unknown.Count > 0)
            throw new InvalidOperationException(
                Problem(
                    $"settings gives {string.Join(", ", unknown)}, which the app does not list under \"settings\"."
                )
            );
        return env;
    }

    private static void CheckAws(DeployEnvironment env, Func<string, string> problem)
    {
        NotFor(env, problem, "aws", ("engine", env.Engine), ("namespace", env.Namespace));
        NotFor(env, problem, "aws", ("registry", env.Registry));
        if (env.Database is not null)
            throw new InvalidOperationException(
                problem(
                    "database is for a kubernetes environment. On aws, name an existing database with "
                        + "databaseSecretArn, or leave it out for the stack to create one."
                )
            );
        if (string.IsNullOrWhiteSpace(env.Region))
            throw new InvalidOperationException(problem("it names no region (us-west-2)."));
        if (env.Profile == DeployEnvironment.Production && string.IsNullOrWhiteSpace(env.Domain))
            throw new InvalidOperationException(
                problem(
                    "the production profile needs a domain: HTTPS needs a certificate, and a certificate needs a "
                        + "domain. Name one, or use the try profile, which serves on a CloudFront address."
                )
            );
        if (env.Profile == DeployEnvironment.Try && env.Domain is not null)
            throw new InvalidOperationException(
                problem("the try profile serves on a CloudFront address and takes no domain.")
            );
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
        if (env.Auth is { } a && a.Kind != DeployAuth.Cognito)
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
        NotFor(env, problem, "compose", ("namespace", env.Namespace), ("registry", env.Registry));
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
        NotFor(env, problem, "kubernetes", ("engine", env.Engine));
        if (env.Namespace is { } ns && !Dns1123Label().IsMatch(ns))
            throw new InvalidOperationException(
                problem(
                    $"namespace '{ns}' is not a Kubernetes namespace name: at most 63 lower-case letters, digits "
                        + "and dashes, starting and ending with a letter or digit."
                )
            );
        if (env.Namespace is null && !Dns1123Label().IsMatch(StackName(name)))
            throw new InvalidOperationException(
                problem(
                    $"its namespace would be '{StackName(name)}', which is not a Kubernetes namespace name. Name "
                        + "one with \"namespace\"."
                )
            );
        foreach (var service in Services.Keys)
            if (!Dns1123Label().IsMatch(service))
                throw new InvalidOperationException(
                    problem(
                        $"service '{service}' is not a Kubernetes object name: at most 63 characters, ending in a "
                            + "letter or digit."
                    )
                );
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
            case { InCluster: true } when env.Profile == DeployEnvironment.Production:
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
                when Dns1123Subdomain().IsMatch(secret) && SecretKey().IsMatch(key):
                break;
            default:
                throw new InvalidOperationException(
                    problem(
                        "database needs secret and key: the Secret in the namespace that holds the Postgres "
                            + "connection string, and the key it is under."
                    )
                );
        }
        foreach (var secret in Secrets)
            if (!SecretKey().IsMatch(secret))
                throw new InvalidOperationException(
                    problem($"secret '{secret}' cannot be a key of a Kubernetes Secret.")
                );
    }

    /// <summary>Refuses the fields only an aws environment reads.</summary>
    private static void NotForCloud(
        DeployEnvironment env,
        Func<string, string> problem,
        string target
    )
    {
        NotFor(
            env,
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
        DeployEnvironment env,
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

    /// <summary>The CloudFormation stack an environment deploys to: <c>&lt;app&gt;-&lt;environment&gt;</c>.</summary>
    public string StackName(string environment) => $"{App}-{environment}";

    /// <summary>How many copies of <paramref name="service"/> run in <paramref name="environment"/>.</summary>
    public int Count(DeployEnvironment environment, string service) =>
        environment.Counts.TryGetValue(service, out var count) ? count : Services[service].Count;

    public string Write() => JsonSerializer.Serialize(this, Json) + "\n";

    /// <summary>
    /// Reads the file at <paramref name="path"/> and checks it. Throws <see cref="InvalidOperationException"/> naming
    /// the first problem.
    /// </summary>
    public static DeployFile Read(string path)
    {
        if (!File.Exists(path))
            throw new InvalidOperationException(
                $"No {FileName} at {path}. Run 'trax deploy init' in the directory that holds the solution."
            );
        DeployFile file;
        try
        {
            file =
                JsonSerializer.Deserialize<DeployFile>(File.ReadAllText(path), Json)
                ?? throw new InvalidOperationException($"{path} is empty.");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"{path} is not valid JSON: {ex.Message}", ex);
        }
        file.Check(path);
        return file;
    }

    private void Check(string path)
    {
        string Problem(string what) => $"{path}: {what}";

        if (Version is not (1 or CurrentVersion))
            throw new InvalidOperationException(
                Problem(
                    $"version {Version} is not one this trax reads (1 or {CurrentVersion}). Update the trax tool."
                )
            );
        if (Version == 1)
        {
            // Version 1 deployed every environment to AWS, and had no target to say so.
            foreach (var env in Environments.Values)
                env.Target ??= DeployEnvironment.Aws;
            Version = CurrentVersion;
        }
        if (!Name().IsMatch(App))
            throw new InvalidOperationException(
                Problem(
                    $"app '{App}' must be lower case, start with a letter and hold only letters, digits and dashes."
                )
            );
        if (Services.Count == 0)
            throw new InvalidOperationException(
                Problem("services is empty. Name at least one project to deploy.")
            );
        var dir = Path.GetDirectoryName(Path.GetFullPath(path))!;
        var ports = new Dictionary<int, string>();
        foreach (var (name, service) in Services)
        {
            if (!Name().IsMatch(name) || name is "database" or "rabbitmq" or "migrate")
                throw new InvalidOperationException(
                    Problem(
                        $"service '{name}' must be lower case, start with a letter, hold only letters, digits and "
                            + "dashes, and not be database, rabbitmq or migrate, which the deployment uses itself."
                    )
                );
            if (!File.Exists(Path.Combine(dir, service.Project)))
                throw new InvalidOperationException(
                    Problem(
                        $"service '{name}' names project '{service.Project}', which does not exist."
                    )
                );
            if (service.Port is < 1 or > 65535)
                throw new InvalidOperationException(
                    Problem($"service '{name}' has port {service.Port}, which is not a TCP port.")
                );
            if (service.Port is { } port && !ports.TryAdd(port, name))
                throw new InvalidOperationException(
                    Problem(
                        $"service '{name}' has port {port}, which service '{ports[port]}' has too."
                    )
                );
            if (service.Memory < ServiceHardening.MinimumMemoryMiB)
                throw new InvalidOperationException(
                    Problem(
                        $"service '{name}' has memory {service.Memory}; it must be at least "
                            + $"{ServiceHardening.MinimumMemoryMiB} (MiB)."
                    )
                );
            if (service.Count < 1)
                throw new InvalidOperationException(
                    Problem($"service '{name}' has count {service.Count}; it must be at least 1.")
                );
        }
        foreach (var setting in Settings.Concat(Secrets))
            if (!Variable().IsMatch(setting))
                throw new InvalidOperationException(
                    Problem(
                        $"'{setting}' is not an environment name: use letters, digits and underscores, and __ for "
                            + "a ':' in configuration (Auth__Authority)."
                    )
                );
        var both = Settings.Intersect(Secrets, StringComparer.Ordinal).ToList();
        if (both.Count > 0)
            throw new InvalidOperationException(
                Problem($"'{both[0]}' is listed as both a setting and a secret.")
            );
        foreach (var connection in new[] { ConnectionStrings.Trax, ConnectionStrings.RabbitMq })
            if (!Variable().IsMatch(connection))
                throw new InvalidOperationException(
                    Problem($"connection string name '{connection}' is not a configuration key.")
                );
    }

    /// <summary>The full path of <paramref name="service"/>'s project.</summary>
    public static string ProjectPath(string deployFilePath, DeployService service) =>
        Path.GetFullPath(
            Path.Combine(Path.GetDirectoryName(Path.GetFullPath(deployFilePath))!, service.Project)
        );

    [GeneratedRegex("^[a-z][a-z0-9-]*$")]
    internal static partial Regex Name();

    [GeneratedRegex("^[a-z0-9]+$")]
    private static partial Regex EnvironmentName();

    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]*$")]
    private static partial Regex Variable();

    [GeneratedRegex("^(?=.{1,63}$)[a-z0-9]([-a-z0-9]*[a-z0-9])?$")]
    private static partial Regex Dns1123Label();

    [GeneratedRegex("^(?=.{1,253}$)[a-z0-9]([-a-z0-9.]*[a-z0-9])?$")]
    private static partial Regex Dns1123Subdomain();

    [GeneratedRegex("^[-._a-zA-Z0-9]+$")]
    private static partial Regex SecretKey();
}

/// <summary>One environment: a stack or a namespace of its own, on the target it names.</summary>
internal sealed class DeployEnvironment
{
    public const string Production = "production";
    public const string Try = "try";

    public const string Compose = "compose";
    public const string Aws = "aws";
    public const string Kubernetes = "kubernetes";

    public const string Podman = "podman";
    public const string Docker = "docker";

    /// <summary>Where the environment runs: <c>compose</c>, <c>aws</c> (ECS Fargate) or <c>kubernetes</c>.</summary>
    public string? Target { get; set; }

    /// <summary>The engine a compose environment's stack is written for: <c>podman</c> (the default) or <c>docker</c>.</summary>
    public string? Engine { get; set; }

    /// <summary>A kubernetes environment's namespace. Without it, <c>&lt;app&gt;-&lt;environment&gt;</c>.</summary>
    public string? Namespace { get; set; }

    /// <summary>
    /// The registry a kubernetes environment's images are pushed to and pulled from (<c>ghcr.io/acme</c>). Without
    /// it the images stay in the local container runtime, for a cluster that reads it.
    /// </summary>
    public string? Registry { get; set; }

    /// <summary>A kubernetes environment's Postgres: an existing one named by secret, or one in the cluster.</summary>
    public DeployEnvironmentDatabase? Database { get; set; }

    /// <summary>
    /// On aws, <c>production</c>: private subnets behind a NAT gateway, a domain with its certificate, a Multi-AZ
    /// database kept on delete. <c>try</c>: public subnets locked by security groups, a CloudFront address, a small
    /// database deleted with the stack. On kubernetes, only <c>try</c> may run Postgres and RabbitMQ in the cluster.
    /// </summary>
    public string Profile { get; set; } = Production;

    /// <summary>An aws environment's region (<c>us-west-2</c>).</summary>
    public string? Region { get; set; }

    /// <summary>The host name the app serves on (<c>orders.example.com</c>). Production only.</summary>
    public string? Domain { get; set; }

    /// <summary>
    /// The Route 53 hosted zone that holds <see cref="Domain"/>. With it the certificate is validated and the name
    /// pointed at the load balancer automatically; without it trax prints the records to add.
    /// </summary>
    public string? HostedZoneId { get; set; }

    /// <summary>An existing VPC to deploy into, in place of the one the stack creates.</summary>
    public DeployNetwork? Network { get; set; }

    /// <summary>
    /// A Secrets Manager secret holding the hosts' whole Postgres connection string, in place of the database the
    /// stack creates.
    /// </summary>
    public string? DatabaseSecretArn { get; set; }

    /// <summary>The sign-in the stack creates: <c>cognito</c>, or none when the hosts trust an existing issuer.</summary>
    public DeployAuth? Auth { get; set; }

    /// <summary>Copies per service in this environment, over the file's <c>count</c>.</summary>
    public SortedDictionary<string, int> Counts { get; set; } = new(StringComparer.Ordinal);

    /// <summary>A value for each name the file lists under <c>settings</c>.</summary>
    public SortedDictionary<string, string> Settings { get; set; } = new(StringComparer.Ordinal);
}

/// <summary>
/// A kubernetes environment's Postgres: the Secret and key holding an existing database's whole connection string, or
/// <c>inCluster</c> for a StatefulSet that the production profile refuses.
/// </summary>
internal sealed class DeployEnvironmentDatabase
{
    public string? Secret { get; set; }

    public string? Key { get; set; }

    public bool? InCluster { get; set; }
}

/// <summary>An existing VPC, with the subnets the load balancer and the services use.</summary>
internal sealed class DeployNetwork
{
    public string VpcId { get; set; } = "";

    /// <summary>The VPC's address range, which the hosts trust forwarded headers from.</summary>
    public string Cidr { get; set; } = "";

    public List<string> PublicSubnets { get; set; } = [];

    public List<string> PrivateSubnets { get; set; } = [];
}

/// <summary>The sign-in a stack creates.</summary>
internal sealed class DeployAuth
{
    public const string Cognito = "cognito";

    public string Kind { get; set; } = Cognito;

    /// <summary>The setting the hosts read the issuer from.</summary>
    public string AuthorityKey { get; set; } = "Auth__Authority";

    /// <summary>The setting the hosts read the audience (the app client's id) from.</summary>
    public string AudienceKey { get; set; } = "Auth__Audience";
}

/// <summary>One deployed project.</summary>
internal sealed class DeployService
{
    /// <summary>The project, relative to <c>trax.deploy.json</c>.</summary>
    public string Project { get; set; } = "";

    /// <summary>How many copies run. A service that runs the <c>api</c> role runs one until hosts hold leases.</summary>
    public int Count { get; set; } = 1;

    /// <summary>
    /// The port on 127.0.0.1 a compose stack publishes this service's HTTP on. Compose publishes one only for a hub
    /// (a service that serves HTTP and runs the api or scheduler role); without this it takes 8080, or the next port
    /// no other service has.
    /// </summary>
    public int? Port { get; set; }

    /// <summary>The memory each copy may use, in MiB. Without it, 1024.</summary>
    public int? Memory { get; set; }
}

/// <summary>The names under <c>ConnectionStrings:</c> a host reads, which the deployment sets.</summary>
internal sealed class DeployConnectionStrings
{
    public string Trax { get; set; } = "Trax";

    /// <summary>
    /// The name a host reads its broker under, with <c>UseRabbitMq(configuration, name: ...)</c>. Compose sets
    /// <c>&lt;name&gt;__Host</c>, <c>__UserName</c> and <c>__Password</c>, and no connection string, so the password is
    /// never part of a URI.
    /// </summary>
    public string RabbitMq { get; set; } = "RabbitMq";
}
