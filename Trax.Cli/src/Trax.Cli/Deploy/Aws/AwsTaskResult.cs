namespace Trax.Cli.Deploy.Aws;

internal sealed record AwsTaskResult(int? ExitCode, string? StoppedReason, string LogStream);
