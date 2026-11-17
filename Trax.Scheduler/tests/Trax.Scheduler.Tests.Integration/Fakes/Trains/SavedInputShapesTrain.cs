using Trax.Core.Functional;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Scheduler.Tests.Integration.Fakes.Trains;

/// <summary>
/// A train whose input holds a list, an array, a nested object and room for one object twice:
/// the shapes the saved form of an input writes with reference metadata.
/// </summary>
public class SavedInputShapesTrain : ServiceTrain<SavedInputShapes, Unit>, ISavedInputShapesTrain
{
    protected override async Task<Either<Exception, Unit>> Junctions() => Resolve();
}
