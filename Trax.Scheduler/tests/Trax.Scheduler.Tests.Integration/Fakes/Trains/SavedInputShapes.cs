using Trax.Core.Functional;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Scheduler.Tests.Integration.Fakes.Trains;

/// <summary>Input for <see cref="SavedInputShapesTrain"/>.</summary>
public record SavedInputShapes
{
    public List<int>? Counts { get; set; }
    public string[]? Tags { get; set; }
    public SavedInputPlace? Home { get; set; }
    public SavedInputPlace? Billing { get; set; }
}
