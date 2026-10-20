namespace Trax.Effect.StateMachine.Tests.Stress.Fixtures;

/// <summary>
/// Base for the stress fixtures: skips at runtime (not via the <c>[Ignore]</c> attribute, so the suite is
/// runnable on demand) unless the suite is enabled, and provides a bounded-concurrency fan-out helper.
/// </summary>
[Category("Stress")]
public abstract class StressFixture
{
    [OneTimeSetUp]
    public void GateOnStressFlag()
    {
        if (!StressProfile.Enabled)
            Assert.Ignore(StressProfile.SkipReason);
    }

    /// <summary>
    /// Runs <paramref name="body"/> for each index 0..<paramref name="count"/>, at most
    /// <paramref name="maxConcurrency"/> in flight at once, and returns every result. Bounding the fan-out
    /// keeps the real connection count under Postgres's limit while still driving heavy contention.
    /// </summary>
    protected static async Task<T[]> Fan<T>(int count, int maxConcurrency, Func<int, Task<T>> body)
    {
        using var gate = new SemaphoreSlim(maxConcurrency);
        var tasks = Enumerable
            .Range(0, count)
            .Select(async i =>
            {
                await gate.WaitAsync();
                try
                {
                    return await body(i);
                }
                finally
                {
                    gate.Release();
                }
            });
        return await Task.WhenAll(tasks);
    }
}
