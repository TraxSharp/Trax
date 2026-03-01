using KedroSpaceflights.Data._04_Models.Schemas;
using Microsoft.Extensions.Logging;
using Plotly.NET;
using Plotly.NET.LayoutObjects;
using CSharpChart = Plotly.NET.CSharp.Chart;

namespace KedroSpaceflights.Pipelines.Reporting.Nodes;

/// <summary>
/// Creates a confusion matrix heatmap from actual model predictions.
/// Converts continuous regression predictions into multi-class classification for visualization.
/// </summary>
/// <remarks>
/// <para>
/// This node generates a confusion matrix by binning continuous prediction values into
/// percentile-based ranges (quartiles, quintiles, deciles, etc.). This provides a more
/// granular view of prediction accuracy across different value ranges compared to simple
/// binary classification.
/// </para>
/// <para>
/// <strong>Input:</strong> ModelPredictions enumerable with Actual and Predicted values
/// </para>
/// <para>
/// <strong>Output:</strong> GenericChart heatmap object stored in memory for downstream PNG export
/// </para>
/// </remarks>
public static class CreateConfusionMatrixNode
{
    /// <summary>
    /// Configuration options for confusion matrix generation.
    /// </summary>
    public record Options
    {
        /// <summary>
        /// Number of bins to divide the predictions into (e.g., 4 for quartiles, 5 for quintiles, 10 for deciles).
        /// Default is 4 (quartiles).
        /// </summary>
        public int NumBins { get; init; } = 4;
    }

    public static Func<IEnumerable<ModelPredictions>, Task<GenericChart>> Create(
        Options? options = null,
        ILogger? logger = null
    )
    {
        var opts = options ?? new Options();

        return async (input) =>
        {
            var predictions = input.ToList();

            if (!predictions.Any())
            {
                throw new InvalidOperationException(
                    "Cannot create confusion matrix from empty predictions"
                );
            }

            logger?.LogInformation(
                "Generating confusion matrix from {Count} predictions using {NumBins} bins",
                predictions.Count,
                opts.NumBins
            );

            // Calculate percentile thresholds based on actual values
            var sortedActuals = predictions.Select(p => p.Actual).OrderBy(v => v).ToList();
            var thresholds = CalculatePercentileThresholds(sortedActuals, opts.NumBins);

            logger?.LogInformation(
                "Percentile thresholds: [{Thresholds}]",
                string.Join(", ", thresholds.Select(t => $"{t:F2}"))
            );

            // Bin predictions into classes
            var binnedPredictions = predictions
                .Select(p =>
                    (
                        Actual: AssignBin(p.Actual, thresholds),
                        Predicted: AssignBin(p.Predicted, thresholds)
                    )
                )
                .ToList();

            // Build NxN confusion matrix
            var matrix = new int[opts.NumBins, opts.NumBins];
            foreach (var (actual, predicted) in binnedPredictions)
            {
                matrix[actual, predicted]++;
            }

            // Convert to format for Plotly heatmap (list of lists)
            var zData = new List<List<int>>();
            for (int i = 0; i < opts.NumBins; i++)
            {
                var row = new List<int>();
                for (int j = 0; j < opts.NumBins; j++)
                {
                    row.Add(matrix[i, j]);
                }
                zData.Add(row);
            }

            // Generate labels based on percentile ranges
            var labels = GeneratePercentileLabels(opts.NumBins);
            var xLabels = labels.Select(l => $"Pred {l}").ToArray();
            var yLabels = labels.Select(l => $"Actual {l}").ToArray();

            logger?.LogInformation(
                "Generated {Size}x{Size} confusion matrix",
                opts.NumBins,
                opts.NumBins
            );

            // Create heatmap using Plotly.NET.CSharp API
            var binName = opts.NumBins switch
            {
                2 => "Median Split",
                3 => "Tertiles",
                4 => "Quartiles",
                5 => "Quintiles",
                10 => "Deciles",
                _ => $"{opts.NumBins} Bins",
            };

            var chart = CSharpChart
                .Heatmap<int, string, string, int>(zData, X: xLabels, Y: yLabels, ShowScale: true)
                .WithTitle($"Confusion Matrix ({binName})")
                .WithSize(Math.Max(600, opts.NumBins * 80), Math.Max(600, opts.NumBins * 80));

            logger?.LogInformation("Generated GenericChart heatmap for confusion matrix");

            return chart;
        };
    }

    /// <summary>
    /// Calculates percentile threshold values for binning.
    /// </summary>
    private static List<double> CalculatePercentileThresholds(
        List<double> sortedValues,
        int numBins
    )
    {
        var thresholds = new List<double>();
        for (int i = 1; i < numBins; i++)
        {
            var percentile = (double)i / numBins;
            var index = (int)(percentile * sortedValues.Count);
            thresholds.Add(sortedValues[Math.Min(index, sortedValues.Count - 1)]);
        }
        return thresholds;
    }

    /// <summary>
    /// Assigns a value to a bin based on percentile thresholds.
    /// </summary>
    private static int AssignBin(double value, List<double> thresholds)
    {
        for (int i = 0; i < thresholds.Count; i++)
        {
            if (value < thresholds[i])
            {
                return i;
            }
        }
        return thresholds.Count; // Last bin
    }

    /// <summary>
    /// Generates human-readable percentile range labels.
    /// </summary>
    private static List<string> GeneratePercentileLabels(int numBins)
    {
        var labels = new List<string>();
        for (int i = 0; i < numBins; i++)
        {
            var startPercentile = (i * 100) / numBins;
            var endPercentile = ((i + 1) * 100) / numBins;
            labels.Add($"P{startPercentile}-{endPercentile}");
        }
        return labels;
    }
}
