namespace Trax.Cli.Deploy.Aws;

/// <summary>
/// A network interface in a VPC: <paramref name="Type"/> is EC2's interface type (<c>interface</c>,
/// <c>nat_gateway</c>, ...), and <paramref name="SecurityGroups"/> the ids of the groups it wears.
/// </summary>
internal sealed record AwsNetworkInterface(
    string Id,
    string Type,
    string Description,
    IReadOnlyList<string> SecurityGroups
);
