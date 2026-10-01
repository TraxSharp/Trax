using Microsoft.Extensions.DependencyInjection;
using Trax.Scheduler.Services.JobSubmitter;

namespace Trax.Scheduler.Configuration;

/// <summary>
/// Internal registry that maps train names to the routed submitter registration that runs them.
/// Used by <see cref="Trax.Scheduler.Trains.JobDispatcher.Junctions.DispatchJobsJunction"/> to route
/// jobs to the correct submitter based on the train being dispatched.
/// </summary>
/// <remarks>
/// A train is routed to a registration, not to a submitter type. Two <c>UseRemoteWorkers</c>
/// calls both submit through <see cref="HttpJobSubmitter"/>, each to its own endpoint with its own
/// options, so the type alone cannot say which one a train belongs to.
/// </remarks>
internal class JobSubmitterRoutingConfiguration
{
    private readonly Dictionary<string, RoutedSubmitterRegistration> _routes = new();
    private RoutedSubmitterRegistration? _attributeDefault;
    private readonly HashSet<string> _attributeRemoteTrains = new();

    /// <summary>
    /// Adds a builder-level route mapping a train to a routed submitter registration.
    /// </summary>
    internal void AddRoute(string trainFullName, RoutedSubmitterRegistration registration) =>
        _routes[trainFullName] = registration;

    /// <summary>
    /// Adds a builder-level route mapping a train to a submitter resolved by its type.
    /// </summary>
    internal void AddRoute(string trainFullName, Type submitterType) =>
        AddRoute(trainFullName, ByType(submitterType));

    /// <summary>
    /// Sets the registration used for trains marked with [TraxRemote].
    /// </summary>
    internal void SetAttributeDefaultSubmitter(RoutedSubmitterRegistration registration) =>
        _attributeDefault = registration;

    /// <summary>
    /// Sets the submitter type used for trains marked with [TraxRemote].
    /// </summary>
    internal void SetAttributeDefaultSubmitter(Type submitterType) =>
        SetAttributeDefaultSubmitter(ByType(submitterType));

    /// <summary>
    /// Registers a train discovered with the [TraxRemote] attribute.
    /// </summary>
    internal void AddAttributeRemoteTrain(string trainFullName) =>
        _attributeRemoteTrains.Add(trainFullName);

    /// <summary>
    /// Resolves the registration for a given train name.
    /// Returns null if the train should use the default local submitter.
    /// </summary>
    /// <remarks>
    /// Precedence:
    /// 1. Builder <c>ForTrain&lt;T&gt;()</c> routing (highest priority)
    /// 2. <c>[TraxRemote]</c> attribute, to the first routed submitter (a scheduler with a
    ///    <c>[TraxRemote]</c> train and no routed submitter refuses to build)
    /// 3. null (use default local <c>IJobSubmitter</c>)
    /// </remarks>
    internal RoutedSubmitterRegistration? GetRegistration(string trainName)
    {
        // Builder routing takes precedence
        if (_routes.TryGetValue(trainName, out var registration))
            return registration;

        // Fall back to [TraxRemote] attribute
        if (_attributeRemoteTrains.Contains(trainName) && _attributeDefault is not null)
            return _attributeDefault;

        return null;
    }

    /// <summary>
    /// Resolves the submitter type for a given train name.
    /// Returns null if the train should use the default local submitter.
    /// </summary>
    internal Type? GetSubmitterType(string trainName) => GetRegistration(trainName)?.SubmitterType;

    /// <summary>
    /// The submitter that runs <paramref name="trainName"/>: its routed registration's own
    /// submitter, or null when the train uses the default <see cref="IJobSubmitter"/>.
    /// </summary>
    internal IJobSubmitter? ResolveSubmitter(IServiceProvider services, string trainName)
    {
        if (GetRegistration(trainName) is not { } registration)
            return null;

        return registration.CreateSubmitter?.Invoke(services)
            ?? (IJobSubmitter)services.GetRequiredService(registration.SubmitterType);
    }

    /// <summary>
    /// Returns true if any routes have been configured (builder or attribute).
    /// </summary>
    internal bool HasRoutes =>
        _routes.Count > 0 || (_attributeRemoteTrains.Count > 0 && _attributeDefault is not null);

    private static RoutedSubmitterRegistration ByType(Type submitterType) =>
        new(new SubmitterRouting(), submitterType, _ => { });
}
