using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Trax.Core.Decisions;
using Trax.Core.Exceptions;
using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Effect.Data.Decisions;
using Trax.Effect.Data.Extensions;
using Trax.Effect.Data.InMemory.Extensions;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Enums;
using Trax.Effect.Extensions;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Models.Metadata.DTOs;
using Trax.Effect.Services.Decisions;
using Trax.Effect.Services.EffectJunction;
using Trax.Effect.Services.EffectProvider;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Effect.Tests.Data.InMemory.Integration.IntegrationTests;

/// <summary>A shadow whose answer carries numbers JSON has no form for.</summary>
public sealed class NonFiniteShadow : IDecider
{
    public Task<DecisionResult> Decide(DecisionRequest request, CancellationToken ct) =>
        Task.FromResult(
            new DecisionResult(
                new Dictionary<string, Answer>
                {
                    [QuestionKey.For<Fulfilment>()] = new ChoiceAnswer(
                        "Standard",
                        double.NaN,
                        new Dictionary<string, double> { ["Standard"] = double.PositiveInfinity }
                    ),
                }
            )
        );
}
