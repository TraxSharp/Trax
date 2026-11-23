namespace Trax.Cli.Deploy.Kubernetes;

/// <summary>An EKS cluster as <c>DescribeCluster</c> reports it: its API server, its VPC and its own security group.</summary>
internal sealed record EksCluster(
    string Name,
    string Status,
    string Endpoint,
    string VpcId,
    string ClusterSecurityGroup
);
