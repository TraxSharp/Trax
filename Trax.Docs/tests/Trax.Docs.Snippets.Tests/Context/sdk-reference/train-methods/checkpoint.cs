// What the example on sdk-reference/train-methods/checkpoint.md names but does not declare: a
// research train's brief, findings, the checkpoint state and the junctions of its chain.
using Trax.Core.Decisions;
using Trax.Core.Junction;

public record ResearchRequest(string Topic);

public record ResearchBrief(string Topic);

public sealed record Findings(IReadOnlyList<string> SourceIds);

public sealed record CheckedFindings(Findings Findings, string Depth);

public record ResearchReport(string Summary);

[Asks("Where should this topic be researched?")]
public enum Source
{
    Web,
    Papers,
}

[Asks("How deep should the sources be read?")]
public enum Depth
{
    Skim,
    CrossCheck,
}

public class PlanResearch : Junction<ResearchRequest, ResearchBrief>
{
    public override Task<ResearchBrief> Run(ResearchRequest input) =>
        Task.FromResult(new ResearchBrief(input.Topic));
}

public class SearchWeb : Junction<ResearchBrief, Findings>
{
    public override Task<Findings> Run(ResearchBrief input) => Task.FromResult(new Findings([]));
}

public class SearchPapers : Junction<ResearchBrief, Findings>
{
    public override Task<Findings> Run(ResearchBrief input) => Task.FromResult(new Findings([]));
}

public class SkimSources : Junction<Findings, CheckedFindings>
{
    public override Task<CheckedFindings> Run(Findings input) =>
        Task.FromResult(new CheckedFindings(input, "skim"));
}

public class FetchFullTexts : Junction<Findings, CheckedFindings>
{
    public override Task<CheckedFindings> Run(Findings input) =>
        Task.FromResult(new CheckedFindings(input, "cross-check"));
}

public class Summarize : Junction<CheckedFindings, ResearchReport>
{
    public override Task<ResearchReport> Run(CheckedFindings input) =>
        Task.FromResult(new ResearchReport($"{input.Findings.SourceIds.Count} sources"));
}
