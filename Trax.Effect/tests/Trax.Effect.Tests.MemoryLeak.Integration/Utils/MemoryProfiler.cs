using System.Diagnostics;

namespace Trax.Effect.Tests.MemoryLeak.Integration.Utils;

/// <summary>
/// Utility class for monitoring memory usage during tests.
/// Provides methods to track memory allocation, garbage collection, and disposal behavior.
/// </summary>
public static class MemoryProfiler
{
    /// <summary>
    /// Represents a memory measurement snapshot.
    /// </summary>
    public record MemorySnapshot(
        long TotalMemory,
        int Gen0Collections,
        int Gen1Collections,
        int Gen2Collections,
        DateTime Timestamp
    )
    {
        /// <summary>
        /// Calculates the difference between this snapshot and another.
        /// </summary>
        /// <param name="other">The other snapshot to compare against</param>
        /// <returns>A new snapshot representing the difference</returns>
        public MemorySnapshot Diff(MemorySnapshot other)
        {
            return new MemorySnapshot(
                TotalMemory - other.TotalMemory,
                Gen0Collections - other.Gen0Collections,
                Gen1Collections - other.Gen1Collections,
                Gen2Collections - other.Gen2Collections,
                Timestamp
            );
        }
    }

    /// <summary>
    /// Takes a snapshot of current memory usage and GC statistics.
    /// </summary>
    /// <param name="forceGC">Whether to force garbage collection before taking the snapshot</param>
    /// <returns>Memory snapshot</returns>
    public static MemorySnapshot TakeSnapshot(bool forceGC = false)
    {
        if (forceGC)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        return new MemorySnapshot(
            GC.GetTotalMemory(false),
            GC.CollectionCount(0),
            GC.CollectionCount(1),
            GC.CollectionCount(2),
            DateTime.UtcNow
        );
    }

    /// <summary>
    /// Monitors memory usage during the execution of an action.
    /// </summary>
    /// <param name="action">The action to monitor</param>
    /// <param name="description">Description of the operation being monitored</param>
    /// <returns>Memory usage information</returns>
    public static MemoryMonitorResult MonitorMemoryUsage(Action action, string description = "")
    {
        var beforeSnapshot = TakeSnapshot(forceGC: true);
        var stopwatch = Stopwatch.StartNew();

        action();

        stopwatch.Stop();
        var afterSnapshot = TakeSnapshot(forceGC: false);
        var afterGCSnapshot = TakeSnapshot(forceGC: true);

        return new MemoryMonitorResult(
            description,
            beforeSnapshot,
            afterSnapshot,
            afterGCSnapshot,
            stopwatch.Elapsed
        );
    }

    /// <summary>
    /// Monitors memory usage during the execution of an async action.
    /// </summary>
    /// <param name="action">The async action to monitor</param>
    /// <param name="description">Description of the operation being monitored</param>
    /// <returns>Memory usage information</returns>
    public static async Task<MemoryMonitorResult> MonitorMemoryUsageAsync(
        Func<Task> action,
        string description = ""
    )
    {
        var beforeSnapshot = TakeSnapshot(forceGC: true);
        var stopwatch = Stopwatch.StartNew();

        await action();

        stopwatch.Stop();
        var afterSnapshot = TakeSnapshot(forceGC: false);
        var afterGCSnapshot = TakeSnapshot(forceGC: true);

        return new MemoryMonitorResult(
            description,
            beforeSnapshot,
            afterSnapshot,
            afterGCSnapshot,
            stopwatch.Elapsed
        );
    }

    /// <summary>
    /// Tests if objects are properly disposed by using weak references.
    /// </summary>
    /// <param name="objectFactory">Factory function that creates objects to test</param>
    /// <param name="objectCount">Number of objects to create for testing</param>
    /// <param name="maxWaitTime">Maximum time to wait for objects to be collected</param>
    /// <returns>True if all objects were collected, false otherwise</returns>
    public static async Task<bool> TestObjectDisposal<T>(
        Func<T> objectFactory,
        int objectCount = 100,
        TimeSpan? maxWaitTime = null
    )
        where T : class
    {
        maxWaitTime ??= TimeSpan.FromSeconds(10);

        var weakReferences = new List<WeakReference>();

        // Create objects and weak references
        for (int i = 0; i < objectCount; i++)
        {
            var obj = objectFactory();
            weakReferences.Add(new WeakReference(obj));

            // If object is disposable, dispose it
            if (obj is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }

        // Force garbage collection
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var startTime = DateTime.UtcNow;

        // Wait for objects to be collected
        while (DateTime.UtcNow - startTime < maxWaitTime)
        {
            var aliveCount = weakReferences.Count(wr => wr.IsAlive);
            if (aliveCount == 0)
                return true;

            await Task.Delay(100);

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        // Check final count
        return weakReferences.All(wr => !wr.IsAlive);
    }
}

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
    /// Memory that remained after GC.
    /// </summary>
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
        return $"""
            Memory Monitor Results: {Description}
            Duration: {Duration.TotalMilliseconds:F2}ms
            Memory Allocated: {MemoryAllocated:N0} bytes
            Memory Retained: {MemoryRetained:N0} bytes
            Memory Freed: {MemoryFreed:N0} bytes
            GC Collections: Gen0={After.Gen0Collections - Before.Gen0Collections}, Gen1={After.Gen1Collections - Before.Gen1Collections}, Gen2={After.Gen2Collections - Before.Gen2Collections}
            """;
    }
}
