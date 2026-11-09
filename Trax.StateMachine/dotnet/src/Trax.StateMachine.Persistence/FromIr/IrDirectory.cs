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
    internal const string HostFileName = MachineFiles.HostFileName;
    internal const string IrSuffix = MachineFiles.IrSuffix;

    /// <summary>The machines a directory serves, built and checked, and the host file they were served from.</summary>
    internal sealed record Loaded(IReadOnlyList<IMachine> Machines, HostFile Host);

    /// <summary>Reads, checks and builds every machine of <paramref name="directory"/>.</summary>
    /// <param name="directory">The directory <c>from-gherkin</c> writes.</param>
    /// <param name="trainAssemblies">Where the trains the machines invoke are looked for, by their full name.</param>
    internal static Loaded Load(string directory, IReadOnlyCollection<Assembly> trainAssemblies)
    {
        var (host, irs) = Read(directory);
        var machines = IrMachines.Create(
            irs.Select(kv => new IrMachines.Source(kv.Value, host.Machines[kv.Key])).ToList(),
            trainAssemblies
        );
        return new Loaded(machines, host);
    }

    /// <summary>
    /// Reads and checks the host file and every IR of <paramref name="directory"/>, building nothing: what
    /// <c>trax machine run</c> runs the features on.
    /// </summary>
    internal static (HostFile Host, IReadOnlyDictionary<string, JsonObject> Irs) Read(
        string directory
    )
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
        foreach (var (id, ir) in irs)
            if (MachineFiles.Mismatch(host, id, ir) is { } mismatch)
                throw new InvalidOperationException(
                    $"{Path.Combine(directory, id + IrSuffix)}: {mismatch}"
                );
        return (host, irs);
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
        if (loaded.Host.Views.Count > 0)
            services.AddSingleton(new MachineViewCatalog(loaded.Host.Views));
        if (loaded.Host.Policies.Count > 0)
        {
            var options = new PolicyRunnerOptions();
            policies?.Invoke(options);
            options.Validate();
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
        var problems = MachineFiles.Problems(host, irs);
        if (problems.Count > 0)
            throw new InvalidOperationException(
                $"The machines in '{directory}' cannot be served:"
                    + Environment.NewLine
                    + string.Join(Environment.NewLine, problems.Select(p => "  - " + p))
            );
    }
}
