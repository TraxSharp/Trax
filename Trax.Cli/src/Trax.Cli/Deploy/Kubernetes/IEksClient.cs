namespace Trax.Cli.Deploy.Kubernetes;

/// <summary>The EKS calls a kubernetes environment on EKS makes; tests swap in a fake.</summary>
internal interface IEksClient
{
    /// <summary>The cluster, or null when there is none of that name in the region.</summary>
    Task<EksCluster?> DescribeClusterAsync(string name, CancellationToken ct);
}
