using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Trax.StateMachine.Features;

namespace Trax.StateMachine.Persistence.FromIr;

/// <summary>
/// Reads a directory of machines derived from features (ADR <c>Trax.Docs/adr/0058</c>): one <c>&lt;id&gt;.ir.json</c>
/// per machine and one <c>host.json</c>. Everything is checked before a machine is built, and every refusal is an
/// <see cref="InvalidOperationException"/> naming the file and what in it is wrong: each IR is named for its id, the host
/// file and the IRs list the same machines, and every machine, state, trigger and field a policy or a view names exists
/// in the IRs. Each machine is then built by <see cref="IrMachines.Create(IrMachines.Source, IReadOnlyCollection{Assembly})"/>.
/// </summary>
internal static class IrDirectory
{
    internal const string HostFileName = "host.json";
    internal const string IrSuffix = ".ir.json";

    /// <summary>The machines a directory serves, built and checked, and the host file they were served from.</summary>
    internal sealed record Loaded(IReadOnlyList<IMachine> Machines, HostFile Host);

    /// <summary>Reads, checks and builds every machine of <paramref name="directory"/>.</summary>
    /// <param name="directory">The directory <c>from-gherkin</c> writes.</param>
    /// <param name="trainAssemblies">Where the trains the machines invoke are looked for, by their full name.</param>
    internal static Loaded Load(string directory, IReadOnlyCollection<Assembly> trainAssemblies)
    {
        if (!Directory.Exists(directory))
            throw new InvalidOperationException(
                $"The machines directory '{directory}' does not exist."
            );
        var hostPath = Path.Combine(directory, HostFileName);
        if (!File.Exists(hostPath))
            throw new InvalidOperationException(
                $"The machines directory '{directory}' has no {HostFileName}, which every machine's access is read from."
            );

        HostFile host;
        try
        {
            host = HostFile.Read(File.ReadAllText(hostPath));
        }
        catch (FormatException ex)
        {
            throw new InvalidOperationException($"{hostPath}: {ex.Message}", ex);
        }

        var irs = new SortedDictionary<string, JsonObject>(StringComparer.Ordinal);
        foreach (
            var path in Directory.GetFiles(directory, "*" + IrSuffix).Order(StringComparer.Ordinal)
        )
        {
            var name = Path.GetFileName(path)[..^IrSuffix.Length];
            JsonObject ir;
            try
            {
                ir =
                    JsonNode.Parse(File.ReadAllText(path)) as JsonObject
                    ?? throw new InvalidOperationException($"{path} is not a JSON object.");
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException($"{path} is not JSON: {ex.Message}", ex);
            }
            if (
                ir["id"] is not JsonValue id
                || id.GetValueKind() != JsonValueKind.String
                || id.GetValue<string>() != name
            )
                throw new InvalidOperationException(
                    $"{path} holds the machine {ir["id"]?.ToJsonString() ?? "with no id"}; an IR file is named for its id."
                );
            irs[name] = ir;
        }

        Check(directory, host, irs);
        var machines = IrMachines.Create(
            irs.Select(kv => new IrMachines.Source(kv.Value, host.Machines[kv.Key])).ToList(),
            trainAssemblies
        );
        return new Loaded(machines, host);
    }

    /// <summary>
    /// Registers every machine of <paramref name="directory"/> as the host's own <see cref="IMachine"/>, refusing the
    /// host if any is refused. Call it before <c>AddTraxGraphQL</c>, which serves the machines registered before it.
    /// </summary>
    internal static IServiceCollection AddIrMachinesFrom(
        this IServiceCollection services,
        string directory,
        params Assembly[] trainAssemblies
    ) => services.AddIrMachinesFrom(directory, null, trainAssemblies);

    /// <summary>
    /// As <see cref="AddIrMachinesFrom(IServiceCollection, string, Assembly[])"/>, and, when the host file has policies,
    /// the runner that fires them, configured by <paramref name="policies"/>.
    /// </summary>
    internal static IServiceCollection AddIrMachinesFrom(
        this IServiceCollection services,
        string directory,
        Action<PolicyRunnerOptions>? policies,
        params Assembly[] trainAssemblies
    )
    {
        var loaded = Load(directory, trainAssemblies);
        services.AddIrMachines(loaded.Machines);
        services.AddSingleton(loaded.Host);
        if (loaded.Host.Machines.Values.Any(m => m.SystemKey is not null))
            services.AddHostedService<SystemInstanceStarter>();
        if (loaded.Host.Views.Count > 0)
            services.AddSingleton(new MachineViewCatalog(loaded.Host.Views));
        if (loaded.Host.Policies.Count > 0)
        {
            var options = new PolicyRunnerOptions();
            policies?.Invoke(options);
            services.AddSingleton(new MachinePolicies(loaded.Host.Policies));
            services.AddSingleton(options);
            services.AddSingleton<MachinePolicyRunner>();
            services.AddHostedService(sp => sp.GetRequiredService<MachinePolicyRunner>());
        }
        return services;
    }

    // The host file against the IRs: the same machines, and every name a policy, a view or an invoke gives exists.
    internal static void Check(
        string directory,
        HostFile host,
        IReadOnlyDictionary<string, JsonObject> irs
    )
    {
        var problems = new List<string>();
        foreach (var id in host.Machines.Keys.Where(id => !irs.ContainsKey(id)))
            problems.Add($"{HostFileName} serves '{id}', and there is no {id}{IrSuffix}.");
        foreach (var id in irs.Keys.Where(id => !host.Machines.ContainsKey(id)))
            problems.Add(
                $"{id}{IrSuffix} has no entry in {HostFileName}, so nothing says who may use it."
            );

        if (problems.Count == 0)
        {
            var machines = irs.ToDictionary(
                kv => kv.Key,
                kv => new IrNames(kv.Value),
                StringComparer.Ordinal
            );

            foreach (var (id, machine) in host.Machines)
            {
                var names = machines[id];
                foreach (
                    var trigger in machine.Triggers.Keys.Where(t => !names.Triggers.Contains(t))
                )
                    problems.Add(
                        $"{HostFileName} gives '{id}' a rule for '{trigger}', which is not one of its triggers."
                    );
                foreach (var (state, invoke) in machine.Invokes)
                {
                    if (!names.States.Contains(state))
                        problems.Add(
                            $"{HostFileName} names a train for '{id}' in '{state}', which is not one of its states."
                        );
                    foreach (var from in invoke.Input.Values.Where(f => !names.Fields.Contains(f)))
                        problems.Add(
                            $"{HostFileName} maps '{id}''s train input from '{from}', which no context of '{id}' has."
                        );
                }
            }

            for (var i = 0; i < host.Policies.Count; i++)
            {
                var policy = host.Policies[i];
                var at = $"{HostFileName} policy {i} ({policy.Shape})";
                if (!machines[policy.When].States.Contains(policy.Enters))
                    problems.Add(
                        $"{at} waits for '{policy.When}' to enter '{policy.Enters}', which is not one of its states."
                    );
                var target = machines[policy.Target];
                if (!target.Triggers.Contains(policy.Trigger))
                    problems.Add(
                        $"{at} fires '{policy.Trigger}', which is not one of '{policy.Target}''s triggers."
                    );
                if (policy.Shape == "broadcast" && !target.States.Contains(policy.By))
                    problems.Add(
                        $"{at} reaches '{policy.Target}' in '{policy.By}', which is not one of its states."
                    );
                if (policy.Shape == "linked" && !target.Fields.Contains(policy.By))
                    problems.Add(
                        $"{at} links by '{policy.By}', which no context of '{policy.Target}' has."
                    );
            }

            foreach (var view in host.Views.Values)
            foreach (var field in view.Fields)
            {
                var names = machines[field.Machine];
                if (!field.IsState && !names.Fields.Contains(field.Source))
                    problems.Add(
                        $"{HostFileName} view '{view.Name}' shows '{field.Machine}.{field.Source}', which no context of '{field.Machine}' has."
                    );
                foreach (var state in field.Labels?.Keys ?? [])
                    if (!names.States.Contains(state))
                        problems.Add(
                            $"{HostFileName} view '{view.Name}' labels '{state}', which is not a state of '{field.Machine}'."
                        );
            }
        }

        if (problems.Count > 0)
            throw new InvalidOperationException(
                $"The machines in '{directory}' cannot be served:"
                    + Environment.NewLine
                    + string.Join(Environment.NewLine, problems.Select(p => "  - " + p))
            );
    }

    // The names an IR declares: its states, triggers and every top-level field of any of its contexts.
    private sealed class IrNames(JsonObject ir)
    {
        public HashSet<string> States { get; } = Names(ir["states"]);
        public HashSet<string> Triggers { get; } = Names(ir["triggers"]);
        public HashSet<string> Fields { get; } =
            (ir["context"] as JsonObject ?? [])
                .SelectMany(c => c.Value?["fields"] as JsonArray ?? [])
                .Select(f => f?["name"]?.GetValue<string>())
                .OfType<string>()
                .ToHashSet(StringComparer.Ordinal);

        private static HashSet<string> Names(JsonNode? node) =>
            (node as JsonArray ?? [])
                .Select(n => n?.GetValue<string>())
                .OfType<string>()
                .ToHashSet(StringComparer.Ordinal);
    }
}
