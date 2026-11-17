using Trax.Effect.Services.ServiceTrain;

namespace Trax.StateMachine.Tests.UnitTests.LexiconTrains;

public interface INotedShotTrain : IServiceTrain<FireShotInput, NotedOutput>;
