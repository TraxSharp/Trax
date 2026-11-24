using k8s;
using k8s.Models;

namespace Trax.Cli.Deploy.Kubernetes;

/// <summary>
/// The cluster calls <c>trax deploy</c> makes for a kubernetes environment, so the deploy can be tested without a
/// cluster. <see cref="KubernetesDeployClient"/> makes them with the official client, against the current kubeconfig
/// context.
/// </summary>
internal interface IKubernetesDeployClient
{
    /// <summary>The kubeconfig context the calls go to, for what trax prints.</summary>
    string Context { get; }

    /// <summary>The API server the context points at (<c>https://ABC.gr7.us-east-1.eks.amazonaws.com</c>).</summary>
    string Server { get; }

    /// <summary>The namespace's labels, or null when it does not exist.</summary>
    Task<IReadOnlyDictionary<string, string>?> NamespaceLabelsAsync(
        string name,
        CancellationToken ct
    );

    Task CreateNamespaceAsync(V1Namespace body, CancellationToken ct);

    /// <summary>The keys a Secret holds (never their values), or null when it does not exist.</summary>
    Task<IReadOnlyCollection<string>?> SecretKeysAsync(
        string ns,
        string name,
        CancellationToken ct
    );

    /// <summary>The Deployments, StatefulSets, Services, Jobs and PersistentVolumeClaims in the namespace.</summary>
    Task<IReadOnlyList<KubernetesObjectState>> ListAsync(string ns, CancellationToken ct);

    /// <summary>Creates or updates a Service, Deployment or StatefulSet with server-side apply.</summary>
    Task ApplyAsync(IKubernetesObject<V1ObjectMeta> body, CancellationToken ct);

    Task CreateJobAsync(V1Job body, CancellationToken ct);

    /// <summary>Waits for the Job to succeed or fail, and on failure returns the end of its pod's log.</summary>
    Task<KubernetesJobResult> WaitForJobAsync(
        string ns,
        string name,
        TimeSpan timeout,
        CancellationToken ct
    );

    /// <summary>
    /// Waits until the Deployment or StatefulSet has rolled out: its controller has seen the latest change, and every
    /// copy is updated and ready. Throws when it has not within <paramref name="timeout"/>.
    /// </summary>
    Task WaitForRolloutAsync(
        string ns,
        string kind,
        string name,
        TimeSpan timeout,
        CancellationToken ct
    );

    /// <summary>The app's Deployments and StatefulSets, with how many copies are ready.</summary>
    Task<IReadOnlyList<KubernetesWorkloadState>> WorkloadsAsync(
        string ns,
        string app,
        CancellationToken ct
    );

    /// <summary>The log of every pod of one of the app's services, each line prefixed with its pod.</summary>
    IAsyncEnumerable<string> LogsAsync(
        string ns,
        string app,
        string service,
        bool follow,
        int sinceMinutes,
        CancellationToken ct
    );

    Task DeleteAsync(string ns, KubernetesObjectState target, CancellationToken ct);
}
