using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Trax.Cli.Deploy;

/// <summary>The names under <c>ConnectionStrings:</c> a host reads, which the deployment sets.</summary>
internal sealed class DeployConnectionStrings
{
    public string Trax { get; set; } = "Trax";

    /// <summary>
    /// The name a host reads its broker under, with <c>UseRabbitMq(configuration, name: ...)</c>. Compose sets
    /// <c>&lt;name&gt;__Host</c>, <c>__UserName</c> and <c>__Password</c>, and no connection string, so the password is
    /// never part of a URI.
    /// </summary>
    public string RabbitMq { get; set; } = "RabbitMq";
}
