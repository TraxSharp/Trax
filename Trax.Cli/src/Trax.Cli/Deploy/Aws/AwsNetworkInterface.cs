namespace Trax.Cli.Deploy.Aws;

/// <summary>
/// A network interface in a VPC: <paramref name="Type"/> is EC2's interface type (<c>interface</c>,
/// <c>nat_gateway</c>, ...), <paramref name="SecurityGroups"/> the ids of the groups it wears, and
/// <paramref name="RequesterId"/> who created it for a managed service (<c>amazon-rds</c>, <c>amazon-elb</c>), or
/// empty.
/// </summary>
internal sealed record AwsNetworkInterface(
    string Id,
    string Type,
    string Description,
    IReadOnlyList<string> SecurityGroups,
    string RequesterId = ""
);
