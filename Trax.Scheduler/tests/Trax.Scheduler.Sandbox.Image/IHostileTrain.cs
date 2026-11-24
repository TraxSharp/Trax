using Trax.Effect.Services.ServiceTrain;

namespace Trax.Scheduler.Sandbox.Image;

/// <summary>The one train in the escape image.</summary>
public interface IHostileTrain : IServiceTrain<HostileInput, HostileOutput>;
