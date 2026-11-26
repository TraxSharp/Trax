namespace Trax.Cli.Deploy.Aws;

/// <summary>
/// One task to run to completion. <paramref name="Command"/> and <paramref name="Environment"/>, when given, override
/// the container's own, so the migrate task definition also runs the drain and the resume; <paramref name="Label"/>
/// names the task in the progress lines.
/// </summary>
internal sealed record AwsTaskRequest(
    string Cluster,
    string TaskDefinition,
    string Container,
    IReadOnlyList<string> Subnets,
    IReadOnlyList<string> SecurityGroups,
    bool AssignPublicIp,
    IReadOnlyList<string>? Command = null,
    IReadOnlyDictionary<string, string>? Environment = null,
    string Label = "migrate"
);
