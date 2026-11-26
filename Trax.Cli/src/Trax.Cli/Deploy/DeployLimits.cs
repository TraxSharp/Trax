namespace Trax.Cli.Deploy;

/// <summary>
/// The limits an environment sets on a service trax runs beside the app (<c>database</c>, <c>rabbitmq</c>), under
/// <c>limits</c>. Each value left out keeps that service's default.
/// </summary>
internal sealed class DeployLimits
{
    /// <summary>The memory, in MiB.</summary>
    public int? Memory { get; set; }

    /// <summary>The processes and threads it may run. Compose only: Kubernetes has no per-container limit.</summary>
    public int? Pids { get; set; }

    /// <summary>The CPUs it may use.</summary>
    public decimal? Cpus { get; set; }
}
