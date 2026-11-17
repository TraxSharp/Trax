using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Trax.Core.Decisions;
using Trax.Core.Exceptions;
using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Effect.Data.Extensions;
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Enums;
using Trax.Effect.Extensions;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Models.Metadata.DTOs;
using Trax.Effect.Services.EffectJunction;
using Trax.Effect.Services.ServiceTrain;
using Trax.Effect.Tests.Integration.Fixtures;

namespace Trax.Effect.Tests.Integration.IntegrationTests;

/// <summary>Reads the tracks out of a recorded decision's routes.</summary>
internal static class RecordedRoutes
{
    public static IReadOnlyList<string> Tracks(
        this Models.RecordedDecision.RecordedDecision decision
    ) =>
        decision.Routes is null
            ? []
            : System
                .Text.Json.Nodes.JsonNode.Parse(decision.Routes)!
                .AsArray()
                .Select(route => (string)route!["track"]!)
                .ToList();
}
