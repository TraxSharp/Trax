namespace Trax.Cli.Deploy.Aws;

internal sealed record AwsSecret(string Name, DateTime? Changed);
