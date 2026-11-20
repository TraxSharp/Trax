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
    /// The port on 127.0.0.1 a compose stack publishes this service's HTTP on. Default: 8080, then 8081 and on for
    /// each further service that serves HTTP.
    /// </summary>
    public int? Port { get; set; }
}
