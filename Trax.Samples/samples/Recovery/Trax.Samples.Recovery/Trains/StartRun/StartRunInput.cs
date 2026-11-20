using Trax.Samples.Recovery.Faults;

namespace Trax.Samples.Recovery.Trains.StartRun;

public record StartRunInput
{
    public required Scenario Scenario { get; init; }

    /// <summary>The order to refund, from <c>CaseFiles.Catalog</c>; A-1001 when left out. Refund only.</summary>
    public string? OrderId { get; init; }

    /// <summary>The fields to map; every field in the corpus when left out. Topic map only.</summary>
    public IReadOnlyList<string>? Fields { get; init; }

    /// <summary>The first year to map; 2016 when left out. Topic map only.</summary>
    public int? FromYear { get; init; }

    /// <summary>The last year to map; 2025 when left out. Topic map only.</summary>
    public int? ToYear { get; init; }

    /// <summary>Crash the scenario's crash point once, on the first attempt that reaches it.</summary>
    public bool CrashOnce { get; init; }

    /// <summary>Where a refund crashes when <see cref="CrashOnce"/> is set; while paying when left out.</summary>
    public RefundCrash? CrashWhile { get; init; }
}
