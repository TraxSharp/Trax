using System.Text.Json.Nodes;

namespace Trax.Cli.Deploy.Aws;

/// <summary>Where a runtime's tasks run: their subnets, the security groups they wear, and whether they get a public address.</summary>
internal sealed record ServiceNetwork(
    JsonNode Subnets,
    JsonArray SecurityGroups,
    string AssignPublicIp
);
