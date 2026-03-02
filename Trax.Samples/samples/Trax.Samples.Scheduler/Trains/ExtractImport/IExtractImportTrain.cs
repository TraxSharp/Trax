using LanguageExt;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Samples.Scheduler.Trains.ExtractImport;

public interface IExtractImportTrain : IServiceTrain<ExtractImportInput, Unit>;
