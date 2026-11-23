using System.Text.Json.Serialization;

namespace Trax.Cli.Deploy.Kubernetes;

/// <summary>The security group ids a <see cref="EksSecurityGroupPolicy"/> gives its pods.</summary>
internal sealed class EksSecurityGroups
{
    [JsonPropertyName("groupIds")]
    public IList<string> GroupIds { get; set; } = [];
}
