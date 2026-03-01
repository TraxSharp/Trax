using KedroSpaceflights.Data._02_Intermediate.Schemas;
using KedroSpaceflights.Data._05_Reporting.Schemas;

namespace KedroSpaceflights.Pipelines.Reporting.Nodes;

/// <summary>
/// Aggregates shuttle passenger capacity data by shuttle type.
/// </summary>
public static class ComparePassengerCapacityNode
{
    /// <summary>
    /// Creates a function that computes average passenger capacity grouped by shuttle type.
    /// </summary>
    /// <returns>
    /// A function that produces <see cref="ShuttleCapacityReport"/> records showing
    /// average capacity for each shuttle type.
    /// </returns>
    public static Func<
        IEnumerable<PreprocessedShuttleSchema>,
        Task<IEnumerable<ShuttleCapacityReport>>
    > Create()
    {
        return async (input) =>
        {
            var report = input
                .GroupBy(s => s.ShuttleType)
                .Select(g => new ShuttleCapacityReport
                {
                    ShuttleType = g.Key,
                    AvgPassengerCapacity = (decimal)g.Average(s => s.PassengerCapacity),
                });

            return await Task.FromResult(report);
        };
    }
}
