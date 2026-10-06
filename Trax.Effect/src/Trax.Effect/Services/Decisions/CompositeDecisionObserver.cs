using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Trax.Core.Decisions;

namespace Trax.Effect.Services.Decisions;

/// <summary>
/// One of the decision observers the composite tells, registered under its own key.
/// </summary>
/// <param name="Key">The key the observer is registered under.</param>
/// <param name="Tag">
/// Identifies a Trax registration, so registering it again adds nothing. Null for an observer the
/// host registered.
/// </param>
/// <param name="Skippable">
/// True when the observer is known, without building it, to be best effort, so one that cannot be
/// built is left out with a warning instead of failing every decision. Trax's own observers are
/// never skippable: decision recording is required, and junction events withhold what a track
/// would give away only when told of every routing. A host's observer is skippable only when it is
/// registered by type and that type does not implement <see cref="IDecisionObserver.Required"/>
/// itself, or as an instance that is not required.
/// </param>
internal sealed record DecisionObserverPart(object Key, Type? Tag, bool Skippable = false);

/// <summary>
/// The <see cref="IDecisionObserver"/> Trax.Core finds in the container when Trax registers one of
/// its own: it tells every registered observer, so decision recording, junction events and a
/// host's own observer each hear every decision, rather than the one registered first or last
/// silently replacing the others.
/// </summary>
/// <remarks>
/// <para>The required observers are told first, in the order they were registered, and the first
/// failure among them propagates, which fails the step as it would for that observer alone: a
/// decision that could not be recorded is not acted on, and is not reported as made either. Then the
/// best-effort observers are told, each failure logged and swallowed.</para>
///
/// <para>An observer registered before Trax's own is folded in when Trax's is added. One registered
/// after it, as <see cref="IDecisionObserver"/>, is what the container returns instead, so
/// <see cref="DecisionObserverCheck"/> refuses the host's start and every run on it: decision
/// recording would not record, and junction events would not withhold what a track gives away.
/// Register it before <c>AddTrax</c> to have both.</para>
///
/// <para>Each observer is built on its own. A host's observer known to be best effort that cannot
/// be built is left out with a warning; any other that cannot be built fails the composite, and so
/// every decision step and, through the check, the host's start.</para>
/// </remarks>
internal sealed class CompositeDecisionObserver : IDecisionObserver
{
    private readonly IReadOnlyList<IDecisionObserver> _required;
    private readonly IReadOnlyList<IDecisionObserver> _bestEffort;
    private readonly ILogger<CompositeDecisionObserver>? _logger;

    public CompositeDecisionObserver(
        IServiceProvider services,
        IEnumerable<DecisionObserverPart> parts,
        ILogger<CompositeDecisionObserver>? logger = null
    )
    {
        _logger = logger;
        var observers = new List<IDecisionObserver>();

        // Built one by one, so a best-effort observer that cannot be built costs only itself. Any
        // other propagates: whether it was required cannot be told from an observer never built.
        foreach (var part in parts)
        {
            try
            {
                observers.Add(services.GetRequiredKeyedService<IDecisionObserver>(part.Key));
            }
            catch (Exception e) when (part.Skippable)
            {
                _logger?.LogWarning(
                    e,
                    "A best-effort decision observer could not be built; it is left out, and the "
                        + "other observers are told about every decision."
                );
            }
        }

        _required = observers.Where(o => o.Required).ToList();
        _bestEffort = observers.Where(o => !o.Required).ToList();
    }

    /// <summary>
    /// True when any part is required. Only a required part's failure leaves the composite, so a
    /// failure that reaches Trax.Core always fails the step.
    /// </summary>
    public bool Required => _required.Count > 0;

    /// <inheritdoc />
    public Task Decided(DecisionMade decision, CancellationToken cancellationToken) =>
        TellAll((o, ct) => o.Decided(decision, ct), cancellationToken);

    /// <inheritdoc />
    public Task Routed(TrackRouted routing, CancellationToken cancellationToken) =>
        TellAll((o, ct) => o.Routed(routing, ct), cancellationToken);

    /// <inheritdoc />
    public Task Refused(DecisionRefused refusal, CancellationToken cancellationToken) =>
        TellAll((o, ct) => o.Refused(refusal, ct), cancellationToken);

    private async Task TellAll(
        Func<IDecisionObserver, CancellationToken, Task> tell,
        CancellationToken cancellationToken
    )
    {
        foreach (var observer in _required)
            await tell(observer, cancellationToken).ConfigureAwait(false);

        foreach (var observer in _bestEffort)
        {
            try
            {
                await tell(observer, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                _logger?.LogWarning(
                    e,
                    "Decision observer ({Observer}) threw; it is best effort, so the decision stands.",
                    observer.GetType().Name
                );
            }
        }
    }
}

/// <summary>
/// Registers decision observers so that every one of them is told, through
/// <see cref="CompositeDecisionObserver"/>.
/// </summary>
internal static class DecisionObservers
{
    /// <summary>
    /// Adds an observer made by <paramref name="factory"/> beside every other one, once per
    /// <paramref name="tag"/>. Any <see cref="IDecisionObserver"/> registered directly before this
    /// call is folded into the composite with its own lifetime.
    /// </summary>
    public static void Add(
        IServiceCollection services,
        Type tag,
        Func<IServiceProvider, IDecisionObserver> factory,
        ServiceLifetime lifetime
    )
    {
        FoldExisting(services);

        if (
            services.Any(d =>
                d.ImplementationInstance is DecisionObserverPart { } p && p.Tag == tag
            )
        )
            return;

        var key = new object();
        services.Add(
            new ServiceDescriptor(typeof(IDecisionObserver), key, (sp, _) => factory(sp), lifetime)
        );
        services.AddSingleton(new DecisionObserverPart(key, tag));
    }

    private static void FoldExisting(IServiceCollection services)
    {
        foreach (
            var descriptor in services
                .Where(d =>
                    d.ServiceType == typeof(IDecisionObserver)
                    && !d.IsKeyedService
                    && d.ImplementationType != typeof(CompositeDecisionObserver)
                )
                .ToList()
        )
        {
            var key = new object();
            services.Remove(descriptor);
            services.Add(Keyed(descriptor, key));
            services.AddSingleton(new DecisionObserverPart(key, null, KnownBestEffort(descriptor)));
        }

        if (
            !services.Any(d =>
                d.ServiceType == typeof(IDecisionObserver)
                && !d.IsKeyedService
                && d.ImplementationType == typeof(CompositeDecisionObserver)
            )
        )
        {
            services.AddTransient<IDecisionObserver, CompositeDecisionObserver>();
            services.AddSingleton<DecisionObserverCheck>();
            services.AddHostedService(sp => sp.GetRequiredService<DecisionObserverCheck>());
        }
    }

    /// <summary>
    /// Whether the observer is best effort as registered, without building it: an instance that is
    /// not required, or a type that leaves <see cref="IDecisionObserver.Required"/> to its default.
    /// A factory's observer cannot be told without calling it, so it is not.
    /// </summary>
    private static bool KnownBestEffort(ServiceDescriptor descriptor)
    {
        try
        {
            return descriptor switch
            {
                { ImplementationInstance: IDecisionObserver instance } => !instance.Required,
                { ImplementationType: { } type } => !ImplementsRequired(type),
                _ => false,
            };
        }
        catch
        {
            return false;
        }
    }

    private static bool ImplementsRequired(Type type)
    {
        var getter = typeof(IDecisionObserver)
            .GetProperty(nameof(IDecisionObserver.Required))!
            .GetMethod!;
        var map = type.GetInterfaceMap(typeof(IDecisionObserver));
        var index = Array.IndexOf(map.InterfaceMethods, getter);
        return index < 0 || map.TargetMethods[index].DeclaringType != typeof(IDecisionObserver);
    }

    private static ServiceDescriptor Keyed(ServiceDescriptor descriptor, object key) =>
        descriptor switch
        {
            { ImplementationInstance: { } instance } => new ServiceDescriptor(
                typeof(IDecisionObserver),
                key,
                instance
            ),
            { ImplementationFactory: { } factory } => new ServiceDescriptor(
                typeof(IDecisionObserver),
                key,
                (sp, _) => factory(sp),
                descriptor.Lifetime
            ),
            _ => new ServiceDescriptor(
                typeof(IDecisionObserver),
                key,
                descriptor.ImplementationType!,
                descriptor.Lifetime
            ),
        };
}
