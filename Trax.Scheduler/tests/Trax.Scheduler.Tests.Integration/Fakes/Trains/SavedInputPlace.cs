using Trax.Core.Functional;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Scheduler.Tests.Integration.Fakes.Trains;

/// <summary>An object <see cref="SavedInputShapes"/> can hold twice.</summary>
public record SavedInputPlace
{
    public string? City { get; set; }
    public int Zip { get; set; }
}
