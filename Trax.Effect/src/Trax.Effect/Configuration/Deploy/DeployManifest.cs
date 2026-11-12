using System.Text.Json;
using System.Text.Json.Serialization;

namespace Trax.Effect.Configuration.Deploy;

/// <summary>
/// What a host needs from where it is deployed, as it reports it when started with <c>--trax-export-deploy</c>: its
/// roles, its database, the transports it talks over, the endpoints a load balancer and its operators reach, and the
/// authentication schemes it accepts. <c>trax deploy</c> reads it, so the infrastructure follows the code.
/// </summary>
/// <seealso href="https://traxsharp.net/docs/sdk-reference/configuration/deploy-manifest">SDK reference</seealso>
public sealed class DeployManifest
{
    /// <summary>The format version this build writes.</summary>
    public const int CurrentVersion = 1;

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
    };

    /// <summary>The format version, <see cref="CurrentVersion"/> when written by this build.</summary>
    public int Version { get; set; } = CurrentVersion;

    /// <summary>
    /// What the host runs, sorted: <c>api</c> (it maps the Trax GraphQL endpoint), <c>scheduler</c>, <c>worker</c>,
    /// <c>job-runner</c>.
    /// </summary>
    public List<string> Roles { get; set; } = [];

    /// <summary>The database the host keeps its runs in, or null when it has no data provider.</summary>
    public DeployDatabase? Database { get; set; }

    /// <summary>The message transports the host connects to, sorted (<c>rabbitmq</c>).</summary>
    public List<string> Transports { get; set; } = [];

    /// <summary>The paths of the endpoints a deployment routes to or probes.</summary>
    public DeployEndpoints Endpoints { get; set; } = new();

    /// <summary>The authentication schemes the host registers, sorted by name.</summary>
    public List<DeployAuthenticationScheme> Authentication { get; set; } = [];

    /// <summary>Writes the manifest as indented JSON.</summary>
    public string Write() => JsonSerializer.Serialize(this, Json) + "\n";

    /// <summary>
    /// Reads a manifest. Throws <see cref="InvalidOperationException"/> for one written by a newer format version,
    /// which this build cannot be sure it understands.
    /// </summary>
    public static DeployManifest Read(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        var manifest =
            JsonSerializer.Deserialize<DeployManifest>(json, Json)
            ?? throw new InvalidOperationException("The deploy manifest is empty.");
        if (manifest.Version > CurrentVersion)
            throw new InvalidOperationException(
                $"The deploy manifest has format version {manifest.Version}, and this build reads up to "
                    + $"{CurrentVersion}. Update the trax tool to the version of the packages the host uses."
            );
        return manifest;
    }
}

/// <summary>The database a deployed host needs.</summary>
public sealed class DeployDatabase
{
    /// <summary><c>postgres</c>, <c>sqlite</c> or <c>in-memory</c>.</summary>
    public string Provider { get; set; } = "";

    /// <summary>
    /// Whether the host migrates the schema when it starts. False when its code calls <c>SkipMigrations()</c>; a
    /// deployment that runs <c>--trax-migrate</c> sets <c>TRAX_MIGRATIONS=skip</c> on the rest regardless.
    /// </summary>
    public bool MigratesOnStart { get; set; }
}

/// <summary>The paths a deployment routes to or probes. A path the host does not map is null.</summary>
public sealed class DeployEndpoints
{
    /// <summary>The Trax GraphQL endpoint, from <c>UseTraxGraphQL</c>.</summary>
    public string? GraphQL { get; set; }

    /// <summary>The health check, from <c>MapHealthChecks</c>.</summary>
    public string? Health { get; set; }

    /// <summary>The operations console, from <c>UseTraxApiDashboard</c>.</summary>
    public string? Console { get; set; }
}

/// <summary>An authentication scheme the host registers.</summary>
public sealed class DeployAuthenticationScheme
{
    /// <summary>The scheme's name, as <c>AddAuthentication</c> registered it.</summary>
    public string Name { get; set; } = "";

    /// <summary>The handler's type name, such as <c>JwtBearerHandler</c> or <c>OpenIdConnectHandler</c>.</summary>
    public string Handler { get; set; } = "";
}
