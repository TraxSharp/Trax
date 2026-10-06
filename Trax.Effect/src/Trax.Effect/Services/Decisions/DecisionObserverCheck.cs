using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Trax.Core.Decisions;

namespace Trax.Effect.Services.Decisions;

/// <summary>
/// Refuses a host whose container hands Trax.Core an <see cref="IDecisionObserver"/> other than the
/// composite Trax registers, or cannot build the composite. The composite exists only while one of
/// Trax's own observers is a part (decision recording, junction events), and each of those must be
/// told about every decision: recording so a decision is not acted on unrecorded, junction events
/// so the steps on a track whose answer is withheld are withheld too.
/// </summary>
/// <remarks>
/// <para>An observer registered after <c>AddTrax</c> replaces the composite, because the container
/// returns the last registration. Decorating <see cref="IDecisionObserver"/> (Scrutor's
/// <c>Decorate</c>, say) replaces it the same way. Both are refused; register the observer to add
/// before <c>AddTrax</c> instead, and Trax tells it alongside its own.</para>
///
/// <para>It checks once, from a scope of its own. The host's start refuses (in
/// <see cref="StartingAsync"/>, before any other hosted service starts, or in
/// <see cref="StartAsync"/> for a harness that skips the lifecycle steps), and so does every run on
/// the host (<see cref="ThrowIfReplaced"/>), so a host built without the generic host fails closed
/// too. A composite that cannot be built, because an observer it must tell cannot be, refuses the
/// same way, with what building it threw; a host observer known to be best effort that cannot be
/// built is left out by the composite and refuses nothing.</para>
/// </remarks>
internal sealed class DecisionObserverCheck(IServiceProvider services) : IHostedLifecycleService
{
    private readonly Lazy<string?> _problem = new(() => Find(services));
    private bool _checked;

    /// <summary>Throws when Trax's decision observers would not be told about decisions.</summary>
    /// <exception cref="InvalidOperationException">
    /// An observer replaced the composite, or the composite cannot be built.
    /// </exception>
    public void ThrowIfReplaced()
    {
        if (_problem.Value is { } problem)
            throw new InvalidOperationException(problem);
    }

    private static string? Find(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var provider = scope.ServiceProvider;

        var parts = provider
            .GetServices<DecisionObserverPart>()
            .Where(part => part.Tag is not null)
            .Select(part => part.Tag!.Name)
            .Distinct()
            .ToList();

        IDecisionObserver? resolved;

        // What resolving throws is turned into the refusal, so the reason is the same on every run
        // and the check never caches a raw exception.
        try
        {
            resolved = provider.GetService<IDecisionObserver>();
        }
        catch (Exception e)
        {
            return "The decision observers Trax composes could not be built ("
                + e.GetType().Name
                + ": "
                + e.Message
                + "), so a decision could not be recorded or reported before the run acts on it. "
                + "Fix the registration of the observer named there.";
        }

        if (resolved is CompositeDecisionObserver or null || parts.Count == 0)
            return null;

        return "An IDecisionObserver ("
            + resolved.GetType().FullName
            + ") was registered after AddTrax(...), so the container hands it to every train in "
            + "place of the observers Trax composes, and "
            + string.Join(", ", parts)
            + " would never be told about a decision the run acts on. Register your observer "
            + "before AddTrax(...); Trax then tells it alongside its own.";
    }

    public Task StartingAsync(CancellationToken cancellationToken)
    {
        _checked = true;
        ThrowIfReplaced();
        return Task.CompletedTask;
    }

    public Task StartAsync(CancellationToken cancellationToken) =>
        _checked ? Task.CompletedTask : StartingAsync(cancellationToken);

    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
