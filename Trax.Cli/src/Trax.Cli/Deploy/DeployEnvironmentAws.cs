namespace Trax.Cli.Deploy;

/// <summary>
/// A kubernetes environment's AWS side: the EKS cluster it applies to, and the AWS foundation (network, database,
/// secrets) trax deploys for it. trax never creates the cluster.
/// </summary>
internal sealed class DeployEnvironmentAws
{
    /// <summary>Through Security Groups for Pods: only the app's pods wear the group the database admits.</summary>
    public const string Pods = "pods";

    /// <summary>The cluster's node group is admitted, so every pod on those nodes reaches the database.</summary>
    public const string Nodes = "nodes";

    /// <summary>The region the cluster and the foundation are in (<c>us-east-1</c>).</summary>
    public string? Region { get; set; }

    /// <summary>The EKS cluster's name. The current kubeconfig context must be this cluster.</summary>
    public string? Cluster { get; set; }

    /// <summary>
    /// The cluster's VPC and private subnets, which the foundation puts the database in. Without it the foundation
    /// creates the VPC, and the cluster is created in it after the first deploy.
    /// </summary>
    public DeployNetwork? Network { get; set; }

    /// <summary>How pods reach the database: <see cref="Pods"/> (the default) or <see cref="Nodes"/>.</summary>
    public string? DatabaseAccess { get; set; }
}
