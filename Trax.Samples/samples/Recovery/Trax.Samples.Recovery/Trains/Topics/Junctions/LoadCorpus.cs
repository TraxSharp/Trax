using Microsoft.EntityFrameworkCore;
using Trax.Effect.Services.EffectJunction;
using Trax.Samples.Recovery.Corpus;

namespace Trax.Samples.Recovery.Trains.Topics.Junctions;

/// <summary>Reads the papers in the fields and years the input chose. Every branch starts from them.</summary>
public class LoadCorpus(ITopicMapDbContext db, DemoPace pace)
    : EffectJunction<TopicMapInput, CorpusSlice>
{
    public override async Task<CorpusSlice> Run(TopicMapInput input)
    {
        if (input.FromYear > input.ToYear)
            throw new ArgumentException(
                $"The year range {input.FromYear} to {input.ToYear} is empty.",
                nameof(input)
            );

        await Task.Delay(pace.StepDelay, CancellationToken);

        var fields = input.Fields.ToList();
        var works = await db
            .Works.AsNoTracking()
            .Where(w =>
                fields.Contains(w.Field) && w.Year >= input.FromYear && w.Year <= input.ToYear
            )
            .OrderBy(w => w.Id)
            .ToListAsync(CancellationToken);

        if (works.Count < 2)
            throw new ArgumentException(
                $"{string.Join(", ", fields)} from {input.FromYear} to {input.ToYear} holds "
                    + $"{works.Count} paper(s); a map needs at least two.",
                nameof(input)
            );

        return new CorpusSlice(
            input.RunId,
            works
                .Select(w => new Paper(
                    w.Id,
                    w.Title,
                    w.Abstract,
                    w.Authors,
                    w.References,
                    w.Concepts
                ))
                .ToList()
        );
    }
}
