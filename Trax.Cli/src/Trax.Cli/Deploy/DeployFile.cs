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

    /// <summary>
    /// The egress image to run beside a service that names <c>egress</c>, by digest
    /// (<c>ghcr.io/traxsharp/trax-egress@sha256:...</c>). Without it, the image this trax was released with.
    /// </summary>
    public string? EgressImage { get; set; }

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
        if (!EnvironmentName.IsMatch(name))
            throw new InvalidOperationException(
                Problem(
                    "its name must be lower-case letters and digits only. The stack is <app>-<environment>, and "
                        + "a dash here would give two apps the same stack."
                )
            );
        if (string.Equals(name, FoundationSuffix, StringComparison.Ordinal))
            throw new InvalidOperationException(
                Problem(
                    $"'{FoundationSuffix}' is taken. Its stack would be <app>-{FoundationSuffix}, which is the name of "
                        + "another app's foundation (app 'orders-prod' would collide with app 'orders' in 'prod')."
                )
            );
        if (env.Profile is not (DeployEnvironment.Production or DeployEnvironment.Try))
            throw new InvalidOperationException(
                Problem($"profile '{env.Profile}' is not production or try.")
            );
        foreach (var (service, entry) in Services)
            if (CheckEgress(service, entry) is { } egress)
                throw new InvalidOperationException(Problem(egress));
        CheckTarget(name, env, Problem);
        CheckEnvironmentLimits(env, Problem);
        foreach (var service in env.Counts.Keys)
            if (!Services.ContainsKey(service))
                throw new InvalidOperationException(
                    Problem($"counts names '{service}', which is not a service.")
                );
        if (!string.Equals(env.Target, DeployEnvironment.Compose, StringComparison.Ordinal))
            CheckSettings(env, Problem);
        return env;
    }

    private void CheckSettings(DeployEnvironment env, Func<string, string> problem)
    {
        var provided = env.Auth is { Kind: DeployAuth.Cognito } auth
            ? new HashSet<string>([auth.AuthorityKey, auth.AudienceKey], StringComparer.Ordinal)
            : [];
        var missing = Settings
            .Where(s => !env.Settings.ContainsKey(s) && !provided.Contains(s))
            .ToList();
        if (missing.Count > 0)
            throw new InvalidOperationException(
                problem(
                    $"settings has no value for {string.Join(", ", missing)}, which the app lists as settings."
                )
            );
        var unknown = env.Settings.Keys.Where(k => !Settings.Contains(k)).ToList();
        if (unknown.Count > 0)
            throw new InvalidOperationException(
                problem(
                    $"settings gives {string.Join(", ", unknown)}, which the app does not list under \"settings\"."
                )
            );
    }

    /// <summary>
    /// The environment's name on AWS, <c>&lt;app&gt;-&lt;environment&gt;</c>: its runtime stack, and the prefix of its
    /// secrets, images, log groups and records.
    /// </summary>
    public string StackName(string environment) => $"{App}-{environment}";

    /// <summary>
    /// The stack holding the environment's network, database, secrets and sign-in, beneath its runtime:
    /// <c>&lt;app&gt;-&lt;environment&gt;-base</c>.
    /// </summary>
    public string FoundationStackName(string environment) =>
        $"{StackName(environment)}-{FoundationSuffix}";

    // An environment may not take this name, or its runtime would be named like another app's foundation.
    private const string FoundationSuffix = "base";

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

        if (Version is not (1 or CurrentVersion))
            throw new InvalidOperationException(
                Problem(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"version {Version} is not one this trax reads (1 or {CurrentVersion}). Update the trax tool."
                    )
                )
            );
        if (Version == 1)
        {
            // Version 1 deployed every environment to AWS, and had no target to say so.
            foreach (var env in Environments.Values)
                env.Target ??= DeployEnvironment.Aws;
            Version = CurrentVersion;
        }
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
        foreach (var setting in Settings.Concat(Secrets))
            if (
                setting.StartsWith("Trax__Deploy__", StringComparison.OrdinalIgnoreCase)
                || setting.StartsWith("Trax:Deploy:", StringComparison.OrdinalIgnoreCase)
            )
                throw new InvalidOperationException(
                    Problem(
                        $"'{setting}' is under Trax:Deploy, which trax deploy sets itself: whether a hub is exposed "
                            + "only on a tailnet, and the secret its proxy proves itself with. An app that set them "
                            + "could switch tailnet sign-in on where nothing protects it."
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

        CheckEgressImage(Problem);

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
            var limit = CheckLimits(
                $"service '{name}'",
                service.Memory,
                service.Pids,
                service.Cpus
            );
            if (limit is not null)
                throw new InvalidOperationException(Problem(limit));
            if (service.Count < 1)
                throw new InvalidOperationException(
                    Problem($"service '{name}' has count {service.Count}; it must be at least 1.")
                );
            if (CheckEgress(name, service) is { } egress)
                throw new InvalidOperationException(Problem(egress));
        }
    }

    /// <summary>Refuses an <c>egressImage</c> not named by digest.</summary>
    private void CheckEgressImage(Func<string, string> problem)
    {
        if (EgressImage is { } image && !DeployEgress.IsDigestReference(image))
            throw new InvalidOperationException(
                problem(
                    $"egressImage '{image}' is not an image named by digest. Give "
                        + $"<registry>/<path>@sha256:<digest> ({DeployEgress.Repository}@sha256:...), so what runs "
                        + "cannot change after the file is written."
                )
            );
    }

    /// <summary>
    /// The first problem with <paramref name="service"/>'s egress, or null: its domains, and its sidecar's name, which
    /// compose takes as <c>&lt;service&gt;-egress</c> and another service must not have.
    /// </summary>
    private string? CheckEgress(string name, DeployService service)
    {
        if (DeployEgress.Check(name, service.Egress) is { } problem)
            return problem;
        if (
            service.Egress is not null
            && Services.ContainsKey($"{name}-{DeployEgress.SidecarContainer}")
        )
            return $"service '{name}' names egress, and its egress sidecar would be named "
                + $"'{name}-{DeployEgress.SidecarContainer}', which is another service. Rename that service.";
        return null;
    }

    /// <summary>
    /// The first limit of <paramref name="who"/> that no runtime would accept, as a sentence, or null when every one
    /// is in range: memory at least <see cref="ServiceHardening.MinimumMemoryMiB"/>, pids at least
    /// <see cref="ServiceHardening.MinimumPids"/>, CPUs from <see cref="ServiceHardening.MinimumCpus"/> to
    /// <see cref="ServiceHardening.MaximumCpus"/>.
    /// </summary>
    internal static string? CheckLimits(string who, int? memory, int? pids, decimal? cpus)
    {
        if (memory < ServiceHardening.MinimumMemoryMiB)
            return string.Create(
                CultureInfo.InvariantCulture,
                $"{who} has memory {memory}; it must be at least {ServiceHardening.MinimumMemoryMiB} (MiB)."
            );
        if (pids < ServiceHardening.MinimumPids)
            return string.Create(
                CultureInfo.InvariantCulture,
                $"{who} has pids {pids}; it must be at least {ServiceHardening.MinimumPids}."
            );
        if (cpus is { } c && (c < ServiceHardening.MinimumCpus || c > ServiceHardening.MaximumCpus))
            return string.Create(
                CultureInfo.InvariantCulture,
                $"{who} has cpus {c}; it must be from {ServiceHardening.MinimumCpus} to {ServiceHardening.MaximumCpus}."
            );
        return null;
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
