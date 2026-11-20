namespace Trax.Cli.Deploy.Aws;

internal sealed record AwsTaskRequest(
    string Cluster,
    string TaskDefinition,
    string Container,
    IReadOnlyList<string> Subnets,
    string SecurityGroup,
    bool AssignPublicIp
);
