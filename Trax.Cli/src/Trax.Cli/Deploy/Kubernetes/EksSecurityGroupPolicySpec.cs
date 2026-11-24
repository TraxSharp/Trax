using System.Text.Json.Serialization;
using k8s.Models;

namespace Trax.Cli.Deploy.Kubernetes;

/// <summary>Which pods a <see cref="EksSecurityGroupPolicy"/> applies to, and the groups they wear.</summary>
internal sealed class EksSecurityGroupPolicySpec
{
    [JsonPropertyName("podSelector")]
    public V1LabelSelector PodSelector { get; set; } = new();

    [JsonPropertyName("securityGroups")]
    public EksSecurityGroups SecurityGroups { get; set; } = new();
}
