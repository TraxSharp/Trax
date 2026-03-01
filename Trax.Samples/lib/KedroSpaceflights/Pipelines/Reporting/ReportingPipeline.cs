using Flowthru.Pipelines;
using KedroSpaceflights.Data;
using KedroSpaceflights.Pipelines.Reporting.Nodes;

namespace KedroSpaceflights.Pipelines.Reporting;

/// <summary>
/// Reporting pipeline that generates visualizations from processed data.
/// Matches Kedro spaceflights reporting pipeline structure.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Pipeline Purpose:</strong> Generate visualizations for data exploration using Plotly.NET.
/// Charts are first created in memory, then exported to PNG format for reports.
/// </para>
/// <para>
/// <strong>Architecture:</strong>
/// This pipeline follows a two-stage pattern for each visualization:
/// 1. Chart Generation (data → GenericChart in memory)
/// 2. PNG Export (GenericChart → PNG binary file)
///
/// This separation enables reusable export nodes across different chart types.
/// </para>
/// </remarks>
public static class ReportingPipeline
{
    /// <summary>
    /// Configuration parameters for the reporting pipeline.
    /// </summary>
    public record Params
    {
        /// <summary>
        /// Configuration options for confusion matrix generation.
        /// </summary>
        public CreateConfusionMatrixNode.Options ConfusionMatrixOptions { get; init; } = new();
    }

    /// <summary>
    /// Creates the reporting pipeline.
    /// </summary>
    /// <param name="catalog">The data catalog containing input and output entries.</param>
    /// <param name="parameters">Configuration parameters for the pipeline (optional).</param>
    /// <returns>A configured pipeline that produces visualizations and reports.</returns>
    public static Pipeline Create(Catalog catalog, Params? parameters = null)
    {
        var p = parameters ?? new Params();

        return PipelineBuilder.CreatePipeline(pipeline =>
        {
            // ===== Shuttle Passenger Capacity Report (JSON) =====

            pipeline.AddNode(
                label: "ComparePassengerCapacity",
                transform: ComparePassengerCapacityNode.Create(),
                input: catalog.PreprocessedShuttles,
                output: catalog.ShuttleCapacityReport
            );

            // ===== Shuttle Passenger Capacity Visualization =====

            // Step 1: Generate chart from preprocessed shuttle data
            pipeline.AddNode(
                label: "GeneratePassengerCapacityChart",
                transform: GeneratePassengerCapacityChartNode.Create(),
                input: catalog.PreprocessedShuttles,
                output: catalog.ShuttlePassengerCapacityChart
            );

            // Step 2: Export chart to PNG for static reports
            pipeline.AddNode(
                label: "ExportPassengerCapacityPng",
                transform: PlotlyImageExportNode.Create(),
                input: catalog.ShuttlePassengerCapacityChart,
                output: catalog.ShuttlePassengerCapacityPlotPng
            );

            // ===== Confusion Matrix Visualization =====

            // Step 1: Generate confusion matrix heatmap from model predictions
            pipeline.AddNode(
                label: "GenerateConfusionMatrixChart",
                transform: CreateConfusionMatrixNode.Create(p.ConfusionMatrixOptions),
                input: catalog.ModelPredictions,
                output: catalog.ConfusionMatrixChart
            );

            // Step 2: Export chart to PNG for static reports
            pipeline.AddNode(
                label: "ExportConfusionMatrixPng",
                transform: PlotlyImageExportNode.Create(),
                input: catalog.ConfusionMatrixChart,
                output: catalog.ConfusionMatrixPlotPng
            );
        });
    }
}
