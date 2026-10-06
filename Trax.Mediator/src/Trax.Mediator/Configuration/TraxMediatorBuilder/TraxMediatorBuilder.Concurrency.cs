namespace Trax.Mediator.Configuration;

public partial class TraxMediatorBuilder
{
    /// <summary>
    /// Sets a global maximum for concurrent RUN executions across all trains.
    /// When this limit is reached, additional RUN requests wait until a slot opens.
    /// </summary>
    /// <param name="maxConcurrent">The maximum number of concurrent RUN executions (must be >= 1).</param>
    public TraxMediatorBuilder GlobalConcurrentRunLimit(int maxConcurrent)
    {
        if (maxConcurrent < 1)
            throw new ArgumentOutOfRangeException(
                nameof(maxConcurrent),
                "GlobalConcurrentRunLimit must be >= 1"
            );

        _globalMaxConcurrentRun = maxConcurrent;
        return this;
    }

    /// <summary>
    /// Sets a per-train concurrency limit for RUN executions.
    /// This overrides any <c>[TraxConcurrencyLimit]</c> attribute on the train class.
    /// </summary>
    /// <typeparam name="TTrain">The train interface type (e.g. <c>IResolveCombatTrain</c>).</typeparam>
    /// <param name="maxConcurrent">The maximum number of concurrent RUN executions for this train (must be >= 1).</param>
    public TraxMediatorBuilder ConcurrentRunLimit<TTrain>(int maxConcurrent)
        where TTrain : class
    {
        if (maxConcurrent < 1)
            throw new ArgumentOutOfRangeException(
                nameof(maxConcurrent),
                $"ConcurrentRunLimit<{typeof(TTrain).Name}> must be >= 1"
            );

        _concurrencyOverrides[typeof(TTrain).FullName!] = maxConcurrent;
        return this;
    }
}
