using System.Text;
using Trax.StateMachine.Features;
using Trax.StateMachine.Gherkin;

namespace Trax.Cli.Machines;

/// <summary>
/// Derives machines from a directory of features and a glossary, and writes them to a directory it owns (ADR
/// <c>Trax.Docs/adr/0058</c>): one <c>&lt;machine&gt;.ir.json</c> per machine, one <c>host.json</c> and
/// <c>access.txt</c>. A machine that no longer derives has its file removed, and a directory holding anything else is
/// refused rather than cleaned. <see cref="Check"/> writes nothing and names every file that differs.
/// </summary>
internal static class FeatureDirectory
{
    internal const string HostFileName = "host.json";
    internal const string AccessFileName = "access.txt";
    internal const string IrSuffix = ".ir.json";

    /// <summary>What a derivation gave: the files to write, keyed by name, or the problems that stopped it.</summary>
    internal sealed record Result(
        IReadOnlyDictionary<string, string> Files,
        IReadOnlyList<string> Problems,
        IReadOnlyList<string> UiSteps
    );

    /// <summary>Reads the glossary and every <c>*.feature</c> under the features directory, and derives.</summary>
    /// <exception cref="FeatureMachinesException">A path is wrong, or the glossary is not one.</exception>
    internal static Result Derive(string featuresDir, string glossaryPath)
    {
        var (glossary, features) = Read(featuresDir, glossaryPath);
        var derived = FeatureDerivation.Derive(glossary, features);
        return Derived(derived);
    }

    /// <summary>The glossary and every <c>*.feature</c> under the features directory, by relative path.</summary>
    /// <exception cref="FeatureMachinesException">A path is wrong, or the glossary is not one.</exception>
    internal static (Glossary Glossary, List<(string Path, string Text)> Features) Read(
        string featuresDir,
        string glossaryPath
    )
    {
        if (!Directory.Exists(featuresDir))
            throw new FeatureMachinesException(
                File.Exists(featuresDir)
                    ? $"--features is a file, {featuresDir}: give the directory that holds the .feature files."
                    : $"--features does not exist: {featuresDir}."
            );
        if (!File.Exists(glossaryPath))
            throw new FeatureMachinesException(
                Directory.Exists(glossaryPath)
                    ? $"--glossary is a directory, {glossaryPath}: give the glossary's JSON file."
                    : $"There is no glossary at {glossaryPath}."
            );

        Glossary glossary;
        try
        {
            glossary = Glossary.Read(File.ReadAllText(glossaryPath));
        }
        catch (FormatException ex)
        {
            throw new FeatureMachinesException($"{glossaryPath}: {ex.Message}");
        }

        // Relative, '/'-separated and sorted, so the output and its provenance read the same on every machine.
        var features = Directory
            .EnumerateFiles(featuresDir, "*.feature", SearchOption.AllDirectories)
            .Select(path =>
                (
                    Path: Path.GetRelativePath(featuresDir, path).Replace('\\', '/'),
                    Text: File.ReadAllText(path)
                )
            )
            .OrderBy(f => f.Path, StringComparer.Ordinal)
            .ToList();
        if (features.Count == 0)
            throw new FeatureMachinesException($"{featuresDir} holds no .feature files.");
        return (glossary, features);
    }

    private static Result Derived(Derived derived)
    {
        var uiSteps = derived.UiSteps.Select(p => p.ToString()).ToList();
        if (derived.Problems.Count > 0)
            return new Result(
                new Dictionary<string, string>(),
                derived.Problems.Select(p => p.ToString()).ToList(),
                uiSteps
            );

        var files = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var (id, ir) in derived.Irs)
            files[id + IrSuffix] = ir + "\n";
        files[HostFileName] = derived.Host! + "\n";
        files[AccessFileName] = derived.Access!;
        return new Result(files, [], uiSteps);
    }

    /// <summary>Writes the files and removes the IR of every machine that no longer derives.</summary>
    /// <returns>What changed, one line per file.</returns>
    internal static IReadOnlyList<string> Write(
        string outDir,
        IReadOnlyDictionary<string, string> files
    )
    {
        var existing = Owned(outDir);
        Directory.CreateDirectory(outDir);
        var changes = new List<string>();
        foreach (var (name, text) in files)
        {
            var path = Path.Combine(outDir, name);
            var before = File.Exists(path) ? Lf(File.ReadAllText(path)) : null;
            if (before == text)
                continue;
            File.WriteAllText(path, text, new UTF8Encoding(false));
            changes.Add($"{(before is null ? "wrote" : "updated")} {name}");
        }
        foreach (var stale in existing.Where(n => !files.ContainsKey(n)))
        {
            File.Delete(Path.Combine(outDir, stale));
            changes.Add($"removed {stale}");
        }
        return changes;
    }

    /// <summary>Every file that differs from what was derived: missing, stale, or no longer derived.</summary>
    internal static IReadOnlyList<string> Check(
        string outDir,
        IReadOnlyDictionary<string, string> files
    )
    {
        var existing = Owned(outDir);
        var drift = new List<string>();
        foreach (var (name, text) in files)
        {
            var path = Path.Combine(outDir, name);
            if (!File.Exists(path))
                drift.Add($"{name} is missing");
            else if (Lf(File.ReadAllText(path)) != text)
                drift.Add(
                    name == AccessFileName
                        ? $"{name} is stale: who may do what has changed"
                        : $"{name} is stale"
                );
        }
        foreach (var stale in existing.Where(n => !files.ContainsKey(n)))
            drift.Add($"{stale} no longer derives");
        return drift;
    }

    // A checkout that writes CRLF holds the same file, as trax client --check and trax machine check read it.
    private static string Lf(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal);

    /// <summary>Refuses an <c>--out</c> that is a file or holds files this command does not write.</summary>
    internal static void RefuseForeign(string outDir) => Owned(outDir);

    // The files already in the directory, all of them ones this command writes. Anything else is refused, so a wrong
    // --out never deletes a file it did not write.
    private static List<string> Owned(string outDir)
    {
        if (File.Exists(outDir))
            throw new FeatureMachinesException(
                $"--out is a file, {outDir}: give a directory, which will hold one .ir.json per machine, "
                    + $"{HostFileName} and {AccessFileName}."
            );
        if (!Directory.Exists(outDir))
            return [];
        var foreign = Directory
            .EnumerateFileSystemEntries(outDir)
            .Select(Path.GetFileName)
            .Where(n => !IsOwned(n!) || Directory.Exists(Path.Combine(outDir, n!)))
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();
        if (foreign.Count > 0)
            throw new FeatureMachinesException(
                $"--out {outDir} holds {string.Join(", ", foreign)}, which from-gherkin does not write. It owns the "
                    + "directory and removes what no longer derives, so give it a directory of its own."
            );
        return Directory
            .EnumerateFiles(outDir)
            .Select(p => Path.GetFileName(p))
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();
    }

    private static bool IsOwned(string name) =>
        name is HostFileName or AccessFileName
        || (name.EndsWith(IrSuffix, StringComparison.Ordinal) && name.Length > IrSuffix.Length);
}

/// <summary>A path or input <see cref="FeatureDirectory"/> cannot use, worded for the terminal.</summary>
internal sealed class FeatureMachinesException(string message) : Exception(message);
