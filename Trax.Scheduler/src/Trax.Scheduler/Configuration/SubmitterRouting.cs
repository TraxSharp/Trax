namespace Trax.Scheduler.Configuration;

/// <summary>
/// Fluent API for specifying which trains should use a particular job submitter.
/// Used with <c>UseRemoteWorkers()</c> and <c>UseSqsWorkers()</c> to route specific trains
/// to a remote execution backend while other trains continue to run locally.
/// </summary>
/// <example>
/// <code>
/// .UseRemoteWorkers(
///     remote => remote.BaseUrl = "https://gpu-workers/trax/execute",
///     routing => routing
///         .ForTrain&lt;IHeavyComputeTrain&gt;()
///         .ForTrain&lt;IAiInferenceTrain&gt;())
/// </code>
/// </example>
public class SubmitterRouting
{
    internal HashSet<string> TrainNames { get; } = [];

    /// <summary>
    /// Routes a train type to this submitter.
    /// </summary>
    /// <typeparam name="TTrain">The train interface type (e.g., <c>IMyTrain</c>)</typeparam>
    /// <returns>This routing instance for method chaining</returns>
    public SubmitterRouting ForTrain<TTrain>()
        where TTrain : class
    {
        TrainNames.Add(typeof(TTrain).FullName!);
        return this;
    }
}
