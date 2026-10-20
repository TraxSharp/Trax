namespace Trax.Effect.StateMachine.Tests.Helpers;

/// <summary>
/// Locates the shared, language-neutral fixtures in <c>Trax.Api.StateMachine/machines</c>, which both engines drive.
/// The folder is part of the repository, so a build that cannot find it is broken: every member throws rather than
/// letting the conformance tests skip, which would report green without comparing the engines.
/// </summary>
public static class FixturePaths
{
    private static readonly Lazy<string> Root = new(() => Find(AppContext.BaseDirectory));

    /// <summary>The shared <c>machines/</c> directory.</summary>
    public static string MachinesRoot => Root.Value;

    public static string AdvanceDir(string machine) =>
        Path.Combine(MachinesRoot, machine, "fixtures", "advance");

    public static string RehydrateDir(string machine) =>
        Path.Combine(MachinesRoot, machine, "fixtures", "rehydrate");

    /// <summary>The shared, TypeScript-generated differential corpus for a machine.</summary>
    public static string DifferentialFile(string machine) =>
        Path.Combine(MachinesRoot, machine, "differential.json");

    /// <summary>The committed IR of a machine, which the TypeScript twin and its corpus are generated from.</summary>
    public static string IrFile(string machine) =>
        Path.Combine(MachinesRoot, machine, $"{machine}.ir.json");

    /// <summary>
    /// The engine-neutral conformance kit beside <c>machines/</c>: tables and cases any engine, in any language,
    /// replays.
    /// </summary>
    public static string ConformanceRoot =>
        Path.Combine(Directory.GetParent(MachinesRoot)!.FullName, "conformance");

    /// <summary>The shared migration golden for a machine: stored older-version snapshots and the exact
    /// canonical wire each must become after forward migration.</summary>
    public static string MigrationFile(string machine) =>
        Path.Combine(MachinesRoot, machine, "migration.json");

    /// <summary>
    /// Walks up from <paramref name="start"/> to the first directory holding
    /// <c>Trax.Api.StateMachine/machines/turnstile/fixtures</c>, and throws, naming every directory it tried, when
    /// there is none.
    /// </summary>
    public static string Find(string start)
    {
        var searched = new List<string>();
        var dir = new DirectoryInfo(start);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "Trax.Api.StateMachine", "machines");
            if (Directory.Exists(Path.Combine(candidate, "turnstile", "fixtures")))
                return candidate;
            searched.Add(candidate);
            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException(
            "The shared state-machine fixtures (Trax.Api.StateMachine/machines) were not found. They are part of the "
                + "repository, so the build or the copy that ran these tests left them out. Searched: "
                + string.Join(", ", searched)
        );
    }
}
