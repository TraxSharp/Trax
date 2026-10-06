using Trax.Effect.Services.ServiceTrain;

namespace Trax.Samples.EnergyHub.Trains.Sustainability.GenerateSustainabilityReport;

public interface IGenerateSustainabilityReportTrain
    : IServiceTrain<GenerateSustainabilityReportInput, GenerateSustainabilityReportOutput>;
