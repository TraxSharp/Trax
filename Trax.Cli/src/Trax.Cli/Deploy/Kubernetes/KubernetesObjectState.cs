namespace Trax.Cli.Deploy.Kubernetes;

/// <summary>An object in the namespace, and the app its <c>trax/app</c> label names, if any.</summary>
internal sealed record KubernetesObjectState(string Kind, string Name, string? App);
