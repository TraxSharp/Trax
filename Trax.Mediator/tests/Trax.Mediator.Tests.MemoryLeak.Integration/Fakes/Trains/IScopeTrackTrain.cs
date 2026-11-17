using Trax.Core.Functional;
using Trax.Effect.Attributes;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Mediator.Tests.MemoryLeak.Integration.Fakes.Trains;

public interface IScopeTrackTrain : IServiceTrain<ScopeTrackInput, ScopeTrackOutput>;
