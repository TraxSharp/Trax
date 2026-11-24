using Amazon;
using Amazon.EKS;
using Amazon.EKS.Model;

namespace Trax.Cli.Deploy.Kubernetes;

/// <summary><see cref="IEksClient"/> over the AWS SDK, with the credentials the AWS CLI's chain finds.</summary>
internal sealed class EksClient(string region) : IEksClient
{
    public async Task<EksCluster?> DescribeClusterAsync(string name, CancellationToken ct)
    {
        using var eks = new AmazonEKSClient(RegionEndpoint.GetBySystemName(region));
        try
        {
            var cluster = (
                await eks.DescribeClusterAsync(new DescribeClusterRequest { Name = name }, ct)
                    .ConfigureAwait(false)
            ).Cluster;
            return new EksCluster(
                cluster.Name,
                cluster.Status?.Value ?? "",
                cluster.Endpoint ?? "",
                cluster.ResourcesVpcConfig?.VpcId ?? "",
                cluster.ResourcesVpcConfig?.ClusterSecurityGroupId ?? ""
            );
        }
        catch (ResourceNotFoundException)
        {
            return null;
        }
    }
}
