using System.ComponentModel;

namespace Trax.Effect.Configuration.TraxBuilder;

public partial class TraxBuilder
{
    [EditorBrowsable(EditorBrowsableState.Never)]
    internal string? HostEnvironmentOverride { get; set; }

    [EditorBrowsable(EditorBrowsableState.Never)]
    internal string? HostInstanceIdOverride { get; set; }

    [EditorBrowsable(EditorBrowsableState.Never)]
    internal Dictionary<string, string> HostLabels { get; } = new();

    /// <summary>
    /// Overrides the auto-detected host environment type.
    /// By default, Trax probes environment variables to detect Lambda, ECS, Kubernetes,
    /// Azure App Service, or falls back to <c>"server"</c>.
    /// </summary>
    /// <param name="environment">The environment identifier (e.g., <c>"lambda"</c>, <c>"ecs"</c>, <c>"my-custom-env"</c>).</param>
    public TraxBuilder SetHostEnvironment(string environment)
    {
        ArgumentNullException.ThrowIfNull(environment);
        HostEnvironmentOverride = environment;
        return this;
    }

    /// <summary>
    /// Overrides the auto-detected host instance ID.
    /// By default, Trax uses environment-specific identifiers (Lambda log stream, pod name, etc.)
    /// or falls back to <c>{MachineName}-{PID}</c>.
    /// </summary>
    /// <param name="instanceId">The instance identifier.</param>
    public TraxBuilder SetHostInstanceId(string instanceId)
    {
        ArgumentNullException.ThrowIfNull(instanceId);
        HostInstanceIdOverride = instanceId;
        return this;
    }

    /// <summary>
    /// Adds a custom label to the host identity. Labels are persisted as JSONB on every
    /// metadata record and can be used for filtering and debugging in distributed environments.
    /// </summary>
    /// <param name="key">The label key (e.g., <c>"region"</c>, <c>"service"</c>, <c>"team"</c>).</param>
    /// <param name="value">The label value (e.g., <c>"us-east-1"</c>, <c>"content-shield"</c>).</param>
    public TraxBuilder AddHostLabel(string key, string value)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(value);
        HostLabels[key] = value;
        return this;
    }

    /// <summary>
    /// Adds multiple custom labels to the host identity.
    /// </summary>
    /// <param name="labels">Dictionary of label key-value pairs to add.</param>
    public TraxBuilder AddHostLabels(Dictionary<string, string> labels)
    {
        ArgumentNullException.ThrowIfNull(labels);
        foreach (var (key, value) in labels)
            HostLabels[key] = value;
        return this;
    }
}
