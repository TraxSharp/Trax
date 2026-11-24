using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Trax.Cli.Deploy;

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

    /// <summary>
    /// The memory each copy may use, in MiB. Without it the copy has no limit (on AWS, a Fargate task's 1024): set it
    /// in production, where one leaking copy could otherwise take the memory its neighbours need.
    /// </summary>
    public int? Memory { get; set; }

    /// <summary>
    /// The processes and threads one copy may run. Without it, 512. Compose only: Kubernetes and Fargate have no
    /// per-container limit, and an environment on either refuses it.
    /// </summary>
    public int? Pids { get; set; }

    /// <summary>The CPUs each copy may use (<c>1.5</c>). Without it the copy has no limit (on AWS, a Fargate task's 0.5).</summary>
    public decimal? Cpus { get; set; }
}
