using Trax.Effect.Services.ServiceTrain;
using LanguageExt;

namespace Trax.Samples.Scheduler.Workflows.ExtractImport;

public interface IExtractImportWorkflow : IServiceTrain<ExtractImportInput, Unit>;
