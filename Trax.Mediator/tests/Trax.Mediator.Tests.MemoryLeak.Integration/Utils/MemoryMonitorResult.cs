using System.Diagnostics;
using System.Globalization;

namespace Trax.Mediator.Tests.MemoryLeak.Integration.Utils;

/// <summary>
/// Results of memory monitoring operation.
/// </summary>
public record MemoryMonitorResult(
    string Description,
    MemoryProfiler.MemorySnapshot Before,
    MemoryProfiler.MemorySnapshot After,
    MemoryProfiler.MemorySnapshot AfterGC,
    TimeSpan Duration
)
{
    /// <summary>
    /// Memory allocated during the operation (before GC).
    /// </summary>
    public long MemoryAllocated => After.TotalMemory - Before.TotalMemory;

    /// <summary>
    /// Memory that remained after GC, as a delta of the whole process's managed heap.
    /// </summary>
    /// <remarks>
    /// Good for "nothing ran away" — a bound in megabytes. Not good for "everything was freed": see
    /// <see cref="MemoryProfiler.RetainedNoiseFloorBytes"/> for why, and use
    /// <see cref="MemoryProfiler.CountAliveAsync"/> for that claim instead. Do not bound this
    /// against <see cref="MemoryAllocated"/> either; both sides are the same noisy measurement, so
    /// the ratio tightens on its own whenever a collection happens to land inside the window.
    /// </remarks>
    public long MemoryRetained => AfterGC.TotalMemory - Before.TotalMemory;

    /// <summary>
    /// Memory that was freed by GC.
    /// </summary>
    public long MemoryFreed => After.TotalMemory - AfterGC.TotalMemory;

    /// <summary>
    /// Gets a formatted summary of the memory monitoring results.
    /// </summary>
    public string GetSummary()
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"""
            Memory Monitor Results: {Description}
            Duration: {Duration.TotalMilliseconds:F2}ms
            Memory Allocated: {MemoryAllocated:N0} bytes
            Memory Retained: {MemoryRetained:N0} bytes
            Memory Freed: {MemoryFreed:N0} bytes
            GC Collections: Gen0={After.Gen0Collections
                - Before.Gen0Collections}, Gen1={After.Gen1Collections
                - Before.Gen1Collections}, Gen2={After.Gen2Collections - Before.Gen2Collections}
            """
        );
    }
}
