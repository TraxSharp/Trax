using System.Text.Json.Nodes;
using Trax.Samples.Board.Tickets;

namespace Trax.Samples.Board.Trains.Checks;

/// <summary>
/// What a check found: whether the server accepts the agent's work, and why. An assignment's checking state routes
/// on <see cref="Accepted"/> and copies the rest into its context.
/// </summary>
public sealed record CheckResult
{
    public bool Accepted { get; init; }

    public string Message { get; init; } = "";

    /// <summary>For a repro check: how many cases on the ticket have not shown the bug.</summary>
    public int? FailedCases { get; init; }

    public static CheckResult Accept(string message, int? failedCases = null) =>
        new()
        {
            Accepted = true,
            Message = message,
            FailedCases = failedCases,
        };

    public static CheckResult Refuse(string message, int? failedCases = null) =>
        new()
        {
            Accepted = false,
            Message = message,
            FailedCases = failedCases,
        };
}
