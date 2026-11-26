namespace Trax.Scheduler.Sandbox.Launcher.Configuration;

/// <summary>
/// What the launcher can tell about a StorageClass on its own. A host-path provisioner makes a
/// directory on the node and enforces no size, so a run's volume there is no ceiling at all; that
/// much is known by name. Whether any other class enforces capacity the launcher cannot see, so it
/// takes the deploy's word for it.
/// </summary>
internal static class KubernetesStorage
{
    /// <summary>Provisioners that make a directory on the node and enforce no size.</summary>
    internal static readonly IReadOnlySet<string> HostPathProvisioners = new HashSet<string>(
        StringComparer.Ordinal
    )
    {
        "rancher.io/local-path",
        "kubernetes.io/no-provisioner",
        "kubernetes.io/host-path",
        "k8s.io/minikube-hostpath",
        "docker.io/hostpath",
        "microk8s.io/hostpath",
        "openebs.io/local",
        "hostpath.csi.k8s.io",
    };

    /// <summary>Whether <paramref name="className"/> is <c>local-path</c>, the host-path class k3s ships.</summary>
    internal static bool NamesAHostPathClass(string className) =>
        string.Equals(className, "local-path", StringComparison.Ordinal);
}
