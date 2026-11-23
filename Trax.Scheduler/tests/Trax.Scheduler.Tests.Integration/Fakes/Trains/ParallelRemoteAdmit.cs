using System.Globalization;
using Trax.Core.Functional;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Services.EffectJunction;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Scheduler.Tests.Integration.Fakes.Trains;

public class ParallelRemoteAdmit : EffectJunction<ParallelRemoteInput, ParallelRemoteInput>
{
    public override Task<ParallelRemoteInput> Run(ParallelRemoteInput input) =>
        Task.FromResult(input);
}
