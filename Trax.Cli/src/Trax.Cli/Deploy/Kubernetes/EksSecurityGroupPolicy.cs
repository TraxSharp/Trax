using System.Text.Json.Serialization;
using k8s;
using k8s.Models;

namespace Trax.Cli.Deploy.Kubernetes;

/// <summary>
/// The VPC resource controller's <c>SecurityGroupPolicy</c> (<c>vpcresources.k8s.aws/v1beta1</c>): the pods its
/// selector matches get their own network interface wearing these security groups in place of their node's.
/// </summary>
internal sealed class EksSecurityGroupPolicy : IKubernetesObject<V1ObjectMeta>
{
    public const string Group = "vpcresources.k8s.aws";
    public const string Version = "v1beta1";
    public const string Plural = "securitygrouppolicies";

    [JsonPropertyName("apiVersion")]
    public string ApiVersion { get; set; } = $"{Group}/{Version}";

    [JsonPropertyName("kind")]
    public string Kind { get; set; } = "SecurityGroupPolicy";

    [JsonPropertyName("metadata")]
    public V1ObjectMeta Metadata { get; set; } = new();

    [JsonPropertyName("spec")]
    public EksSecurityGroupPolicySpec Spec { get; set; } = new();
}
