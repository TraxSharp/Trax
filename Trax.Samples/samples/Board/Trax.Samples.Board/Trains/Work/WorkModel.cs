namespace Trax.Samples.Board.Trains.Work;

public sealed record ClaimWorkInput
{
    /// <summary>The lane to claim from: triage, reproduce, fix, qa or review. It must be the caller's own.</summary>
    public string Lane { get; init; } = "";

    /// <summary>A ticket to claim; the next free one in the lane when left out.</summary>
    public string? TicketId { get; init; }
}

/// <summary>Why a claim was refused: <c>FORBIDDEN</c>, <c>NOTHING_TO_CLAIM</c> or <c>ALREADY_CLAIMED</c>.</summary>
public sealed record ClaimProblem
{
    public string Code { get; init; } = "";

    public string Message { get; init; } = "";
}

/// <summary>
/// What a worker is handed with a claim: the ticket, what its lane expects, the server's tools it may call, the
/// assignment event that delivers its evidence, and how long the claim holds. Or why there is nothing to hand it.
/// </summary>
public sealed record WorkPackage
{
    public ClaimProblem? Problem { get; init; }

    public TicketView? Ticket { get; init; }

    public string Lane { get; init; } = "";

    public string Instructions { get; init; } = "";

    public IReadOnlyList<string> Tools { get; init; } = [];

    /// <summary>The assignment events that deliver this lane's evidence.</summary>
    public IReadOnlyList<string> Events { get; init; } = [];

    /// <summary>How long the claim holds, as an ISO 8601 duration.</summary>
    public string Deadline { get; init; } = "";
}

/// <summary>A ticket as a worker or the board page sees it.</summary>
public sealed record TicketView
{
    public string Id { get; init; } = "";

    public string Title { get; init; } = "";

    /// <summary>The report, with the policy the server holds the rules to.</summary>
    public string Report { get; init; } = "";

    public string State { get; init; } = "";

    /// <summary>The lane it is in: triage, reproduce, fix, qa, review, or done.</summary>
    public string Lane { get; init; } = "";

    public string? ClaimedBy { get; init; }

    /// <summary>The last thing a lane recorded on it, and who.</summary>
    public string? Note { get; init; }

    public string? By { get; init; }
}

public sealed record FileTicketInput
{
    /// <summary>The ticket's id on the board.</summary>
    public string Id { get; init; } = "";

    /// <summary>The report it is filed from (boundary, threshold, hundred, fixed-already).</summary>
    public string Report { get; init; } = "";
}

public sealed record BoardInput;

public sealed record BoardView
{
    public IReadOnlyList<TicketView> Tickets { get; init; } = [];
}

/// <summary>A tool a worker may call: what an order pays for shipping under a ticket's rules as they are today.</summary>
public sealed record ChargeInput
{
    public string TicketId { get; init; } = "";

    public decimal Subtotal { get; init; }
}

public sealed record ChargeOutput
{
    public decimal Subtotal { get; init; }

    public decimal Shipping { get; init; }
}
