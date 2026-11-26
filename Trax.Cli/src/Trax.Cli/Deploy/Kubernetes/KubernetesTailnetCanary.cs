using k8s.Models;

namespace Trax.Cli.Deploy.Kubernetes;

/// <summary>
/// What a tailnet deploy proves the cluster's network plugin with before anything it protects runs: a listener labelled
/// as the edge, so the edge's NetworkPolicy selects it, its Service, a control listener and Service the policies admit
/// the app's pods to, and a Job from one of the app's own pods that must reach the control and not the canary. The Job
/// succeeds only when the control answers and the canary does not.
/// </summary>
internal sealed record KubernetesTailnetCanary(
    V1Deployment Listener,
    V1Service Service,
    V1Deployment Control,
    V1Service ControlService,
    V1Job Probe
);
