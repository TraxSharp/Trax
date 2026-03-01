using KedroSpaceflights.Data._02_Intermediate.Schemas;
using Microsoft.Extensions.Logging;
using Plotly.NET;
using Plotly.NET.LayoutObjects;
using CSharpChart = Plotly.NET.CSharp.Chart;

namespace KedroSpaceflights.Pipelines.Reporting.Nodes;

/// <summary>
/// Generates a bar chart visualization comparing average passenger capacity by shuttle type.
/// Matches Kedro's compare_passenger_capacity_go function using plotly.graph_objects.
/// </summary>
/// <remarks>
/// <para>
/// This node creates a bar chart showing average passenger capacity grouped by shuttle type.
/// The input data is aggregated preprocessed shuttle data, and the output is a GenericChart
/// ready for downstream PNG export.
/// </para>
/// <para>
/// <strong>Input:</strong> Preprocessed shuttle data
/// </para>
/// <para>
/// <strong>Output:</strong> GenericChart bar chart stored in memory for downstream PNG export
/// </para>
/// </remarks>
public static class GeneratePassengerCapacityChartNode
{
    public static Func<IEnumerable<PreprocessedShuttleSchema>, Task<GenericChart>> Create(
        ILogger? logger = null
    )
    {
        return async (input) =>
        {
            var shuttles = input.ToList();

            logger?.LogInformation(
                "Generating passenger capacity chart from {Count} shuttle records",
                shuttles.Count
            );

            // Group by shuttle type and calculate average passenger capacity
            // Sort by capacity descending to show ranking from highest to lowest
            var aggregated = shuttles
                .GroupBy(s => s.ShuttleType)
                .Select(g => new
                {
                    ShuttleType = g.Key,
                    AvgPassengerCapacity = g.Average(s => s.PassengerCapacity),
                })
                .OrderByDescending(x => x.AvgPassengerCapacity)
                .ToList();

            logger?.LogInformation(
                "Aggregated {Count} shuttle types, sorted by capacity (highest to lowest)",
                aggregated.Count
            );

            // Extract data for chart
            var shuttleTypes = aggregated.Select(x => x.ShuttleType).ToList();
            var capacities = aggregated.Select(x => x.AvgPassengerCapacity).ToList();

            // Create column chart using Plotly.NET.CSharp API
            // Note: Both test and examples projects produce similar rendering with this API
            var chart = CSharpChart
                .Column<string, double, double>(shuttleTypes, capacities)
                .WithXAxisStyle(Title.init("Shuttle Type (Ranked by Capacity)"))
                .WithYAxisStyle(Title.init("Average Passenger Capacity"))
                .WithTitle("Shuttle Passenger Capacity Rankings")
                .WithSize(1000, 600);

            logger?.LogInformation("Generated sorted passenger capacity bar chart");

            return chart;
        };
    }
}
