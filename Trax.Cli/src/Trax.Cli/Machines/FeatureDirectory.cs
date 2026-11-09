using System.Text;
using Trax.StateMachine.Features;
using Trax.StateMachine.Gherkin;

namespace Trax.Cli.Machines;

/// <summary>
/// Derives machines from a directory of features and its lexicon, and writes them to a directory it owns (ADR
/// <c>Trax.Docs/adr/0058</c>, <c>Trax.Docs/adr/0059</c>): one <c>&lt;machine&gt;.ir.json</c> per machine, one
/// <c>host.json</c>, <c>access.txt</c> and the <c>glossary.json</c> the lexicon elaborates to. A machine that no longer
/// derives has its file removed, and a directory holding anything else is refused rather than cleaned.
/// <see cref="Check"/> writes nothing and names every file that differs. A glossary's JSON given by path is still read,
/// for features not yet written with a lexicon, and is not written.
/// </summary>
internal static class FeatureDirectory
{
    internal const string HostFileName = "host.json";
    internal const string AccessFileName = "access.txt";
    internal const string GlossaryFileName = "glossary.json";
    internal const string IrSuffix = ".ir.json";

    /// <summary>
    /// What a derivation gave: the files to write, keyed by name, or the problems that stopped it, and the warnings,
    /// which stop nothing.
    /// </summary>
    internal sealed record Result(
        IReadOnlyDictionary<string, string> Files,
        IReadOnlyList<string> Problems,
        IReadOnlyList<string> UiSteps,
        IReadOnlyList<string> Warnings
    );

    /// <summary>
    /// What the features are read with: the glossary, and its canonical JSON when it was elaborated from the
    /// directory's lexicon, which <c>from-gherkin</c> writes.
    /// </summary>
    internal sealed record Read(
        FeatureGlossary Glossary,
        string? Json,
        List<(string Path, string Text)> Features
    );

    /// <summary>Reads the lexicon and every <c>*.feature</c> under the features directory, and derives.</summary>
    /// <exception cref="FeatureMachinesException">A path is wrong, or the lexicon or glossary is not one.</exception>
    internal static Result Derive(
        string featuresDir,
        string? glossaryPath,
        IReadOnlyList<string>? trains = null
    )
    {
        var read = ReadFeatures(featuresDir, glossaryPath, trains);
        var derived = FeatureDerivation.Derive(read.Glossary.Inner, read.Features);
        return Derived(derived, read);
    }

    /// <summary>
    /// The glossary and every <c>*.feature</c> under the features directory, by relative path. The glossary is the
    /// directory's lexicon, elaborated on every run, or the JSON file at <paramref name="glossaryPath"/> when one is
    /// given. Two lexicons, or a lexicon beside a <c>glossary.json</c>, are refused: the glossary is derived from the
    /// lexicon, so a hand-kept copy beside it could only disagree.
    /// </summary>
    /// <exception cref="FeatureMachinesException">A path is wrong, or the lexicon or glossary is not one.</exception>
    internal static Read ReadFeatures(
        string featuresDir,
        string? glossaryPath,
        IReadOnlyList<string>? trains = null
    )
    {
        if (!Directory.Exists(featuresDir))
            throw new FeatureMachinesException(
                File.Exists(featuresDir)
                    ? $"--features is a file, {featuresDir}: give the directory that holds the .feature files."
                    : $"--features does not exist: {featuresDir}."
            );
        var (glossary, json) = glossaryPath is null
            ? FromLexicon(featuresDir, trains ?? [])
            : (FromJson(glossaryPath), null);

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
        return new Read(glossary, json, features);
    }

    private static FeatureGlossary FromJson(string glossaryPath)
    {
        if (!File.Exists(glossaryPath))
            throw new FeatureMachinesException(
                Directory.Exists(glossaryPath)
                    ? $"--glossary is a directory, {glossaryPath}: give the glossary's JSON file."
                    : $"There is no glossary at {glossaryPath}."
            );
        try
        {
            return FeatureGlossary.Read(File.ReadAllText(glossaryPath));
        }
        catch (FormatException ex)
        {
            throw new FeatureMachinesException($"{glossaryPath}: {ex.Message}");
        }
    }

    private static (FeatureGlossary Glossary, string Json) FromLexicon(
        string featuresDir,
        IReadOnlyList<string> trains
    )
    {
        var beside = Path.Combine(featuresDir, GlossaryFileName);
        var lexicons = Directory.GetFiles(featuresDir, "*" + Lexicon.Extension);
        if (lexicons.Length == 0)
            throw new FeatureMachinesException(
                File.Exists(beside)
                    ? $"{featuresDir} holds {GlossaryFileName} and no lexicon. Write the lexicon (*{Lexicon.Extension}) "
                        + $"it elaborates to, or read the JSON as it is with --glossary {beside}."
                    : $"{featuresDir} holds no lexicon (*{Lexicon.Extension}) to say what its features' words mean."
            );
        if (File.Exists(beside))
            throw new FeatureMachinesException(
                $"{featuresDir} holds a lexicon and {GlossaryFileName}. The glossary is derived from the lexicon and "
                    + $"written to --out by from-gherkin, so delete {beside}."
            );
        List<System.Reflection.Assembly> assemblies;
        try
        {
            assemblies = trains.Select(MachineLoader.LoadAssembly).ToList();
        }
        catch (InvalidOperationException ex)
        {
            throw new FeatureMachinesException($"--trains: {ex.Message}");
        }
        var reading =
            trains.Count == 0
                ? Lexicon.ReadDirectory(featuresDir)!
                : Lexicon.ReadDirectory(featuresDir, assemblies)!;
        if (!reading.Succeeded)
            throw new FeatureMachinesException(
                string.Join(Environment.NewLine, reading.Problems.Select(p => p.ToString()))
                    + Environment.NewLine
                    + $"{reading.Problems.Count} problem(s) in the lexicon."
            );
        return (reading.Glossary!, reading.Json!);
    }

    private static Result Derived(Derived derived, Read read)
    {
        var uiSteps = derived.UiSteps.Select(p => p.ToString()).ToList();
        var warnings = derived.Warnings.Select(p => read.Glossary.Locate(p).ToString()).ToList();
        if (derived.Problems.Count > 0)
            return new Result(
                new Dictionary<string, string>(),
                derived.Problems.Select(p => read.Glossary.Locate(p).ToString()).ToList(),
                uiSteps,
                warnings
            );

        var files = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var (id, ir) in derived.Irs)
            files[id + IrSuffix] = ir + "\n";
        files[HostFileName] = derived.Host! + "\n";
        files[AccessFileName] = derived.Access!;
        if (read.Json is { } json)
            files[GlossaryFileName] = json;
        return new Result(files, [], uiSteps, warnings);
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
        // A file that differs from a derived one only in case goes first. On a file system that ignores case it is the
        // derived file under another spelling, which writing would keep and the stale pass would then delete; on one
        // that keeps case it is stale anyway.
        foreach (
            var variant in existing
                .Where(n =>
                    !files.ContainsKey(n)
                    && files.Keys.Any(k => string.Equals(k, n, StringComparison.OrdinalIgnoreCase))
                )
                .ToList()
        )
        {
            File.Delete(Path.Combine(outDir, variant));
            existing.Remove(variant);
            changes.Add($"removed {variant}");
        }
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
                    + $"{HostFileName}, {AccessFileName} and {GlossaryFileName}."
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
        name is HostFileName or AccessFileName or GlossaryFileName
        || (name.EndsWith(IrSuffix, StringComparison.Ordinal) && name.Length > IrSuffix.Length);
}

/// <summary>A path or input <see cref="FeatureDirectory"/> cannot use, worded for the terminal.</summary>
internal sealed class FeatureMachinesException(string message) : Exception(message);
