using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Trax.Core.Decisions;
using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Effect.Attributes;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Models.ManifestGroup;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Effect.Tests.Integration.Fakes.Trains;

/// <summary>Answers every question with the choice set for it.</summary>
public sealed class ScriptedResearchDecider : IDecider
{
    public IDictionary<string, string> Choices { get; } =
        new Dictionary<string, string>(StringComparer.Ordinal);

    public int Asked { get; set; }

    public void Reset()
    {
        Choices.Clear();
        Choices[QuestionKey.For<ResearchSource>()] = nameof(ResearchSource.Papers);
        Choices[QuestionKey.For<Vault>()] = nameof(Vault.Left);
        Asked = 0;
    }

    public Task<DecisionResult> Decide(DecisionRequest request, CancellationToken ct)
    {
        Asked++;
        return Task.FromResult(
            new DecisionResult(
                Choices.ToDictionary(
                    c => c.Key,
                    c => (Answer)new ChoiceAnswer(c.Value),
                    StringComparer.Ordinal
                )
            )
        );
    }
}
