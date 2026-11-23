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

/// <summary>
/// The research chain, noting each call of its <c>Junctions()</c>: which instance, and whether it was
/// being read or run. Used by one test alone, so nothing else has read its chain first.
/// </summary>
public interface ICountingResearchTrain : IServiceTrain<string, string>;
