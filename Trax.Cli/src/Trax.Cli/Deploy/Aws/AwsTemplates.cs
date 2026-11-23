using System.Text.Json.Nodes;

namespace Trax.Cli.Deploy.Aws;

/// <summary>The two CloudFormation templates of one environment, in the order they are created.</summary>
internal sealed record AwsTemplates(JsonObject Foundation, JsonObject Runtime);
