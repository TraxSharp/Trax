using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Trax.Core.Exceptions;
using Trax.Core.Monad;
using Trax.Effect.Attributes;
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
/// <para>A train's own <c>[Inject]</c> properties are checked the same way, because the container
/// fills them with <c>GetService</c> and leaves one null rather than failing: a property whose
/// type is not registered is refused unless it is declared nullable, which is reported as a
/// warning, and a registered type that can never be built is refused. They are read on the built
/// train, because filling skips a property its constructor or an initializer already set; a train
/// that cannot be built at boot has them reported as warnings only. A junction the chain builds
/// itself (<c>Chain&lt;T&gt;()</c>, <c>ShortCircuit&lt;T&gt;()</c>) never has its <c>[Inject]</c>
/// properties filled, so each one is reported as a warning.</para>
///
/// <para>Every train is checked before anything is reported, so one start tells you about all of
/// them rather than one per attempt. Opt out with
/// <c>AddMediator(m => m.SkipChainVerification())</c>, which turns the <c>[Inject]</c> check off
/// with the rest, for the blind spot named in
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
                    + "until something runs it. The check of each train's [Inject] properties is "
                    + "off with it, so one the container cannot fill is null on every run instead "
                    + "of stopping the host."
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

            var warnings = new List<string>();
            var problem = Check(scope.ServiceProvider, registration, warnings, out var skipped);

            foreach (var warning in warnings)
                logger?.LogWarning("{TrainName}: {Warning}", registration.ServiceTypeName, warning);

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
        List<string> warnings,
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

            // The class without its [Inject] properties filled. When it builds, what failed was
            // filling them, and the instance shows which of them its constructor or initializers
            // already set, which filling skips.
            if (
                UnfilledInjectProperties(
                    services,
                    registration,
                    warnings,
                    BareTrain(services, registration)
                ) is
                { Count: > 0 } unbuildable
            )
                return unbuildable.Select(r => $"{registration.ServiceTypeName}: {r}").ToList();

            // A train whose constructor needs something only a request provides, a current user
            // read from HttpContext say, cannot be built at boot and still runs fine. Refusing
            // to start over it would make the upgrade that adds this check break such hosts.
            skipped = $"it could not be constructed outside a request ({ex.Message})";
            return null;
        }

        // An [Inject] property is filled by GetService, so a type nothing registers leaves it null
        // on every run: the container never fails over one, so the train above built regardless.
        // Read on the built train, because filling skips a property that already holds a value.
        if (
            UnfilledInjectProperties(services, registration, warnings, train) is
            { Count: > 0 } unfilled
        )
            return unfilled.Select(r => $"{registration.ServiceTypeName}: {r}").ToList();

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

        warnings.AddRange(BuiltJunctionsWithInjectProperties(chain));

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

        var refused = Describe(chain, faults).Concat(RefusedCheckpoints(chain, false)).ToList();

        return refused.Count == 0
            ? null
            : refused.Select(f => $"{registration.ServiceTypeName}: {f}").ToList();
    }

    /// <summary>
    /// The checkpoints a host refuses to store: one whose state reaches a member marked
    /// <c>[TraxSensitive]</c>, since a checkpoint is plain JSON, and one inside a track whose
    /// routing key is sensitive, since that route is withheld from every record and a resumed run
    /// could not find its way back into it. See
    /// Trax.Docs/adr/0047-a-checkpoint-stores-a-state-the-train-declares-and-a-resume-skips-to-it.md.
    /// </summary>
    private static IEnumerable<string> RefusedCheckpoints(ChainRecorder chain, bool inWithheldTrack)
    {
        for (var i = 0; i < chain.Steps.Count; i++)
        {
            var step = chain.Steps[i];

            if (step.Kind == ChainStepKind.Checkpoint && step.In is { } state)
            {
                if (Trax.Effect.Utils.TraxRedaction.ReachesSensitiveMember(state))
                    yield return $"Checkpoint<{Readable(state)}> holds a state that reaches a "
                        + "member marked [TraxSensitive]. A checkpoint is stored as plain JSON, "
                        + "so a sensitive state is never stored. Checkpoint a state without it.";

                if (inWithheldTrack)
                    yield return $"Checkpoint<{Readable(state)}> is inside a track whose routing "
                        + "key is marked [TraxSensitive]. That route is withheld from every "
                        + "record, so a run could not resume into it. Checkpoint after the "
                        + "routing step instead.";
            }

            var withheld =
                inWithheldTrack
                || step.Kind is ChainStepKind.Switch or ChainStepKind.Gate or ChainStepKind.Scale
                    && step.Out is { IsGenericType: true } taken
                    && Trax.Effect.Services.JunctionEvents.SensitiveQuestions.IsSensitive(
                        taken.GetGenericArguments()[0]
                    );

            foreach (var track in chain.TracksAt(i))
            foreach (var refusal in RefusedCheckpoints(track.Steps, withheld))
                yield return refusal;
        }
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
    /// The train's own <c>[Inject]</c> properties the container cannot supply, one fault each,
    /// except an unregistered one declared nullable, which is a warning in
    /// <paramref name="warnings"/>.
    /// </summary>
    /// <remarks>
    /// <c>InjectProperties</c> fills a property with <c>GetService</c> and leaves it null when
    /// nothing is registered, so the train still builds and fails on the run that reads it. It
    /// skips a property that already holds a value, one its constructor or an initializer set, so
    /// a property is judged on <paramref name="instance"/> and one holding a value is left alone.
    /// A property declared <c>T?</c> says the train copes without it, so it is only reported; any
    /// other declaration, including one in a project without nullable annotations, is refused, as
    /// an unregistered constructor argument is. A type that is registered is followed through its
    /// constructor the way a constructor argument is: one that can never be built makes
    /// <c>GetService</c> throw, which fails every resolution of the train, so it is refused
    /// however the property is declared. With no <paramref name="instance"/> to read, whether a
    /// property is set cannot be told, so everything found is a warning and nothing is refused.
    /// <c>ServiceTrain</c>'s own framework properties are left out: they are optional by design.
    /// An <c>IEnumerable&lt;T&gt;</c> property is always filled, if only with nothing.
    /// </remarks>
    private static List<string> UnfilledInjectProperties(
        IServiceProvider services,
        TrainRegistration registration,
        List<string> warnings,
        object? instance
    )
    {
        var faults = new List<string>();
        var implementation = registration.ImplementationType;

        if (
            implementation.IsAbstract
            || services.GetService<IServiceProviderIsService>() is not { } isService
        )
            return faults;

        // A train registered through a factory may hand back something else; its properties
        // cannot be read from the declared class.
        if (instance is not null && !implementation.IsInstanceOfType(instance))
            instance = null;

        var nullability = new NullabilityInfoContext();

        foreach (var property in InjectProperties(implementation))
        {
            // ServiceTrain's own properties, declared in Trax.Effect beside the attribute.
            if (property.DeclaringType?.Assembly == typeof(InjectAttribute).Assembly)
                continue;

            var type = property.PropertyType;

            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IEnumerable<>))
                continue;

            if (instance is not null && HoldsValue(property, instance))
                continue;

            var optional = DeclaredNullable(nullability, property);
            var registered = false;
            string? reason;

            try
            {
                registered = isService.IsService(type);
                reason = registered
                    ? new DependencyWalk(
                        isService,
                        services.GetService<IServiceCollection>(),
                        $"the train's [Inject] property '{property.Name}'"
                    ).WhyNotRegistered(type)
                    : $"its [Inject] property '{property.Name}' needs '{Readable(type)}', which "
                        + "is not registered, so the property is null on every run."
                        + (
                            optional
                                ? ""
                                : " Register it before building the host, or declare the "
                                    + "property nullable if the train runs without it."
                        );
            }
            catch (Exception)
            {
                // A container that cannot answer says nothing about the property.
                continue;
            }

            if (reason is null)
                continue;

            // A registered type that cannot be built throws out of GetService, which fails the
            // train's resolution on every run however the property is declared.
            if (optional && !registered)
                warnings.Add(
                    reason + " It is declared nullable, so the train is taken to cope without it."
                );
            else if (instance is null)
                warnings.Add(
                    reason
                        + " The train could not be built here, so whether its constructor or an "
                        + "initializer sets the property could not be checked."
                );
            else
                faults.Add(reason);
        }

        return faults;
    }

    /// <summary>
    /// The train's class built by the container without its <c>[Inject]</c> properties filled, or
    /// null when that fails too or the class is not registered on its own.
    /// </summary>
    private static object? BareTrain(IServiceProvider services, TrainRegistration registration)
    {
        if (registration.ImplementationType.IsAbstract)
            return null;

        try
        {
            return services.GetService(registration.ImplementationType);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Whether <paramref name="property"/> already holds a value on the train.</summary>
    private static bool HoldsValue(PropertyInfo property, object instance)
    {
        try
        {
            return property.GetValue(instance) is not null;
        }
        catch (Exception)
        {
            // A getter that throws holds nothing filling could read either; InjectProperties
            // would throw on it too, which the build would have shown.
            return false;
        }
    }

    /// <summary>
    /// Whether <paramref name="property"/> is declared nullable. A trimmed app turns nullability
    /// metadata off (<c>NullabilityInfoContextSupport</c>), and then every property reads as
    /// unknown; that says nothing about the declaration, so it is taken as nullable rather than
    /// refusing a property the source declares <c>T?</c>.
    /// </summary>
    private static bool DeclaredNullable(NullabilityInfoContext context, PropertyInfo property) =>
        context.Create(property).ReadState switch
        {
            NullabilityState.Nullable => true,
            NullabilityState.Unknown => !NullabilityMetadataSupported,
            _ => false,
        };

    /// <summary>
    /// Whether <see cref="NullabilityInfoContext"/> reads nullability metadata in this process.
    /// </summary>
    private static bool NullabilityMetadataSupported =>
        !AppContext.TryGetSwitch(NullabilityInfoContextSwitch, out var supported) || supported;

    private const string NullabilityInfoContextSwitch =
        "System.Reflection.NullabilityInfoContext.IsSupported";

    /// <summary>
    /// A warning for every <c>[Inject]</c> property of a junction the chain builds itself, with
    /// <c>Chain&lt;T&gt;()</c> or <c>ShortCircuit&lt;T&gt;()</c>, including inside a routing step's
    /// tracks.
    /// </summary>
    /// <remarks>
    /// A junction built that way is constructed from Memory and the container and never has its
    /// properties filled, so each such property is null whenever the junction runs. Only
    /// <c>IChain&lt;TInterface&gt;()</c> resolves the junction's registration, which fills them.
    /// It is a warning and not a refusal: whether the junction ever reads the property, or sets it
    /// itself, cannot be told from its type, and a junction that guards against null runs fine.
    /// </remarks>
    private static IEnumerable<string> BuiltJunctionsWithInjectProperties(ChainRecorder chain)
    {
        var seen = new System.Collections.Generic.HashSet<Type>();

        IEnumerable<string> Walk(ChainRecorder recorder)
        {
            for (var i = 0; i < recorder.Steps.Count; i++)
            {
                var step = recorder.Steps[i];

                if (
                    step.Kind is ChainStepKind.Chain or ChainStepKind.ShortCircuit
                    && step.Junction is { IsInterface: false } junction
                    && seen.Add(junction)
                )
                {
                    var names = InjectProperties(junction).Select(p => $"'{p.Name}'").ToList();

                    if (names.Count > 0)
                        yield return $"{step.Kind}<{Readable(junction)}> builds the junction "
                            + "itself, which never fills [Inject] properties, so "
                            + string.Join(", ", names)
                            + (names.Count == 1 ? " is" : " are")
                            + " null whenever it runs. Take the dependency as a constructor "
                            + "parameter, or register the junction under an interface and reach "
                            + $"it with IChain.";
                }

                foreach (var track in recorder.TracksAt(i))
                foreach (var warning in Walk(track.Steps))
                    yield return warning;
            }
        }

        return Walk(chain).ToList();
    }

    /// <summary>
    /// The properties <c>InjectProperties</c> fills: public, instance, writable, marked
    /// <c>[Inject]</c>.
    /// </summary>
    private static IEnumerable<PropertyInfo> InjectProperties(Type type) =>
        type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.IsDefined(typeof(InjectAttribute)) && p.CanWrite);

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
        IServiceCollection? descriptors,
        string reachedBy = "the train's constructor"
    )
    {
        /// <summary>How deep the walk follows dependencies before it gives up and says nothing.</summary>
        private const int MaxDepth = 16;

        private readonly System.Collections.Generic.HashSet<Type> _visited = [];

        /// <summary>Why <paramref name="implementation"/> can never be built, or null.</summary>
        public string? WhyNot(Type implementation) => WhyNot(implementation, [], 0);

        /// <summary>
        /// Why the class registered for <paramref name="serviceType"/> can never be built, or
        /// null, including when it is registered through a factory or an instance.
        /// </summary>
        public string? WhyNotRegistered(Type serviceType) =>
            ImplementationOf(serviceType) is { } implementation
                ? WhyNot(implementation, [serviceType], 1)
                : null;

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

        private string Through(List<Type> through) =>
            through.Count == 0
                ? ""
                : $", and {reachedBy} reaches it through "
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
