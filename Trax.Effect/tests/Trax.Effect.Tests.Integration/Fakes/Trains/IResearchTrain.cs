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
/// <c>PlanResearch → Switch&lt;Brief, ResearchSource&gt; → FetchFullTexts →
/// Checkpoint&lt;CheckedFindings&gt; → ScoreFindings → Checkpoint&lt;Scored&gt; → Summarize</c>.
/// </summary>
public interface IResearchTrain : IServiceTrain<string, string>;
