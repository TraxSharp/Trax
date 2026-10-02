using LanguageExt;
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

/// <summary>Input for <see cref="SavedInputShapesTrain"/>.</summary>
public record SavedInputShapes
{
    public List<int>? Counts { get; set; }
    public string[]? Tags { get; set; }
    public SavedInputPlace? Home { get; set; }
    public SavedInputPlace? Billing { get; set; }
}

/// <summary>An object <see cref="SavedInputShapes"/> can hold twice.</summary>
public record SavedInputPlace
{
    public string? City { get; set; }
    public int Zip { get; set; }
}

/// <summary>Interface for <see cref="SavedInputShapesTrain"/>.</summary>
public interface ISavedInputShapesTrain : IServiceTrain<SavedInputShapes, Unit> { }
