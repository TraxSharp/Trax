using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Trax.Effect.Services.ServiceTrain;
using Trax.StateMachine.Features;

namespace Trax.StateMachine.Persistence.FromIr;

/// <summary>
/// Builds the machines a host serves from IR files and their entries in a host file, with no C# machine class (ADR
/// <c>Trax.Docs/adr/0058</c>). Everything is checked before a machine is returned, and every refusal is an
/// <see cref="InvalidOperationException"/> naming the machine: the IR is one this engine reads and a served machine can
/// be; it is the IR its host entry was derived from; every train it invokes, and every effect, custom rule, custom
/// reduction and migration its host entry names for it, one for one, is found; and the machine built from it exports
/// exactly the file.
/// </summary>
internal static class IrMachines
{
    /// <summary>One machine to serve: its IR, its entry in the host file, and its corpus to self-check against.</summary>
    internal sealed record Source(JsonObject Ir, HostMachine Host, string? Corpus = null);

    /// <summary>Builds every machine of <paramref name="sources"/>, refusing two whose type names would collide.</summary>
    /// <param name="sources">The machines.</param>
    /// <param name="trainAssemblies">
    /// Where the trains the machines invoke, and the effects, custom rules, reductions and migrations their host entries
    /// name, are looked for, by their full name.
    /// </param>
    internal static IReadOnlyList<IMachine> Create(
        IReadOnlyList<Source> sources,
        IReadOnlyCollection<Assembly> trainAssemblies
    )
    {
        var prefixes = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var source in sources)
        {
            var id = source.Host.Id;
            if (prefixes.TryGetValue(IrTypes.Pascal(id), out var other))
                throw new InvalidOperationException(
                    $"The machines '{other}' and '{id}' would both emit types named {IrTypes.Pascal(id)}*. Rename one."
                );
            prefixes[IrTypes.Pascal(id)] = id;
        }
        return sources.Select(s => Create(s, trainAssemblies)).ToList();
    }

    /// <summary>Builds one machine from its IR and its host entry.</summary>
    internal static IMachine Create(Source source, IReadOnlyCollection<Assembly> trainAssemblies)
    {
        var (ir, host, corpus) = source;
        var id = host.Id;
        string Refuse(string problem) =>
            $"The machine '{id}' cannot be served from its IR: {problem}";

        // Read once by the engine, which refuses an IR it cannot read; then held to what a served machine can be.
        try
        {
            _ = new IrMachine(ir);
        }
        catch (ArgumentException ex)
        {
            throw new InvalidOperationException(Refuse(ex.Message), ex);
        }
        if (ir["id"]!.GetValue<string>() != id)
            throw new InvalidOperationException(
                Refuse($"the IR's id is '{ir["id"]}', and the host file's entry is '{id}'.")
            );
        foreach (var problem in Unservable(ir))
            throw new InvalidOperationException(Refuse(problem));

        // The host entry was derived from this IR, by its hash, not a hash of the file's bytes: the canonical IR's,
        // which the store writes to every instance's ir_hash.
        var canonical = CanonicalJson.Serialize(Canonicalizable(ir));
        var hash = Hash(canonical);
        if (host.SchemaHash != hash)
            throw new InvalidOperationException(
                Refuse(
                    $"the host file's entry was derived from the IR {host.SchemaHash}, and this IR is {hash}. "
                        + "Derive them again together."
                )
            );

        foreach (var problem in MachineFiles.HandlerProblems(host, ir))
            throw new InvalidOperationException(Refuse(problem));
        var trains = Trains(ir, host, trainAssemblies, Refuse);
        var handlers = Handlers(host, trainAssemblies, Refuse);

        IrTypes types;
        try
        {
            types = IrTypes.Emit(
                id,
                Names(ir["states"]),
                Names(ir["triggers"]),
                Schemas(ir["context"]),
                Schemas(ir["inputs"])
            );
        }
        catch (InvalidOperationException ex)
        {
            throw new InvalidOperationException(Refuse(ex.Message), ex);
        }

        var replay = new IrReplay(ir, host, types, trains, corpus) { Handlers = handlers };
        var kind = host.IsSystem ? typeof(SystemIrMachine<,>) : typeof(ServedIrMachine<,>);
        var machine = (IMachine)
            Activator.CreateInstance(
                kind.MakeGenericType(types.State, types.Trigger),
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                binder: null,
                args: [replay],
                culture: null
            )!;

        // Built here, so a refusal names the machine at registration rather than at its first request.
        string exported;
        try
        {
            exported = machine.ExportIr();
        }
        catch (Exception ex)
            when (ex is InvalidOperationException or ArgumentException or TargetInvocationException)
        {
            var cause = ex is TargetInvocationException { InnerException: { } inner } ? inner : ex;
            throw new InvalidOperationException(Refuse(cause.Message), cause);
        }
        if (exported != canonical)
            throw new InvalidOperationException(
                Refuse(
                    "the machine built from it exports a different IR, so the server would not run what the file and "
                        + $"every client generated from it say. First difference: {FirstDifference(canonical, exported)}."
                )
            );
        return machine;
    }

    /// <summary>
    /// Registers machines built by <see cref="Create(IReadOnlyList{Source}, IReadOnlyCollection{Assembly})"/> as the
    /// host's own <see cref="IMachine"/> registrations, beside the ones <c>AddStateMachines</c> scans for, so the
    /// registry, the stores and the startup checks see them as any other machine.
    /// </summary>
    internal static IServiceCollection AddIrMachines(
        this IServiceCollection services,
        IEnumerable<IMachine> machines
    )
    {
        foreach (var machine in machines)
            services.TryAddEnumerable(ServiceDescriptor.Singleton(typeof(IMachine), machine));
        return services;
    }

    /// <summary>The input and output types of a train interface, from its <c>IServiceTrain&lt;TIn, TOut&gt;</c>.</summary>
    internal static (Type Input, Type Output) TrainTypes(Type train)
    {
        var service =
            (train.IsGenericType && train.GetGenericTypeDefinition() == typeof(IServiceTrain<,>))
                ? train
                : train
                    .GetInterfaces()
                    .Single(i =>
                        i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IServiceTrain<,>)
                    );
        var arguments = service.GetGenericArguments();
        return (arguments[0], arguments[1]);
    }

    // What no served machine can be: a failed or cancelled outcome edge with a guard, which the builder has no way to
    // declare.
    private static IEnumerable<string> Unservable(JsonObject ir)
    {
        foreach (var (name, outcome) in ir["outcomes"] as JsonObject ?? [])
            if (outcome!["outcome"]!.GetValue<string>() != "done")
                foreach (var edge in outcome["edges"]!.AsArray())
                    if (edge!["guard"] is not null)
                        yield return $"the outcome {name} has a guarded edge, and only a done outcome's edges take "
                            + "a guard.";
    }

    // The C# behind each effect, custom rule, custom reduction and migration the host entry names, each found by its full
    // name in the assemblies given, of the kind it is named as: an effect's ISnapshotEffect interface, which the host
    // resolves from its services when it runs; a handler's class, created here with its public parameterless
    // constructor, since a machine's rules hold no state and reach no service.
    private static IrHandlers Handlers(
        HostMachine host,
        IReadOnlyCollection<Assembly> assemblies,
        Func<string, string> refuse
    )
    {
        Type Find(string name, string what)
        {
            var found = assemblies
                .Select(a => a.GetType(name, throwOnError: false))
                .OfType<Type>()
                .Distinct()
                .ToList();
            return found.Count == 1
                ? found[0]
                : throw new InvalidOperationException(
                    refuse(
                        found.Count == 0
                            ? $"{what} is {name}, which none of the assemblies given holds."
                            : $"{what} is {name}, which more than one assembly given holds."
                    )
                );
        }

        T Create<T>(string name, string what)
            where T : class
        {
            var type = Find(name, what);
            if (
                !typeof(T).IsAssignableFrom(type)
                || type.IsAbstract
                || type.IsInterface
                || type.ContainsGenericParameters
                || type.GetConstructor(Type.EmptyTypes) is null
            )
                throw new InvalidOperationException(
                    refuse(
                        $"{what} is {name}, which is not a class implementing {typeof(T).Name} with a public "
                            + "parameterless constructor."
                    )
                );
            return (T)Activator.CreateInstance(type)!;
        }

        var effects = new Dictionary<string, Type>(StringComparer.Ordinal);
        foreach (var (trigger, name) in host.Effects)
        {
            var type = Find(name, $"the effect {trigger} runs");
            if (!type.IsInterface || !typeof(ISnapshotEffect).IsAssignableFrom(type))
                throw new InvalidOperationException(
                    refuse(
                        $"the effect {trigger} runs is {name}, which is not an interface extending ISnapshotEffect."
                    )
                );
            effects[trigger] = type;
        }
        return new IrHandlers(
            effects,
            host.Rules.ToDictionary(
                kv => kv.Key,
                kv => Create<ICustomRule>(kv.Value, $"the custom rule '{kv.Key}'"),
                StringComparer.Ordinal
            ),
            host.Reductions.ToDictionary(
                kv => kv.Key,
                kv => Create<ICustomReduction>(kv.Value, $"the custom reduction '{kv.Key}'"),
                StringComparer.Ordinal
            ),
            host.Migrations.ToDictionary(
                kv => kv.Key,
                kv => Create<IMachineMigration>(kv.Value, $"the migration from version {kv.Key}")
            )
        );
    }

    // The train each invoking state runs: named by the host file, found by its full name in the assemblies given, and an
    // IServiceTrain. The outcome's output schema is checked against the train's output type when the export is compared.
    private static Dictionary<string, Type> Trains(
        JsonObject ir,
        HostMachine host,
        IReadOnlyCollection<Assembly> assemblies,
        Func<string, string> refuse
    )
    {
        var invoking = (ir["outcomes"] as JsonObject ?? [])
            .Select(o => o.Value!["state"]!.GetValue<string>())
            .ToHashSet(StringComparer.Ordinal);
        foreach (var state in host.Invokes.Keys)
            if (!invoking.Contains(state))
                throw new InvalidOperationException(
                    refuse(
                        $"the host file names a train for {state}, which the IR does not say invokes one."
                    )
                );

        var trains = new Dictionary<string, Type>(StringComparer.Ordinal);
        foreach (var state in invoking.Order(StringComparer.Ordinal))
        {
            if (!host.Invokes.TryGetValue(state, out var invoke))
                throw new InvalidOperationException(
                    refuse($"{state} invokes a train, and the host file does not name which.")
                );
            var found = assemblies
                .Select(a => a.GetType(invoke.Train, throwOnError: false))
                .OfType<Type>()
                .Distinct()
                .ToList();
            if (found.Count != 1)
                throw new InvalidOperationException(
                    refuse(
                        found.Count == 0
                            ? $"{state} invokes {invoke.Train}, which none of the train assemblies holds."
                            : $"{state} invokes {invoke.Train}, which more than one train assembly holds."
                    )
                );
            var train = found[0];
            if (
                !train.IsInterface
                || !train
                    .GetInterfaces()
                    .Any(i =>
                        i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IServiceTrain<,>)
                    )
            )
                throw new InvalidOperationException(
                    refuse(
                        $"{state} invokes {invoke.Train}, which is not a train interface (IServiceTrain<TIn, TOut>)."
                    )
                );
            trains[state] = train;
        }
        return trains;
    }

    // An IR as the exporter writes it, so its canonical text and hash are the ones the exporter produces. The engine
    // accepts an irFormat beside it, which a generated twin adds and the exporter does not write.
    private static JsonObject Canonicalizable(JsonObject ir)
    {
        var copy = (JsonObject)ir.DeepClone();
        copy.Remove("irFormat");
        return copy;
    }

    private static List<string> Names(JsonNode? node) =>
        node!.AsArray().Select(n => n!.GetValue<string>()).ToList();

    private static Dictionary<string, ContextSchema> Schemas(JsonNode? node) =>
        node!
            .AsObject()
            .ToDictionary(
                kv => kv.Key,
                kv => IrMachine.ReadSchema(kv.Value!.AsObject()),
                StringComparer.Ordinal
            );

    private static string FirstDifference(string expected, string actual)
    {
        var at = 0;
        while (at < expected.Length && at < actual.Length && expected[at] == actual[at])
            at++;
        string Around(string text) =>
            text.Substring(Math.Max(0, at - 40), Math.Min(80, text.Length - Math.Max(0, at - 40)));
        return $"the file has ...{Around(expected)}... and the machine ...{Around(actual)}...";
    }

    /// <summary>The hash a host entry names its IR by: the canonical IR's, which the store writes to every instance.</summary>
    internal static string Hash(JsonObject ir) => MachineFiles.Hash(ir);

    private static string Hash(string canonical) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
}
