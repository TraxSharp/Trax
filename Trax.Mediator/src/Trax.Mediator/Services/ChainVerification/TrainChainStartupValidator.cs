using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Trax.Core.Exceptions;
using Trax.Core.Monad;
using Trax.Mediator.Configuration;
using Trax.Mediator.Services.TrainDiscovery;

namespace Trax.Mediator.Services.ChainVerification;

/// <summary>
/// Reads every registered train's chain at startup and refuses to start the host if one of them
/// cannot run.
/// </summary>
/// <remarks>
/// A chain is a declaration of junction types, so two things are decidable before any traffic
/// arrives: that the chain can be read at all, and that every junction's input reaches Memory
/// before the junction needs it. Left to runtime, each of those surfaces only on the path that
/// happens to hit it, which for a rarely-taken train can be a long way from deployment.
///
/// <para>Whether a junction can be built is deliberately not checked. A junction takes its
/// constructor arguments from Memory, which the chain fills as it runs, so answering that at
/// startup would mean replaying the resolution rather than the types. An approximation of it
/// rejected every train in the sample applications, all of which run.</para>
///
/// <para>Every train is checked before anything is reported, so one start tells you about all of
/// them rather than one per attempt. Opt out with
/// <c>AddMediator(m => m.SkipChainVerification())</c> for the blind spot named in
/// <c>ChainVerification</c> (a junction asking for an interface that only a subtype of the
/// train's declared input implements), or temporarily while a codebase whose chains do not pass
/// yet is moved onto <c>Junctions()</c>.</para>
/// </remarks>
internal sealed class TrainChainStartupValidator(
    ITrainDiscoveryService discoveryService,
    IServiceScopeFactory scopeFactory,
    MediatorConfiguration configuration,
    ILogger<TrainChainStartupValidator>? logger = null
) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (configuration.SkipChainVerification)
        {
            logger?.LogWarning(
                "Chain verification is off, so a train whose chain cannot run will not be found "
                    + "until something runs it."
            );

            return;
        }

        // An async scope, because a train's scoped dependency may implement only
        // IAsyncDisposable, which a synchronous Dispose refuses outright. Request scopes are
        // disposed asynchronously, so such a train runs fine — disposing this one synchronously
        // would refuse the host over a disposal problem, reported as a chain problem, with
        // SkipChainVerification() the only way past it.
        await using var scope = scopeFactory.CreateAsyncScope();
        var problems = new List<string>();
        var checkedTrains = 0;

        foreach (var registration in discoveryService.DiscoverTrains())
        {
            cancellationToken.ThrowIfCancellationRequested();

            var problem = Check(scope.ServiceProvider, registration, out var skipped);

            if (skipped is not null)
            {
                logger?.LogWarning(
                    "The chain of {TrainName} was not verified at startup: {Reason}",
                    registration.ServiceTypeName,
                    skipped
                );
                continue;
            }

            checkedTrains++;

            if (problem is not null)
                problems.Add(problem);
        }

        if (problems.Count > 0)
            throw new TrainException(
                $"{problems.Count} of {checkedTrains} registered trains declare a chain that "
                    + "cannot run:"
                    + Environment.NewLine
                    + string.Join(Environment.NewLine, problems.Select(p => "  - " + p))
            );

        logger?.LogDebug("Verified the chains of {TrainCount} trains.", checkedTrains);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    /// Returns what is wrong with a train's chain, or null. <paramref name="skipped"/> says why
    /// a train's chain could not be read at all, which is reported as a warning rather than a
    /// refusal: the check exists to find chains that cannot run, and an unreadable train is not
    /// evidence of one.
    /// </summary>
    private static string? Check(
        IServiceProvider services,
        TrainRegistration registration,
        out string? skipped
    )
    {
        skipped = null;
        object train;

        try
        {
            train = services.GetRequiredService(registration.ServiceType);
        }
        catch (Exception ex)
        {
            // A train whose constructor needs something only a request provides, a current user
            // read from HttpContext say, cannot be built at boot and still runs fine. Refusing
            // to start over it would make the upgrade that adds this check break such hosts.
            skipped = $"it could not be constructed outside a request ({ex.Message})";
            return null;
        }

        // DeclaredChain is public on Train<,>, but the registration hands back the service
        // interface, so the concrete method is reached by name. A registered train need not
        // derive from Train<,>; one that does not has no chain to read.
        var declaredChain = train
            .GetType()
            .GetMethod(nameof(Core.Train.Train<,>.DeclaredChain), Type.EmptyTypes);

        if (declaredChain is null)
        {
            skipped = $"{train.GetType().Name} does not derive from Train<TIn, TOut>";
            return null;
        }

        ChainRecorder chain;

        try
        {
            chain = (ChainRecorder)declaredChain.Invoke(train, null)!;
        }
        catch (Exception ex) when (ex.InnerException is ChainDeclarationException declaration)
        {
            return $"{registration.ServiceTypeName}: {declaration.Message}";
        }
        catch (Exception ex)
        {
            // Invoke wraps whatever Junctions() threw. The wrapper's message says only that an
            // invocation target threw, which leaves the operator nothing to act on, so report
            // the inner exception's message as the reason.
            var cause = ex.InnerException ?? ex;

            return $"{registration.ServiceTypeName}: its chain could not be read ({cause.Message})";
        }

        // Asks whether the container can supply a type without building one. Resolving each
        // candidate would construct services at boot, and a factory that only works inside a
        // request (one reading HttpContext, say) would crash startup instead of answering.
        var isService = services.GetService<IServiceProviderIsService>();

        IReadOnlyList<ChainFault> faults;

        try
        {
            faults = Core.Monad.ChainVerification.Verify(
                chain,
                registration.InputType,
                registration.OutputType,
                type => isService?.IsService(type) ?? services.GetService(type) is not null
            );
        }
        catch (Exception ex)
        {
            return $"{registration.ServiceTypeName}: its chain could not be verified ({ex.Message})";
        }

        return faults.Count == 0
            ? null
            : $"{registration.ServiceTypeName}: "
                + string.Join("; ", faults.Select(f => $"step {f.StepIndex} {f.Reason}"));
    }
}
