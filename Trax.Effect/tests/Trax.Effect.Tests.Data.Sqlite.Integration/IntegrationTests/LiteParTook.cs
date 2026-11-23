using System.Collections.Concurrent;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Core.Decisions;
using Trax.Core.Functional;
using Trax.Core.Monad;
using Trax.Effect.Data.Extensions;
using Trax.Effect.Data.JunctionEvents;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Data.Sqlite.Extensions;
using Trax.Effect.Extensions;
using Trax.Effect.Models.JunctionRun;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Models.Metadata.DTOs;
using Trax.Effect.Services.EffectJunction;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Effect.Tests.Data.Sqlite.Integration.IntegrationTests;

internal static class LiteParTook
{
    private static readonly ConcurrentQueue<(long Run, string Branch, string Track)> Taken = new();

    public static void Note(long run, string track) =>
        Taken.Enqueue((run, ChainGraph.CurrentBranchPath ?? "", track));

    public static List<(string Branch, string Track)> Of(long run) =>
        Taken.Where(t => t.Run == run).Select(t => (t.Branch, t.Track)).ToList();
}
