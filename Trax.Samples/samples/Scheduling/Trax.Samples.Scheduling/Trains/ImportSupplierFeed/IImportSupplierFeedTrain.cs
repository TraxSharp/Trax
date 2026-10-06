using Trax.Effect.Services.ServiceTrain;

namespace Trax.Samples.Scheduling.Trains.ImportSupplierFeed;

public interface IImportSupplierFeedTrain
    : IServiceTrain<ImportSupplierFeedInput, ImportSupplierFeedOutput>;
