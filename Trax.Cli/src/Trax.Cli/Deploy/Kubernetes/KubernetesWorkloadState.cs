namespace Trax.Cli.Deploy.Kubernetes;

/// <summary>A Deployment or StatefulSet of the app, with how many copies are ready and the image it runs.</summary>
internal sealed record KubernetesWorkloadState(
    string Kind,
    string Name,
    int Ready,
    int Desired,
    string? Image
);
