using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Services.EffectJunction;

namespace Trax.Samples.Recovery.Trains.DecisionJournal.Junctions;

public class ReadDecisionJournal(IDataContextProviderFactory dataContexts)
    : EffectJunction<DecisionJournalInput, DecisionJournalOutput>
{
    public override async Task<DecisionJournalOutput> Run(DecisionJournalInput input)
    {
        await using var context = await dataContexts.CreateDbContextAsync(CancellationToken);

        var metadata = await context
            .Metadatas.AsNoTracking()
            .Where(m => m.Id == input.MetadataId)
            .Select(m => new { m.ReplayDecisionsOf, m.ReplayAbandoned })
            .FirstOrDefaultAsync(CancellationToken);

        var rows = await context
            .RecordedDecisions.AsNoTracking()
            .Where(d => d.MetadataId == input.MetadataId)
            .OrderBy(d => d.Id)
            .ToListAsync(CancellationToken);

        return new DecisionJournalOutput
        {
            MetadataId = input.MetadataId,
            ReplayDecisionsOf = metadata?.ReplayDecisionsOf,
            ReplayAbandoned = metadata?.ReplayAbandoned ?? false,
            Decisions = rows.Select(d => new RecordedDecisionView
                {
                    QuestionKey = d.QuestionKey,
                    Occurrence = d.Occurrence,
                    Kind = d.Kind,
                    Replayed = d.Replayed,
                    ReplayRefused = ReplayRefused(d.Answer),
                    Model = d.Model,
                    StateHash = d.StateHash is { Length: > 15 } hash ? hash[..15] : d.StateHash,
                    DecidedAt = d.DecidedAt,
                })
                .ToList(),
        };
    }

    private static string? ReplayRefused(string? answerJson)
    {
        if (answerJson is null)
            return null;
        return JsonNode.Parse(answerJson)?["replay_refused"]?.GetValue<string>();
    }
}
