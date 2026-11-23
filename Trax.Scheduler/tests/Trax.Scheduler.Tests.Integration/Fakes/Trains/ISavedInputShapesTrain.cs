using Trax.Core.Functional;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Scheduler.Tests.Integration.Fakes.Trains;

/// <summary>Interface for <see cref="SavedInputShapesTrain"/>.</summary>
public interface ISavedInputShapesTrain : IServiceTrain<SavedInputShapes, Unit>;
