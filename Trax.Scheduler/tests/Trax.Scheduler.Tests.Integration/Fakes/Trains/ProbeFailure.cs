using System.Collections.Concurrent;
using Trax.Core.Decisions;
using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Scheduler.Tests.Integration.Fakes.Trains;

/// <summary>Where <see cref="DecisionProbeTrain"/> fails, for a test that needs a partial run.</summary>
public enum ProbeFailure
{
    None,
    BeforeFirstQuestion,
    BetweenQuestions,

    /// <summary>After both questions are answered and both tracks taken.</summary>
    AfterQuestions,
}
