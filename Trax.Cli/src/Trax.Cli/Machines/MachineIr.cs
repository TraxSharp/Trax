using Trax.Cli.Clients;
using Trax.StateMachine;
using Trax.StateMachine.Persistence;

namespace Trax.Cli.Machines;

/// <summary>
/// Where <c>generate</c> and <c>check</c> read a machine's IR from: a compiled machine (<c>--assembly</c>) or one
/// machine of a host's client manifest (<c>--manifest</c>), for a frontend that has the manifest and not the server.
/// Both are read when asked, so a machine whose <c>Id(...)</c> the engine refuses is reported as a refusal.
/// </summary>
internal sealed class MachineIr
{
    private readonly Func<string> _name;
    private readonly Func<string> _export;

    private MachineIr(Func<string> name, Func<string> export)
    {
        _name = name;
        _export = export;
    }

    /// <summary>The machine's id, which names every generated file.</summary>
    public string Name => _name();

    /// <summary>The IR's text, without a trailing newline: the bytes the machine's IR hash is the SHA-256 of.</summary>
    public string ExportIr() => _export();

    public static MachineIr Of(IMachine machine) => new(() => machine.Name, machine.ExportIr);

    /// <summary>A manifest's machine, refused when its IR is not one a view may be generated from or does not hash
    /// to the manifest's <see cref="ClientManifestMachine.IrHash"/>.</summary>
    public static MachineIr Of(ClientManifestMachine machine)
    {
        ClientGenerator.CheckIr(machine);
        return new(() => machine.Id, machine.IrText);
    }

    /// <summary>
    /// The machine named <paramref name="id"/> in the manifest at <paramref name="manifestPath"/>, or its only
    /// machine when no id is given.
    /// </summary>
    public static MachineIr FromManifest(string manifestPath, string? id)
    {
        if (!File.Exists(manifestPath))
            throw new InvalidOperationException($"Manifest not found: {manifestPath}");
        ClientManifest manifest;
        try
        {
            manifest = ClientManifest.Read(File.ReadAllText(manifestPath));
        }
        catch (InvalidOperationException ex)
        {
            throw new InvalidOperationException($"{manifestPath}: {ex.Message}", ex);
        }

        // A machine listed twice leaves --machine with two IRs to pick from, so the manifest is refused whole.
        var repeated = manifest
            .Machines.GroupBy(m => m.Id, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => $"'{g.Key}'")
            .ToList();
        if (repeated.Count > 0)
            throw new InvalidOperationException(
                $"{manifestPath}: The client manifest is malformed at $.machines: {string.Join(", ", repeated)} "
                    + (repeated.Count == 1 ? "is" : "are")
                    + " listed more than once. Export it again from the host."
            );

        var ids = string.Join(", ", manifest.Machines.Select(m => m.Id));
        if (id is not null)
            return Of(
                manifest.Machines.FirstOrDefault(m => m.Id == id)
                    ?? throw new InvalidOperationException(
                        manifest.Machines.Count == 0
                            ? $"The manifest {manifestPath} exposes no machine, so it has no '{id}'."
                            : $"The manifest {manifestPath} has no machine '{id}'. It has: {ids}."
                    )
            );
        return manifest.Machines switch
        {
            [var only] => Of(only),
            [] => throw new InvalidOperationException(
                $"The manifest {manifestPath} exposes no machine. Export it again from a host that exposes one."
            ),
            _ => throw new InvalidOperationException(
                $"The manifest {manifestPath} has {manifest.Machines.Count} machines; pick one with --machine: {ids}."
            ),
        };
    }
}
