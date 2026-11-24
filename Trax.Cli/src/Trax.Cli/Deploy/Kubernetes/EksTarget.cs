namespace Trax.Cli.Deploy.Kubernetes;

/// <summary>
/// What a kubernetes environment on EKS renders from besides the file: where each service's image was pushed, the
/// foundation's database and secret, and the security groups the app's pods wear. <see cref="ClusterSecurityGroup"/>
/// is the cluster's own group, which the pods keep for DNS and each other when Security Groups for Pods replaces their
/// node's groups.
/// </summary>
internal sealed record EksTarget(
    IReadOnlyDictionary<string, string> Images,
    string Region,
    string DatabaseAddress,
    string DatabasePort,
    string DatabaseSecretArn,
    string DatabaseClientSecurityGroup,
    string ClusterSecurityGroup,
    bool PodSecurityGroups
);
