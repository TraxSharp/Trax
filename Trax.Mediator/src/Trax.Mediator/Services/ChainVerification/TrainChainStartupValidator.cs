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
/// A chain is a declaration of junction types, so several things are decidable before any traffic
/// arrives: that the chain can be read at all, that every junction's input reaches Memory before
/// the junction needs it, and that every junction it names is one Trax can build (exactly one
/// public constructor, not abstract, not an interface; Trax.Core records those as refusals when
/// the chain is read). Left to runtime, each of those surfaces only on the path that happens to
/// hit it, which for a rarely-taken train can be a long way from deployment.
///
/// <para>Whether a junction's constructor arguments will be found is not checked. A junction takes
/// them from Memory, which the chain fills as it runs, or from the container, and
/// <c>ChainVerification.Verify</c> has no option to replay that yet. A junction whose argument is
/// missing fails on its first run, with a message naming the junction and the type.</para>
///
/// <para>A train that cannot be built at startup is refused when its constructor needs a type the
/// container does not register at all, since that train fails on every run. When everything its
/// constructor names is registered, the failure is taken to be a dependency only a request can
/// supply, and the train is skipped with a warning.</para>
///
/// <para>Every train is checked before anything is reported, so one start tells you about all of
/// them rather than one per attempt. Opt out with
/// <c>AddMediator(m => m.SkipChainVerification())</c> for the blind spot named in
/// <c>ChainVerification</c> (a junction asking for an interface that only a subtype of the
/// train's declared input implements), or temporarily while a codebase whose chains do not pass
/// yet is moved onto <c>Junctions()</c>.</para>
///
/// <para>The check runs in <see cref="StartingAsync"/>, which the host finishes for every hosted
/// service before it calls any <c>StartAsync</c>. A refusal therefore stops the host before a
/// worker starts claiming work, even under <c>HostOptions.ServicesStartConcurrently</c>, where
/// every <c>StartAsync</c> begins at once and registration order decides nothing.</para>
/// </remarks>
internal sealed class TrainChainStartupValidator(
    ITrainDiscoveryService discoveryService,
    IServiceScopeFactory scopeFactory,
    MediatorConfiguration configuration,
    ILogger<TrainChainStartupValidator>? logger = null
) : IHostedLifecycleService
{
    public async Task StartingAsync(CancellationToken cancellationToken)
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
        var failedTrains = 0;
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
            {
                failedTrains++;
                problems.AddRange(problem);
            }
        }

        if (failedTrains > 0)
            throw new TrainException(
                $"{failedTrains} of {checkedTrains} registered trains cannot run:"
                    + Environment.NewLine
                    + string.Join(Environment.NewLine, problems.Select(p => "  - " + p))
            );

        logger?.LogDebug("Verified the chains of {TrainCount} trains.", checkedTrains);
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    /// Returns what is wrong with a train, one fault per entry, each naming the train, or null.
    /// <paramref name="skipped"/> says why a train's chain could not be read at all, which is
    /// reported as a warning rather than a refusal: the check exists to find trains that cannot
    /// run, and an unreadable train is not evidence of one.
    /// </summary>
    private static IReadOnlyList<string>? Check(
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
            // A constructor argument the container does not register at all fails every run of
            // the train, so it is refused here, naming the type, rather than surfacing as the raw
            // DI error on the first run.
            var unregistered = UnregisteredConstructorArguments(
                services,
                registration.ImplementationType
            );

            if (unregistered.Count > 0)
                return
                [
                    $"{registration.ServiceTypeName} cannot be built: its constructor needs "
                        + string.Join(", ", unregistered.Select(t => $"'{Readable(t)}'"))
                        + (
                            unregistered.Count == 1
                                ? ", which is not registered. Register it"
                                : ", which are not registered. Register them"
                        )
                        + " before building the host.",
                ];

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
            return [$"{registration.ServiceTypeName}: {declaration.Message}"];
        }
        catch (Exception ex)
        {
            // Invoke wraps whatever Junctions() threw. The wrapper's message says only that an
            // invocation target threw, which leaves the operator nothing to act on, so report
            // the inner exception's message as the reason.
            var cause = ex.InnerException ?? ex;

            return
            [
                $"{registration.ServiceTypeName}: its chain could not be read ({cause.Message})",
            ];
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
            return
            [
                $"{registration.ServiceTypeName}: its chain could not be verified ({ex.Message})",
            ];
        }

        return faults.Count == 0
            ? null
            : Describe(chain, faults).Select(f => $"{registration.ServiceTypeName}: {f}").ToList();
    }

    /// <summary>
    /// Orders a chain's faults for the reader: refusals first, then each step's fault numbered
    /// from one and named by its junction.
    /// </summary>
    /// <remarks>
    /// A refusal is something the declaration did that no step expresses, such as naming a type
    /// that is not a junction, and it is usually the cause of what follows: a refused step records
    /// nothing, so the chain can then end without its result. That Resolve fault is a consequence,
    /// and listing it first sent the reader after the wrong line. While a chain has refusals its
    /// Resolve faults are left out; a real one shows on the next start, once the refusal is fixed.
    /// <c>Verify</c> reports a refusal as a Resolve fault past the last step carrying the
    /// refusal's text, which is how the two are told apart here.
    /// </remarks>
    private static IEnumerable<string> Describe(
        ChainRecorder chain,
        IReadOnlyList<ChainFault> faults
    )
    {
        var refusalReasons = new System.Collections.Generic.HashSet<string>(chain.Refusals);

        bool IsRefusal(ChainFault fault) =>
            fault.Kind == ChainStepKind.Resolve
            && fault.Junction is null
            && fault.StepIndex >= chain.Steps.Count
            && refusalReasons.Contains(fault.Reason);

        var refusals = faults.Where(IsRefusal).Select(f => f.Reason).ToList();

        var steps = faults
            .Where(f => !IsRefusal(f))
            .Where(f => refusals.Count == 0 || f.Kind != ChainStepKind.Resolve)
            .Select(f =>
                // The one fault Verify reports about the train's input rather than a step. A
                // recorded Seed step never faults, so every Seed fault is this one.
                f.Kind == ChainStepKind.Seed
                    ? f.Reason
                    : $"step {f.StepIndex + 1} ({StepName(f)}) {f.Reason}"
            );

        return refusals.Concat(steps);
    }

    private static string StepName(ChainFault fault) =>
        fault.Junction is { } junction ? Readable(junction) : fault.Kind.ToString();

    /// <summary>
    /// The constructor arguments of <paramref name="implementation"/> the container does not
    /// register, or none when any public constructor can be satisfied or the container cannot
    /// say.
    /// </summary>
    /// <remarks>
    /// Asked of <see cref="IServiceProviderIsService"/>, which answers without building anything.
    /// A type it reports registered but that still failed to build needs something only a request
    /// provides, which the caller reports as a skip. An argument with a default value, or a keyed
    /// one, is left out: the first need not be registered and the second is not answered by this
    /// question. With several constructors, the longest is reported, since that is the one the
    /// container prefers.
    /// </remarks>
    private static IReadOnlyList<Type> UnregisteredConstructorArguments(
        IServiceProvider services,
        Type implementation
    )
    {
        // IsAbstract is true of an interface too, which is what a train registered only through
        // a factory is listed with.
        if (
            implementation.IsAbstract
            || services.GetService<IServiceProviderIsService>() is not { } isService
        )
            return [];

        try
        {
            IReadOnlyList<Type>? longest = null;

            foreach (
                var constructor in implementation
                    .GetConstructors()
                    .OrderByDescending(c => c.GetParameters().Length)
            )
            {
                var missing = constructor
                    .GetParameters()
                    .Where(p =>
                        !p.HasDefaultValue
                        && !p.IsDefined(typeof(FromKeyedServicesAttribute), inherit: true)
                        && !p.IsDefined(typeof(ServiceKeyAttribute), inherit: true)
                        && !isService.IsService(p.ParameterType)
                    )
                    .Select(p => p.ParameterType)
                    .Distinct()
                    .ToList();

                if (missing.Count == 0)
                    return [];

                longest ??= missing;
            }

            return longest ?? [];
        }
        catch (Exception)
        {
            // A container that cannot answer leaves the train skipped with a warning, as before.
            return [];
        }
    }

    /// <summary>A type's short name, generics written as <c>Name&lt;Arg&gt;</c>.</summary>
    private static string Readable(Type type)
    {
        if (!type.IsGenericType)
            return type.Name;

        var name = type.Name;
        var tick = name.IndexOf('`');

        return (tick < 0 ? name : name[..tick])
            + "<"
            + string.Join(", ", type.GetGenericArguments().Select(Readable))
            + ">";
    }
}
