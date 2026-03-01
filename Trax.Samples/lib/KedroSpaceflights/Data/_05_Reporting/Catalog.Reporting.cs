using Flowthru.Data;
using KedroSpaceflights.Data._05_Reporting.Schemas;
using Plotly.NET;

namespace KedroSpaceflights.Data;

public partial class Catalog
{
    /// <summary>
    /// Passenger capacity analysis report grouped by shuttle type.
    /// </summary>
    public ICatalogEntry<IEnumerable<ShuttleCapacityReport>> ShuttleCapacityReport =>
        GetOrCreateEntry(() =>
            CatalogEntries.Enumerable.Json<ShuttleCapacityReport>(
                label: "ShuttleCapacityReport",
                filePath: $"{_basePath}/_05_Reporting/Datasets/shuttle_capacity_report.json"
            )
        );

    /// <summary>
    /// Shuttle passenger capacity bar chart (in-memory GenericChart).
    /// Intermediate chart object stored in memory for downstream export to PNG.
    /// </summary>
    public ICatalogEntry<GenericChart> ShuttlePassengerCapacityChart =>
        GetOrCreateEntry(() =>
            CatalogEntries.Single.Memory<GenericChart>(label: "ShuttlePassengerCapacityChart")
        );

    /// <summary>
    /// Shuttle passenger capacity bar chart (PNG image).
    /// Static image representation of the passenger capacity visualization.
    /// Stored as binary PNG file.
    /// </summary>
    public ICatalogEntry<byte[]> ShuttlePassengerCapacityPlotPng =>
        GetOrCreateEntry(() =>
            CatalogEntries.Single.Binary(
                label: "ShuttlePassengerCapacityPlotPng",
                filePath: $"{_basePath}/_05_Reporting/Images/shuttle_passenger_capacity_plot.png"
            )
        );

    /// <summary>
    /// Confusion matrix heatmap (in-memory GenericChart).
    /// Intermediate chart object stored in memory for downstream export to PNG.
    /// </summary>
    public ICatalogEntry<GenericChart> ConfusionMatrixChart =>
        GetOrCreateEntry(() =>
            CatalogEntries.Single.Memory<GenericChart>(label: "ConfusionMatrixChart")
        );

    /// <summary>
    /// Confusion matrix heatmap (PNG image).
    /// Static image representation of the confusion matrix visualization.
    /// Stored as binary PNG file.
    /// </summary>
    public ICatalogEntry<byte[]> ConfusionMatrixPlotPng =>
        GetOrCreateEntry(() =>
            CatalogEntries.Single.Binary(
                label: "ConfusionMatrixPlotPng",
                filePath: $"{_basePath}/_05_Reporting/Images/confusion_matrix_plot.png"
            )
        );
}
