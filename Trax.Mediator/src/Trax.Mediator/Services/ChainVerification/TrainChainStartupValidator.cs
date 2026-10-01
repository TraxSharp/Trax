using System.Reflection;
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
/// the junction needs it, that every junction it names is one Trax can build (exactly one public
/// constructor, not abstract, not an interface), and that each such junction's constructor
/// arguments will be found, in Memory as the chain has filled it by then or in the container. Left
/// to runtime, each of those surfaces only on the path that happens to hit it, which for a
/// rarely-taken train can be a long way from deployment.
///
/// <para>A train that cannot be built at startup is refused when it can never be built: its class
/// has no public constructor, or its constructor needs a type the container does not register at
/// all, directly or through a registered dependency whose own constructor needs one. When nothing
/// like that can be found, the failure is taken to be a dependency only a request can supply, and
/// the train is skipped with a warning.</para>
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
/// every <c>StartAsync</c> begins at once and registration order decides nothing. Something that
/// starts hosted services itself and calls only <c>StartAsync</c> gets the check from there
/// instead.</para>
/// </remarks>
internal sealed class TrainChainStartupValidator(
    ITrainDiscoveryService discoveryService,
    IServiceScopeFactory scopeFactory,
    MediatorConfiguration configuration,
    ILogger<TrainChainStartupValidator>? logger = null
) : IHostedLifecycleService
{
    private bool _checked;

    public async Task StartingAsync(CancellationToken cancellationToken)
    {
        _checked = true;

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

    public Task StartAsync(CancellationToken cancellationToken) =>
        _checked ? Task.CompletedTask : StartingAsync(cancellationToken);

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
            if (CannotEverBeBuilt(services, registration) is { } reason)
                return [$"{registration.ServiceTypeName} cannot be built: {reason}"];

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
            // With the container's answer, Verify also checks each junction's constructor
            // arguments. Without it, only the flow of types through Memory can be replayed.
            faults = isService is not null
                ? Core.Monad.ChainVerification.Verify(
                    chain,
                    registration.InputType,
                    registration.OutputType,
                    isService
                )
                : Core.Monad.ChainVerification.Verify(
                    chain,
                    registration.InputType,
                    registration.OutputType,
                    type => services.GetService(type) is not null
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
    /// Orders a chain's faults for the reader: refusals first, then each step's fault, every one
    /// numbered from one by its written position.
    /// </summary>
    /// <remarks>
    /// A refusal is something the declaration did that the chain cannot run with, such as naming a
    /// type that is not a junction, and it is often the cause of what follows: a step refused that
    /// way records no output, so the chain can then end without its result. That Resolve fault is
    /// a consequence, and listing it sent the reader after the wrong line, so a Resolve fault is
    /// left out when a refused step before it produced nothing. Any other Resolve fault is listed,
    /// refusals or not.
    /// </remarks>
    private static IEnumerable<string> Describe(
        ChainRecorder chain,
        IReadOnlyList<ChainFault> faults
    )
    {
        var steps = chain.Steps;

        bool IsStep(ChainFault fault) => fault.StepIndex < steps.Count;

        var firstEmptyRefusedStep = faults
            .Where(f => f.IsRefusal && IsStep(f) && steps[f.StepIndex].Out is null)
            .Select(f => (int?)f.StepIndex)
            .Min();

        bool IsCascade(ChainFault fault) =>
            !fault.IsRefusal
            && fault.Kind == ChainStepKind.Resolve
            && firstEmptyRefusedStep < fault.StepIndex;

        string Line(ChainFault fault)
        {
            // A refusal of the chain as a whole belongs to no step. Its reason names what to fix.
            if (!IsStep(fault))
                return fault.Reason;

            // A refusal's reason already names the step's junction.
            if (fault.IsRefusal)
                return $"step {fault.StepIndex + 1}: {fault.Reason}";

            // The one fault Verify reports about the train's input rather than a step. A recorded
            // Seed step never faults except by refusal, so every other Seed fault is this one.
            if (fault.Kind == ChainStepKind.Seed)
                return fault.Reason;

            return $"step {fault.StepIndex + 1} ({StepName(fault)}) {fault.Reason}";
        }

        return faults
            .Where(f => f.IsRefusal)
            .Concat(faults.Where(f => !f.IsRefusal && !IsCascade(f)))
            .Select(Line)
            .Distinct();
    }

    private static string StepName(ChainFault fault) =>
        fault.Junction is { } junction ? Readable(junction) : fault.Kind.ToString();

    /// <summary>
    /// Why the train <paramref name="registration"/> describes can never be built, or null when
    /// nothing proves it: its class has no public constructor, or its constructor needs a type the
    /// container does not register, directly or through the constructor of a registered
    /// dependency.
    /// </summary>
    /// <remarks>
    /// Asked of <see cref="IServiceProviderIsService"/>, which answers without building anything,
    /// and of the registered <see cref="IServiceCollection"/>, which says what class a dependency
    /// resolves to. A dependency registered through a factory or an instance cannot be followed,
    /// so a failure behind one is left to the caller's skip: it may need something only a request
    /// provides. An argument with a default value, or a keyed one, is left out: the first need not
    /// be registered and the second is not answered by this question. With several constructors,
    /// the one the container would use is followed, the longest whose arguments are all
    /// registered, and when none is, the longest is reported.
    /// </remarks>
    private static string? CannotEverBeBuilt(
        IServiceProvider services,
        TrainRegistration registration
    )
    {
        var implementation = registration.ImplementationType;

        // IsAbstract is true of an interface too, which is what a train registered only through
        // a factory is listed with.
        if (
            implementation.IsAbstract
            || services.GetService<IServiceProviderIsService>() is not { } isService
        )
            return null;

        try
        {
            var dependencies = new DependencyWalk(
                isService,
                services.GetService<IServiceCollection>()
            );

            return dependencies.WhyNot(implementation);
        }
        catch (Exception)
        {
            // A container that cannot answer leaves the train skipped with a warning, as before.
            return null;
        }
    }

    /// <summary>
    /// Follows a class's constructor through the container's registrations until it finds a type
    /// nothing registers.
    /// </summary>
    private sealed class DependencyWalk(
        IServiceProviderIsService isService,
        IServiceCollection? descriptors
    )
    {
        /// <summary>How deep the walk follows dependencies before it gives up and says nothing.</summary>
        private const int MaxDepth = 16;

        private readonly System.Collections.Generic.HashSet<Type> _visited = [];

        /// <summary>Why <paramref name="implementation"/> can never be built, or null.</summary>
        public string? WhyNot(Type implementation) => WhyNot(implementation, [], 0);

        private string? WhyNot(Type implementation, List<Type> through, int depth)
        {
            if (depth > MaxDepth || !_visited.Add(implementation))
                return null;

            var constructors = implementation.GetConstructors();

            if (constructors.Length == 0)
                return $"{Readable(implementation)} has no public constructor"
                    + Through(through)
                    + ". Give it one.";

            var ordered = constructors.OrderByDescending(c => c.GetParameters().Length).ToList();
            var chosen = ordered.FirstOrDefault(c => Unregistered(c).Count == 0);

            if (chosen is null)
            {
                var missing = Unregistered(ordered[0]);

                return (
                        through.Count == 0
                            ? "its constructor needs "
                            : $"{Readable(implementation)} needs "
                    )
                    + string.Join(", ", missing.Select(t => $"'{Readable(t)}'"))
                    + (
                        missing.Count == 1
                            ? ", which is not registered"
                            : ", which are not registered"
                    )
                    + Through(through)
                    + (missing.Count == 1 ? ". Register it" : ". Register them")
                    + " before building the host.";
            }

            foreach (var parameter in chosen.GetParameters().Where(p => !IsLeftOut(p)))
            {
                if (ImplementationOf(parameter.ParameterType) is not { } dependency)
                    continue;

                if (
                    WhyNot(dependency, [.. through, parameter.ParameterType], depth + 1) is
                    { } reason
                )
                    return reason;
            }

            return null;
        }

        private List<Type> Unregistered(ConstructorInfo constructor) =>
            constructor
                .GetParameters()
                .Where(p => !IsLeftOut(p) && !isService.IsService(p.ParameterType))
                .Select(p => p.ParameterType)
                .Distinct()
                .ToList();

        private static bool IsLeftOut(ParameterInfo parameter) =>
            parameter.HasDefaultValue
            || parameter.IsDefined(typeof(FromKeyedServicesAttribute), inherit: true)
            || parameter.IsDefined(typeof(ServiceKeyAttribute), inherit: true);

        /// <summary>
        /// The class the container builds for <paramref name="serviceType"/> from its
        /// constructor, or null when it is registered through a factory or an instance, or not
        /// found. The last registration wins, as it does when the container resolves one.
        /// </summary>
        private Type? ImplementationOf(Type serviceType)
        {
            if (descriptors is null)
                return null;

            ServiceDescriptor? closed = null;
            ServiceDescriptor? open = null;
            var definition = serviceType.IsConstructedGenericType
                ? serviceType.GetGenericTypeDefinition()
                : null;

            foreach (var descriptor in descriptors)
            {
                if (descriptor.IsKeyedService)
                    continue;

                if (descriptor.ServiceType == serviceType)
                    closed = descriptor;
                else if (definition is not null && descriptor.ServiceType == definition)
                    open = descriptor;
            }

            if (closed is not null)
                return closed.ImplementationType is { IsAbstract: false } type ? type : null;

            if (open?.ImplementationType is { IsGenericTypeDefinition: true } generic)
            {
                try
                {
                    return generic.MakeGenericType(serviceType.GetGenericArguments());
                }
                catch (ArgumentException)
                {
                    return null;
                }
            }

            return null;
        }

        private static string Through(List<Type> through) =>
            through.Count == 0
                ? ""
                : ", and the train's constructor reaches it through "
                    + string.Join(" -> ", through.Select(t => $"'{Readable(t)}'"));
    }

    /// <summary>
    /// A type's short name, generics written as <c>Name&lt;Arg&gt;</c>, and a type nested in a
    /// generic type written with its outer type, which owns the arguments:
    /// <c>Outer&lt;Arg&gt;.Inner</c>.
    /// </summary>
    private static string Readable(Type type) =>
        Readable(type, type.IsGenericType ? type.GetGenericArguments() : []);

    private static string Readable(Type type, Type[] arguments)
    {
        var prefix = "";
        var inherited = 0;

        if (type.IsNested && type.DeclaringType is { IsGenericType: true } outer)
        {
            inherited = Math.Min(outer.GetGenericArguments().Length, arguments.Length);
            prefix = Readable(outer, arguments[..inherited]) + ".";
        }

        var name = type.Name;
        var tick = name.IndexOf('`');

        if (tick >= 0)
            name = name[..tick];

        var own = arguments[inherited..];

        return own.Length == 0
            ? prefix + name
            : $"{prefix}{name}<{string.Join(", ", own.Select(Readable))}>";
    }
}
