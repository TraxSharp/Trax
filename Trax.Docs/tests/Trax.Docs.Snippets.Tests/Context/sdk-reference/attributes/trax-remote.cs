// What the example on sdk-reference/attributes/trax-remote.md names but does not declare.
using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Effect.Services.ServiceTrain;

public record RenderReportInput(string ReportId);

public record RenderedPdf(byte[] Content);

public interface IRenderReportTrain : IServiceTrain<RenderReportInput, Unit>;

public class RenderPdf : Junction<RenderReportInput, RenderedPdf>
{
    public override Task<RenderedPdf> Run(RenderReportInput input) =>
        Task.FromResult(new RenderedPdf([]));
}

public class UploadPdf : Junction<RenderedPdf, Unit>
{
    public override Task<Unit> Run(RenderedPdf input) => Task.FromResult(Unit.Default);
}
