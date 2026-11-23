namespace Trax.Cli.Tests.Fakes;

public enum IngestState
{
    Idle,
    Fetching,
    NeedsReview,
    Fetched,
    FetchFailed,
    Cancelled,
    Approved,
}
