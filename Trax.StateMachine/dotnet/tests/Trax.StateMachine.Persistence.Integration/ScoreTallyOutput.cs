using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Data.InMemory.Extensions;
using Trax.Effect.Data.Sqlite.Extensions;
using Trax.Effect.Extensions;
using Trax.Effect.Services.ServiceTrain;
using Trax.StateMachine.Features;
using Trax.StateMachine.Gherkin;

namespace Trax.StateMachine.Persistence.Integration;

/// <summary>Its output: an integer per name, so the glossary's values need an int's range.</summary>
public sealed record ScoreTallyOutput(IReadOnlyDictionary<string, int> Totals);
