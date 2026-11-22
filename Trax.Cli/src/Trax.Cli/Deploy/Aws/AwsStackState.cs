namespace Trax.Cli.Deploy.Aws;

internal sealed record AwsStackState(
    string Status,
    IReadOnlyDictionary<string, string> Outputs,
    IReadOnlyDictionary<string, string> Parameters
)
{
    /// <summary>The stack's tags: <c>trax:app</c> names the app that deployed it.</summary>
    public IReadOnlyDictionary<string, string> Tags { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>Whether the stack is settled and can be changed.</summary>
    public bool Settled => !Status.EndsWith("_IN_PROGRESS", StringComparison.Ordinal);

    /// <summary>
    /// Whether the stack holds anything. A first deploy's change set makes the stack exist in REVIEW_IN_PROGRESS
    /// before it is executed; a stack left there by a change set that failed or was abandoned is created again.
    /// </summary>
    public bool Created => Status != "REVIEW_IN_PROGRESS";

    /// <summary>A stack whose creation failed and rolled back: it can only be deleted.</summary>
    public bool Dead => Status is "ROLLBACK_COMPLETE" or "ROLLBACK_FAILED" or "DELETE_FAILED";
}
