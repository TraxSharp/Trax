namespace Trax.Cli.Deploy.Aws;

internal sealed record AwsServiceState(
    string Name,
    int Desired,
    int Running,
    int Pending,
    string Rollout,
    string TaskDefinition
);
