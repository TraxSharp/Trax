using System.Collections.Concurrent;
using System.Globalization;
using Trax.Core.Decisions;
using Trax.Core.Functional;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Models.ManifestGroup;
using Trax.Effect.Services.EffectJunction;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Scheduler.Tests.Integration.Fakes.Trains;

[Asks("How deep should the research go?")]
public enum ResearchDepth
{
    Quick,
    Deep,
}
