using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Services.EffectJunction;
using Trax.Mediator.Services.RunStarters;

namespace Trax.Samples.Recovery.Trains.DecisionJournal.Junctions;

public class ReadDecisionJournal(IDataContextProviderFactory dataContexts, IRunStarters starters)
    : EffectJunction<DecisionJournalInput, DecisionJournalOutput>
{
    public override async Task<DecisionJournalOutput> Run(DecisionJournalInput input)
    {
        // Only the caller who started the run reads its decisions; any other run answers as one with none recorded.
        // The execution read is the caller's own latest under the id, never another one recorded under the same id.
        if (
            await starters.LatestExecutionStartedByCurrentCallerAsync(
                input.RunId,
                CancellationToken
            )
            is not { } metadataId
        )
            return new DecisionJournalOutput { RunId = input.RunId, Decisions = [] };

        await using var context = await dataContexts.CreateDbContextAsync(CancellationToken);

        var metadata = await context
            .Metadatas.AsNoTracking()
            .Where(m => m.Id == metadataId)
            .Select(m => new
            {
                m.Id,
                m.ReplayDecisionsOf,
                m.ReplayAbandoned,
            })
            .FirstOrDefaultAsync(CancellationToken);
        if (metadata is null)
            return new DecisionJournalOutput { RunId = input.RunId, Decisions = [] };

        var rows = await context
            .RecordedDecisions.AsNoTracking()
            .Where(d => d.MetadataId == metadata.Id)
            .OrderBy(d => d.Id)
            .ToListAsync(CancellationToken);

        return new DecisionJournalOutput
        {
            RunId = input.RunId,
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
                    Decider = d.Decider?[(d.Decider.LastIndexOf('.') + 1)..],
                    Question = d.Question,
                    Answer = d.Answer,
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
