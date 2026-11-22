using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Trax.Core.Extensions;

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
    public const int CurrentVersion = 1;

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        RespectNullableAnnotations = false,
        RespectRequiredConstructorParameters = false,
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

    /// <summary>The cloud environments this app deploys to, by name (<c>production</c>, <c>staging</c>): one stack each.</summary>
    public SortedDictionary<string, DeployEnvironment> Environments { get; set; } =
        new(StringComparer.Ordinal);

    /// <summary>
    /// The environment <paramref name="name"/>, checked for a cloud deploy. Throws naming the first problem.
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
        if (!EnvironmentName.IsMatch(name))
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
        if (string.IsNullOrWhiteSpace(env.Region))
            throw new InvalidOperationException(Problem("it names no region (us-west-2)."));
        if (
            string.Equals(env.Profile, DeployEnvironment.Production, StringComparison.Ordinal)
            && string.IsNullOrWhiteSpace(env.Domain)
        )
            throw new InvalidOperationException(
                Problem(
                    "the production profile needs a domain: HTTPS needs a certificate, and a certificate needs a "
                        + "domain. Name one, or use the try profile, which serves on a CloudFront address."
                )
            );
        if (
            string.Equals(env.Profile, DeployEnvironment.Try, StringComparison.Ordinal)
            && env.Domain is not null
        )
            throw new InvalidOperationException(
                Problem("the try profile serves on a CloudFront address and takes no domain.")
            );
        CheckNetworkAndCounts(name, env);
        CheckSettingsAndAuth(name, env);
        return env;
    }

    private void CheckNetworkAndCounts(string name, DeployEnvironment env)
    {
        string Problem(string what) => $"Environment '{name}': {what}";
        if (env.Network is { } network)
        {
            if (string.IsNullOrWhiteSpace(network.VpcId) || string.IsNullOrWhiteSpace(network.Cidr))
                throw new InvalidOperationException(
                    Problem("network needs vpcId and cidr (the VPC's address range).")
                );
            if (network.PublicSubnets.Count < 2 || network.PrivateSubnets.Count < 2)
                throw new InvalidOperationException(
                    Problem(
                        "network needs at least two public and two private subnets, in different availability "
                            + "zones: a load balancer and a database each span two."
                    )
                );
        }
        foreach (var service in env.Counts.Keys)
            if (!Services.ContainsKey(service))
                throw new InvalidOperationException(
                    Problem($"counts names '{service}', which is not a service.")
                );
    }

    private void CheckSettingsAndAuth(string name, DeployEnvironment env)
    {
        string Problem(string what) => $"Environment '{name}': {what}";
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
        if (
            env.Auth is { } a
            && !string.Equals(a.Kind, DeployAuth.Cognito, StringComparison.Ordinal)
        )
            throw new InvalidOperationException(
                Problem(
                    $"auth kind '{a.Kind}' is not one trax creates. Use cognito, or leave auth out and give an "
                        + "existing issuer's address in settings."
                )
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
    public static async Task<DeployFile> ReadAsync(string path)
    {
        if (!File.Exists(path))
            throw new InvalidOperationException(
                $"No {FileName} at {path}. Run 'trax deploy init' in the directory that holds the solution."
            );
        DeployFile file;
        try
        {
            file =
                JsonSerializer.Deserialize<DeployFile>(
                    await File.ReadAllTextAsync(path).ConfigureAwait(false),
                    Json
                ) ?? throw new InvalidOperationException($"{path} is empty.");
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

        if (Version != CurrentVersion)
            throw new InvalidOperationException(
                Problem(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"version {Version} is not one this trax reads ({CurrentVersion}). Update the trax tool."
                    )
                )
            );
        if (!Name.IsMatch(App))
            throw new InvalidOperationException(
                Problem(
                    $"app '{App}' must be lower case, start with a letter and hold only letters, digits and dashes."
                )
            );
        if (Services.Count == 0)
            throw new InvalidOperationException(
                Problem("services is empty. Name at least one project to deploy.")
            );
        CheckServices(path);
        foreach (var setting in Settings.Concat(Secrets))
            if (!Variable.IsMatch(setting))
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
            if (!Variable.IsMatch(connection))
                throw new InvalidOperationException(
                    Problem($"connection string name '{connection}' is not a configuration key.")
                );
    }

    private void CheckServices(string path)
    {
        string Problem(string what) => $"{path}: {what}";

        var dir = Path.GetDirectoryName(Path.GetFullPath(path)).NotNull();
        var ports = new Dictionary<int, string>();
        foreach (var (name, service) in Services)
        {
            if (!Name.IsMatch(name) || name is "database" or "rabbitmq" or "migrate")
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
                    Problem(
                        string.Create(
                            CultureInfo.InvariantCulture,
                            $"service '{name}' has port {service.Port}, which is not a TCP port."
                        )
                    )
                );
            if (service.Port is { } port && !ports.TryAdd(port, name))
                throw new InvalidOperationException(
                    Problem(
                        $"service '{name}' has port {port.ToString(CultureInfo.InvariantCulture)}, which service '{ports[port]}' has too."
                    )
                );
            if (service.Count < 1)
                throw new InvalidOperationException(
                    Problem($"service '{name}' has count {service.Count}; it must be at least 1.")
                );
        }
    }

    /// <summary>The full path of <paramref name="service"/>'s project.</summary>
    public static string ProjectPath(string deployFilePath, DeployService service) =>
        Path.GetFullPath(
            Path.Combine(
                Path.GetDirectoryName(Path.GetFullPath(deployFilePath)).NotNull(),
                service.Project
            )
        );

    [GeneratedRegex("^[a-z][a-z0-9-]*$", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    internal static partial Regex Name { get; }

    [GeneratedRegex("^[a-z0-9]+$", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex EnvironmentName { get; }

    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex Variable { get; }
}
