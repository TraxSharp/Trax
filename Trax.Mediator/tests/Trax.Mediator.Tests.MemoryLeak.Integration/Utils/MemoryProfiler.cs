using System.Diagnostics;

namespace Trax.Mediator.Tests.MemoryLeak.Integration.Utils;

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
    /// The smallest <see cref="MemoryMonitorResult.MemoryRetained"/> bound a test may assert.
    /// </summary>
    /// <remarks>
    /// <c>MemoryRetained</c> is a delta of <see cref="GC.GetTotalMemory(bool)"/>, which reports the
    /// whole process's managed heap and is documented as approximate. Everything the process retains
    /// between the two snapshots lands in it — a static initialised on first use, the structures
    /// supporting newly jitted code, the test runner's own accumulating results — none of which the
    /// code under test allocated. A bound below this floor therefore measures the runner rather than
    /// the subject, which is why such a bound passes on a developer's machine and fails on a loaded
    /// CI one.
    ///
    /// <para>To assert that particular objects were freed, do not tighten the bound: hold a
    /// <see cref="WeakReference"/> to each and use <see cref="CountAliveAsync"/>. Reachability after
    /// a full collection is a fact about the object graph, not a measurement of the process.</para>
    /// </remarks>
    public const long RetainedNoiseFloorBytes = 1024 * 1024;

    /// <summary>
    /// Forces collection until every reference is unreachable, and returns how many are still alive.
    /// </summary>
    /// <remarks>
    /// A full collection is deterministic about unreachable objects, but a finalizer that resurrects
    /// nothing still has to run before the object's memory is reclaimed, and that happens on another
    /// thread. So this retries until the deadline instead of collecting once and reading the count.
    /// </remarks>
    public static async Task<int> CountAliveAsync(
        IReadOnlyCollection<WeakReference> references,
        TimeSpan? maxWait = null
    )
    {
        var deadline = DateTime.UtcNow + (maxWait ?? TimeSpan.FromSeconds(5));

        while (true)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            var alive = references.Count(reference => reference.IsAlive);

            if (alive == 0 || DateTime.UtcNow >= deadline)
                return alive;

            // allowed-delay: waiting for the finalizer thread, not for a signal we could await.
            await Task.Delay(50);
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
        return $"""
            Memory Monitor Results: {Description}
            Duration: {Duration.TotalMilliseconds:F2}ms
            Memory Allocated: {MemoryAllocated:N0} bytes
            Memory Retained: {MemoryRetained:N0} bytes
            Memory Freed: {MemoryFreed:N0} bytes
            GC Collections: Gen0={After.Gen0Collections
                - Before.Gen0Collections}, Gen1={After.Gen1Collections
                - Before.Gen1Collections}, Gen2={After.Gen2Collections - Before.Gen2Collections}
            """;
    }
}
