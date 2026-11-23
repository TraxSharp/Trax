namespace Trax.Samples.Board.Trains.Work;

/// <summary>Why a claim was refused: <c>FORBIDDEN</c>, <c>NOTHING_TO_CLAIM</c> or <c>ALREADY_CLAIMED</c>.</summary>
public sealed record ClaimProblem
{
    public string Code { get; init; } = "";

    public string Message { get; init; } = "";
}
