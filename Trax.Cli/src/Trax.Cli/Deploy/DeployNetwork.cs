using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Trax.Cli.Deploy;

/// <summary>An existing VPC, with the subnets the load balancer and the services use.</summary>
internal sealed class DeployNetwork
{
    public string VpcId { get; set; } = "";

    /// <summary>The VPC's address range, which the hosts trust forwarded headers from.</summary>
    public string Cidr { get; set; } = "";

    public List<string> PublicSubnets { get; set; } = [];

    public List<string> PrivateSubnets { get; set; } = [];
}
