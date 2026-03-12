using System.Diagnostics;

namespace Trax.Effect.Models.Host;

/// <summary>
/// Captures the identity of the host environment where a train executes.
/// Auto-detected at startup and stamped onto every <see cref="Metadata.Metadata"/> record
/// so the shared database records WHERE each train ran.
/// </summary>
public record TraxHostInfo
{
    /// <summary>
    /// Machine hostname (e.g., <c>ip-10-0-1-42</c>, <c>devbox</c>).
    /// </summary>
    public string HostName { get; init; } = null!;

    /// <summary>
    /// Environment type: <c>"lambda"</c>, <c>"ecs"</c>, <c>"kubernetes"</c>,
    /// <c>"azure-app-service"</c>, or <c>"server"</c> (default).
    /// </summary>
    public string HostEnvironment { get; init; } = null!;

    /// <summary>
    /// Instance-level identifier (Lambda log stream, ECS task/container ID,
    /// Kubernetes pod name, or <c>{MachineName}-{PID}</c>).
    /// </summary>
    public string HostInstanceId { get; init; } = null!;

    /// <summary>
    /// User-provided key-value labels (e.g., region, service, team).
    /// Serialized as JSONB in the database.
    /// </summary>
    public Dictionary<string, string> Labels { get; init; } = new();

    /// <summary>
    /// Process-wide singleton set once at startup by the builder.
    /// Read by <see cref="Metadata.Metadata.Create"/> and
    /// <see cref="Extensions.ServiceTrainExtensions"/> to stamp host identity.
    /// </summary>
    public static TraxHostInfo? Current { get; set; }

    /// <summary>
    /// Auto-detects the host environment by probing well-known environment variables.
    /// </summary>
    public static TraxHostInfo AutoDetect()
    {
        var (environment, instanceId) = DetectEnvironment();

        return new TraxHostInfo
        {
            HostName = GetHostName(),
            HostEnvironment = environment,
            HostInstanceId = instanceId,
            Labels = new Dictionary<string, string>(),
        };
    }

    private static string GetHostName()
    {
        try
        {
            return Environment.MachineName;
        }
        catch
        {
            return "unknown";
        }
    }

    private static (string Environment, string InstanceId) DetectEnvironment()
    {
        // AWS Lambda
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("AWS_LAMBDA_FUNCTION_NAME")))
        {
            var instanceId =
                Environment.GetEnvironmentVariable("AWS_LAMBDA_LOG_STREAM_NAME")
                ?? FallbackInstanceId();

            return ("lambda", instanceId);
        }

        // AWS ECS
        if (
            !string.IsNullOrEmpty(
                Environment.GetEnvironmentVariable("ECS_CONTAINER_METADATA_URI_V4")
            )
            || !string.IsNullOrEmpty(
                Environment.GetEnvironmentVariable("ECS_CONTAINER_METADATA_URI")
            )
        )
        {
            var instanceId = Environment.GetEnvironmentVariable("HOSTNAME") ?? FallbackInstanceId();
            return ("ecs", instanceId);
        }

        // Kubernetes
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("KUBERNETES_SERVICE_HOST")))
        {
            var instanceId = Environment.GetEnvironmentVariable("HOSTNAME") ?? FallbackInstanceId();
            return ("kubernetes", instanceId);
        }

        // Azure App Service
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WEBSITE_SITE_NAME")))
        {
            var instanceId =
                Environment.GetEnvironmentVariable("WEBSITE_INSTANCE_ID") ?? FallbackInstanceId();
            return ("azure-app-service", instanceId);
        }

        // Default: bare-metal / VM / local dev
        return ("server", FallbackInstanceId());
    }

    private static string FallbackInstanceId()
    {
        try
        {
            return $"{Environment.MachineName}-{Environment.ProcessId}";
        }
        catch
        {
            return $"unknown-{Environment.ProcessId}";
        }
    }
}
