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
    public const int CurrentVersion = 1;

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

        if (Version != CurrentVersion)
            throw new InvalidOperationException(
                Problem(
                    $"version {Version} is not one this trax reads ({CurrentVersion}). Update the trax tool."
                )
            );
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

    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]*$")]
    private static partial Regex Variable();
}

/// <summary>One deployed project.</summary>
internal sealed class DeployService
{
    /// <summary>The project, relative to <c>trax.deploy.json</c>.</summary>
    public string Project { get; set; } = "";

    /// <summary>How many copies run. A service that runs the <c>api</c> role runs one until hosts hold leases.</summary>
    public int Count { get; set; } = 1;
}

/// <summary>The names under <c>ConnectionStrings:</c> a host reads, which the deployment sets.</summary>
internal sealed class DeployConnectionStrings
{
    public string Trax { get; set; } = "Trax";

    public string RabbitMq { get; set; } = "RabbitMq";
}
