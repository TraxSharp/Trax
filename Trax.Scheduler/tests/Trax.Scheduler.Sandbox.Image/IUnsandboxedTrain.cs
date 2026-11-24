using Trax.Effect.Services.ServiceTrain;

namespace Trax.Scheduler.Sandbox.Image;

/// <summary>A train in the image that does not carry [TraxSandbox], which a sandbox must refuse to run.</summary>
public interface IUnsandboxedTrain : IServiceTrain<HostileInput, HostileOutput>;
