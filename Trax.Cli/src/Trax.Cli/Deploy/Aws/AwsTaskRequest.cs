namespace Trax.Cli.Deploy.Aws;

internal sealed record AwsTaskRequest(
    string Cluster,
    string TaskDefinition,
    string Container,
    IReadOnlyList<string> Subnets,
    IReadOnlyList<string> SecurityGroups,
    bool AssignPublicIp
);
