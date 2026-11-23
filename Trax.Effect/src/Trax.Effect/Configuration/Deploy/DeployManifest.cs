using System.Globalization;
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

    /// <summary>
    /// The configuration section a secrets reader marks itself in (<c>Trax:Deploy:SecretSources:&lt;name&gt; =
    /// true</c>), so the export reports it in <see cref="SecretSources"/>.
    /// </summary>
    public const string SecretSourcesSection = "Trax:Deploy:SecretSources";

    private static readonly JsonSerializerOptions Json = new()
    {
        RespectNullableAnnotations = false,
        RespectRequiredConstructorParameters = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
    };

    /// <summary>The format version, <see cref="CurrentVersion"/> when written by this build.</summary>
    public int Version { get; set; } = CurrentVersion;

    /// <summary>
    /// What the host runs, sorted: <c>api</c> (it maps the Trax GraphQL endpoint), <c>job-runner</c> (it maps a runner
    /// endpoint), <c>scheduler</c>, <c>worker</c>.
    /// </summary>
    public IList<string> Roles { get; set; } = [];

    /// <summary>The database the host keeps its runs in, or null when it has no data provider.</summary>
    public DeployDatabase? Database { get; set; }

    /// <summary>The message transports the host connects to, sorted (<c>rabbitmq</c>).</summary>
    public IList<string> Transports { get; set; } = [];

    /// <summary>The paths of the endpoints a deployment routes to or probes.</summary>
    public DeployEndpoints Endpoints { get; set; } = new();

    /// <summary>The authentication schemes the host registers, sorted by name.</summary>
    public IList<DeployAuthenticationScheme> Authentication { get; set; } = [];

    /// <summary>
    /// Where the host reads its secrets from at start besides its environment, sorted (<c>aws-secrets</c>, from
    /// <c>AddTraxAwsSecretsAsync</c>). A deploy target that delivers secrets only that way refuses a host without it.
    /// </summary>
    public IList<string> SecretSources { get; set; } = [];

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
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"The deploy manifest has format version {manifest.Version}, and this build reads up to "
                )
                    + $"{CurrentVersion}. Update the trax tool to the version of the packages the host uses."
            );
        return manifest;
    }
}
