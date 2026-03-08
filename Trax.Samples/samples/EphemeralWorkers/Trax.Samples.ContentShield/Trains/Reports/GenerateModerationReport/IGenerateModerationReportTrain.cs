using Trax.Effect.Services.ServiceTrain;

namespace Trax.Samples.ContentShield.Trains.Reports.GenerateModerationReport;

public interface IGenerateModerationReportTrain
    : IServiceTrain<GenerateModerationReportInput, GenerateModerationReportOutput>;
