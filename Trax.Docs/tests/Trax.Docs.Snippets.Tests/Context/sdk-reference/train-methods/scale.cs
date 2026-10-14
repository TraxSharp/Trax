// What the example on sdk-reference/train-methods/scale.md names but does not declare.
using Trax.Core.Decisions;
using Trax.Core.Junction;

public record Ticket(string Id, string Summary);

public record TriageResult(string Queue);

[Asks("How urgent is this ticket?")]
public enum Urgency
{
    Low,
    Medium,
    High,
}

public class QueueNormally : Junction<Ticket, TriageResult>
{
    public override Task<TriageResult> Run(Ticket input) =>
        Task.FromResult(new TriageResult("normal"));
}

public class PageOnCall : Junction<Ticket, TriageResult>
{
    public override Task<TriageResult> Run(Ticket input) =>
        Task.FromResult(new TriageResult("on-call"));
}
