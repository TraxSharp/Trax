namespace Trax.Scheduler.Configuration;

/// <summary>
/// Internal registry that maps train names to specific job submitter types.
/// Used by <see cref="Trax.Scheduler.Trains.JobDispatcher.Steps.DispatchJobsStep"/> to route
/// jobs to the correct submitter based on the train being dispatched.
/// </summary>
internal class JobSubmitterRoutingConfiguration
{
    private readonly Dictionary<string, Type> _routes = new();
    private Type? _attributeDefaultSubmitterType;
    private readonly HashSet<string> _attributeRemoteTrains = new();

    /// <summary>
    /// Adds a builder-level route mapping a train to a specific submitter type.
    /// </summary>
    internal void AddRoute(string trainFullName, Type submitterType) =>
        _routes[trainFullName] = submitterType;

    /// <summary>
    /// Sets the submitter type used for trains marked with [TraxRemote].
    /// </summary>
    internal void SetAttributeDefaultSubmitter(Type submitterType) =>
        _attributeDefaultSubmitterType = submitterType;

    /// <summary>
    /// Registers a train discovered with the [TraxRemote] attribute.
    /// </summary>
    internal void AddAttributeRemoteTrain(string trainFullName) =>
        _attributeRemoteTrains.Add(trainFullName);

    /// <summary>
    /// Resolves the submitter type for a given train name.
    /// Returns null if the train should use the default local submitter.
    /// </summary>
    /// <remarks>
    /// Precedence:
    /// 1. Builder <c>ForTrain&lt;T&gt;()</c> routing (highest priority)
    /// 2. <c>[TraxRemote]</c> attribute (if a remote submitter is configured)
    /// 3. null (use default local <c>IJobSubmitter</c>)
    /// </remarks>
    internal Type? GetSubmitterType(string trainName)
    {
        // Builder routing takes precedence
        if (_routes.TryGetValue(trainName, out var type))
            return type;

        // Fall back to [TraxRemote] attribute
        if (
            _attributeRemoteTrains.Contains(trainName) && _attributeDefaultSubmitterType is not null
        )
            return _attributeDefaultSubmitterType;

        return null;
    }

    /// <summary>
    /// Returns true if any routes have been configured (builder or attribute).
    /// </summary>
    internal bool HasRoutes =>
        _routes.Count > 0
        || (_attributeRemoteTrains.Count > 0 && _attributeDefaultSubmitterType is not null);
}
