using k8s.Models;

namespace Trax.Cli.Deploy.Kubernetes;

/// <summary>
/// What a tailnet deploy proves the cluster's network plugin with before anything it protects runs: a listener labelled
/// as the edge, so the edge's NetworkPolicy selects it, its Service, and a Job from one of the app's own pods that
/// must not reach it. The Job succeeds only when the connection is refused or times out.
/// </summary>
internal sealed record KubernetesTailnetCanary(
    V1Deployment Listener,
    V1Service Service,
    V1Job Probe
);
