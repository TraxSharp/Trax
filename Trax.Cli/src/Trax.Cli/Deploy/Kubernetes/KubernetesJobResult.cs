namespace Trax.Cli.Deploy.Kubernetes;

/// <summary>How a Job ended: whether it succeeded, why not, and the end of its pod's log.</summary>
internal sealed record KubernetesJobResult(bool Succeeded, string? Reason, string LogTail);
