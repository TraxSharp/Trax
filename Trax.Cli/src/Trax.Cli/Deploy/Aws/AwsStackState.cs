namespace Trax.Cli.Deploy.Aws;

internal sealed record AwsStackState(
    string Status,
    IReadOnlyDictionary<string, string> Outputs,
    IReadOnlyDictionary<string, string> Parameters
)
{
    /// <summary>Whether the stack is settled and can be changed.</summary>
    public bool Settled => !Status.EndsWith("_IN_PROGRESS", StringComparison.Ordinal);

    /// <summary>A stack whose creation failed and rolled back: it can only be deleted.</summary>
    public bool Dead => Status is "ROLLBACK_COMPLETE" or "ROLLBACK_FAILED" or "DELETE_FAILED";
}
