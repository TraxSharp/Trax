// What the example in the trax-schedule-work skill takes as given: the trains it schedules.
using Trax.Core.Functional;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Services.ServiceTrain;

public record ExtractOrdersInput : IManifestProperties;

public interface IExtractOrdersTrain : IServiceTrain<ExtractOrdersInput, Unit>;

public record LoadOrdersInput : IManifestProperties;

public interface ILoadOrdersTrain : IServiceTrain<LoadOrdersInput, Unit>;

public record NightlyReportInput : IManifestProperties;

public interface INightlyReportTrain : IServiceTrain<NightlyReportInput, Unit>;
