using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Trax.Cli.Deploy;

/// <summary>The names under <c>ConnectionStrings:</c> a host reads, which the deployment sets.</summary>
internal sealed class DeployConnectionStrings
{
    public string Trax { get; set; } = "Trax";

    public string RabbitMq { get; set; } = "RabbitMq";
}
