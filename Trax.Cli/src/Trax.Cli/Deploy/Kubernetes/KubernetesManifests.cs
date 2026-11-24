using k8s;
using k8s.Models;

namespace Trax.Cli.Deploy.Kubernetes;

/// <summary>
/// What a kubernetes environment is made of, in the order the deployer applies it: the namespace, the admission policy
/// and binding that hold it when a service names egress (cluster-wide objects, scoped to the namespace), its network
/// policies, the in-cluster Postgres and RabbitMQ when the environment runs them, the migrate Job, then each service's
/// Service and Deployment, and a tailnet exposure's proxy. <see cref="ProxySecretName"/> names the Secret the deployer
/// writes a freshly minted proxy secret to before the services, when the environment is exposed on a tailnet.
/// </summary>
internal sealed record KubernetesManifests(
    V1Namespace Namespace,
    IReadOnlyList<IKubernetesObject<V1ObjectMeta>> Admission,
    IReadOnlyList<V1NetworkPolicy> Policies,
    string SecretName,
    IReadOnlyList<string> SecretKeys,
    V1SecretKeySelector? DatabaseSecret,
    IReadOnlyList<IKubernetesObject<V1ObjectMeta>> Backing,
    V1Job Migrate,
    IReadOnlyList<IKubernetesObject<V1ObjectMeta>> Services,
    string? ProxySecretName
)
{
    public string NamespaceName => Namespace.Metadata.Name;

    /// <summary>Every object, in the order it is applied.</summary>
    public IEnumerable<IKubernetesObject<V1ObjectMeta>> All =>
        [Namespace, .. Admission, .. Policies, .. Backing, Migrate, .. Services];
}
