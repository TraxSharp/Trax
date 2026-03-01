using LanguageExt;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Samples.Scheduler.Workflows.ExtractImport;

public interface IExtractImportWorkflow : IServiceTrain<ExtractImportInput, Unit>;
