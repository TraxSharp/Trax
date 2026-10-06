using Trax.Effect.StateMachine.Persistence;

namespace Trax.Cli.Machines;

/// <summary>What to generate, and where. Each artifact has its own output root because a real consumer splits
/// them across trees (the IR and corpus in a shared machines dir, the twin next to the frontend).</summary>
internal sealed record MachineGenerateOptions(
    IMachine Machine,
    string? IrOut = null,
    string? TwinOut = null,
    string? CorpusOut = null,
    string? EngineSrc = null,
    string ImportStyle = "relative",
    string? Specifier = null,
    string? ToolsDir = null
);

/// <summary>An artifact written by <c>generate</c>: its kind and final path.</summary>
internal sealed record GeneratedArtifact(string Kind, string Path);

/// <summary>The outcome of <c>generate</c>: every file placed on disk.</summary>
internal sealed record GenerateResult(IReadOnlyList<GeneratedArtifact> Written);

internal enum DriftStatus
{
    UpToDate,
    Drifted,
    Missing,
}

/// <summary>The drift verdict for one artifact under <c>check</c>.</summary>
internal sealed record ArtifactCheck(string Kind, string Path, DriftStatus Status);

/// <summary>The outcome of <c>check</c>: per-artifact drift; clean iff every artifact is up to date.</summary>
internal sealed record CheckResult(IReadOnlyList<ArtifactCheck> Checks)
{
    public bool IsClean => Checks.All(c => c.Status == DriftStatus.UpToDate);
}

/// <summary>
/// Orchestrates <c>trax machine generate</c> / <c>check</c>. The IR is exported in-process (C#); the twin and
/// corpus are produced by shelling out to the node entrypoints against the engine's <c>src</c>. Everything is
/// generated into a staging directory first, so a failing step leaves the target tree untouched; only when all
/// requested artifacts are produced does <c>generate</c> place them, and <c>check</c> diffs them instead.
/// </summary>
internal sealed partial class MachineGenerator
{
    private readonly INodeRunner _node;
    private readonly PlacementFiles _files;

    public MachineGenerator(INodeRunner node)
        : this(node, new PlacementFiles()) { }

    internal MachineGenerator(INodeRunner node, PlacementFiles files)
    {
        _node = node;
        _files = files;
    }

    /// <summary>Produce every requested artifact and place it at its output root. Every artifact is replaced or none is.</summary>
    public GenerateResult Generate(MachineGenerateOptions options)
    {
        var staging = CreateStagingDir();
        try
        {
            var planned = Produce(options, staging);
            Place(planned);
            return new GenerateResult(
                planned.Select(a => new GeneratedArtifact(a.Kind, a.FinalPath)).ToList()
            );
        }
        finally
        {
            TryDelete(staging);
        }
    }

    /// <summary>
    /// Replace every artifact or none. Each staged file is first copied beside its target under a temporary
    /// name, which is where a missing or unwritable output root fails; only then are the targets swapped in,
    /// each one's previous content kept aside until all have been, so a failure part-way puts back the ones
    /// already swapped and removes the directories it created. A failure is reported as an
    /// <see cref="InvalidOperationException"/> naming the path; when something could not be put back, the
    /// message names where each original was left instead of claiming nothing changed.
    /// </summary>
    private void Place(IReadOnlyList<PlannedArtifact> planned)
    {
        var token = Guid.NewGuid().ToString("N");
        var copies = new List<(PlannedArtifact Artifact, string Temp)>();
        var swapped = new List<(string Final, string? Backup)>();
        var createdDirs = new List<string>();
        string current = "";
        try
        {
            foreach (var artifact in planned)
            {
                current = artifact.FinalPath;
                CreateDirectory(Path.GetDirectoryName(artifact.FinalPath)!, createdDirs);
                var temp = $"{artifact.FinalPath}.{token}.tmp";
                copies.Add((artifact, temp));
                _files.Copy(artifact.StagedPath, temp);
            }

            foreach (var (artifact, temp) in copies)
            {
                current = artifact.FinalPath;
                string? backup = null;
                if (File.Exists(artifact.FinalPath))
                {
                    backup = $"{artifact.FinalPath}.{token}.bak";
                    _files.Move(artifact.FinalPath, backup);
                }
                swapped.Add((artifact.FinalPath, backup));
                _files.Move(temp, artifact.FinalPath);
            }
        }
        catch (Exception ex)
        {
            var leftBehind = new List<string>();
            for (var i = swapped.Count - 1; i >= 0; i--)
            {
                var (final, backup) = swapped[i];
                // The new file, if it got there; the original is still at its backup path.
                var cleared = TryRun(() => _files.Delete(final));
                if (backup is null)
                    continue;
                if (!cleared || !TryRun(() => _files.Move(backup, final)))
                    leftBehind.Add($"{final} is at {backup}");
            }
            foreach (var (_, temp) in copies)
                if (!TryRun(() => _files.Delete(temp)))
                    leftBehind.Add($"{temp} could not be removed");
            for (var i = createdDirs.Count - 1; i >= 0; i--)
                TryRun(() => _files.DeleteEmptyDirectory(createdDirs[i]));

            throw new InvalidOperationException(
                $"Could not write {current}: {ex.Message} "
                    + (
                        leftBehind.Count == 0
                            ? "No artifact was changed."
                            : "Putting the previous artifacts back failed too, so move them back by hand: "
                                + string.Join("; ", leftBehind)
                                + "."
                    ),
                ex
            );
        }

        foreach (var (_, backup) in swapped)
            if (backup is not null)
                TryRun(() => _files.Delete(backup));
    }

    /// <summary>Creates <paramref name="dir"/> and records, outermost first, each directory that did not exist.</summary>
    private void CreateDirectory(string dir, List<string> created)
    {
        var missing = new Stack<string>();
        for (
            var probe = Path.GetFullPath(dir);
            !string.IsNullOrEmpty(probe) && !Directory.Exists(probe);
            probe = Path.GetDirectoryName(probe)
        )
            missing.Push(probe);

        _files.CreateDirectory(dir);
        foreach (var path in missing)
            if (!created.Contains(path))
                created.Add(path);
    }

    /// <summary>Runs a rollback step; true when it succeeded. Nothing it throws replaces the original failure.</summary>
    private static bool TryRun(Action action)
    {
        try
        {
            action();
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>Regenerate to staging and diff against what is committed; never writes to the output roots.</summary>
    public CheckResult Check(MachineGenerateOptions options)
    {
        var staging = CreateStagingDir();
        try
        {
            var checks = Produce(options, staging)
                .Select(a =>
                {
                    if (!File.Exists(a.FinalPath))
                        return new ArtifactCheck(a.Kind, a.FinalPath, DriftStatus.Missing);
                    var status = FilesEqual(a.StagedPath, a.FinalPath)
                        ? DriftStatus.UpToDate
                        : DriftStatus.Drifted;
                    return new ArtifactCheck(a.Kind, a.FinalPath, status);
                })
                .ToList();
            return new CheckResult(checks);
        }
        finally
        {
            TryDelete(staging);
        }
    }

    // Generate every requested artifact into the staging directory and return each one's staged + final path.
    // The IR is always produced (it feeds the node steps) but only planned for placement when --ir-out is set.
    private IReadOnlyList<PlannedArtifact> Produce(MachineGenerateOptions o, string staging)
    {
        if (o.IrOut is null && o.TwinOut is null && o.CorpusOut is null)
            throw new InvalidOperationException(
                "Nothing to generate: pass at least one of --ir-out, --twin-out, --corpus-out."
            );

        string id;
        try
        {
            id = o.Machine.Name;
        }
        catch (ArgumentException ex)
        {
            // The engine refuses a malformed Id(...) when the machine is built, which reading the name
            // triggers. Report it as the same refusal as the check below rather than a stack trace.
            throw new InvalidOperationException(ex.Message, ex);
        }
        // The id names every file below; refuse it before anything is written (cli/0005).
        if (!IsMachineId(id))
            throw new InvalidOperationException(
                $"The machine id '{id}' is not kebab-case (lowercase letters and digits in words joined by "
                    + "'-', starting with a letter, e.g. 'write-to-congress'). It names the generated files, "
                    + "so change the machine's Id(...)."
            );
        var planned = new List<PlannedArtifact>();

        var stagedIr = Path.Combine(staging, $"{id}.ir.json");
        File.WriteAllText(stagedIr, o.Machine.ExportIr() + "\n");
        if (o.IrOut is not null)
            planned.Add(new("ir", stagedIr, Path.Combine(o.IrOut, $"{id}.ir.json")));

        if (o.TwinOut is not null)
        {
            RequireEngine(o.EngineSrc, "the twin");
            var twinStage = Path.Combine(staging, "twin");
            Directory.CreateDirectory(twinStage);
            var args = new List<string>
            {
                "--ir",
                stagedIr,
                "--engine-src",
                o.EngineSrc!,
                "--out-dir",
                twinStage,
                "--import-style",
                o.ImportStyle,
            };
            if (o.Specifier is not null)
            {
                args.Add("--specifier");
                args.Add(o.Specifier);
            }
            var twinScript = ToolScript(o, "generate-twin.mjs");
            RunNode(twinScript, args, "the twin");
            PlannedArtifact[] twin =
            [
                new(
                    "contexts",
                    Path.Combine(twinStage, $"{id}.contexts.g.ts"),
                    Path.Combine(o.TwinOut, $"{id}.contexts.g.ts")
                ),
                new(
                    "machine",
                    Path.Combine(twinStage, $"{id}.machine.g.ts"),
                    Path.Combine(o.TwinOut, $"{id}.machine.g.ts")
                ),
            ];
            RequireProduced(twin, twinScript);
            planned.AddRange(twin);
        }

        if (o.CorpusOut is not null)
        {
            RequireEngine(o.EngineSrc, "the corpus");
            var corpusStage = Path.Combine(staging, "differential.json");
            var corpusScript = ToolScript(o, "generate-corpus.mjs");
            RunNode(
                corpusScript,
                ["--ir", stagedIr, "--engine-src", o.EngineSrc!, "--out", corpusStage],
                "the corpus"
            );
            PlannedArtifact corpus = new(
                "corpus",
                corpusStage,
                Path.Combine(o.CorpusOut, "differential.json")
            );
            RequireProduced([corpus], corpusScript);
            planned.Add(corpus);
        }

        return planned;
    }

    private void RunNode(string script, IReadOnlyList<string> args, string what)
    {
        if (!_node.IsAvailable())
            throw new InvalidOperationException(
                $"Generating {what} needs node on PATH. Install Node.js (>= 22), or generate --ir-out only."
            );
        if (!File.Exists(script))
            throw new InvalidOperationException(
                $"Codegen entrypoint not found: {script}. Pass --tools-dir if the engine's tools/ directory "
                    + "is not a sibling of --engine-src."
            );

        var result = _node.Run(script, args);
        if (result.ExitCode != 0)
            throw new InvalidOperationException(
                $"node {Path.GetFileName(script)} failed (exit {result.ExitCode}): {result.StdErr.Trim()}"
            );
    }

    // A node step that exits 0 without writing a file the CLI expects (an engine whose generator names its
    // output differently) is refused here, before anything is placed or compared.
    private static void RequireProduced(IEnumerable<PlannedArtifact> artifacts, string script)
    {
        var missing = artifacts.Where(a => !File.Exists(a.StagedPath)).ToList();
        if (missing.Count > 0)
            throw new InvalidOperationException(
                $"node {Path.GetFileName(script)} exited 0 but did not write "
                    + string.Join(", ", missing.Select(a => Path.GetFileName(a.StagedPath)))
                    + ". The engine at --engine-src may not match this version of trax; nothing was written."
            );
    }

    private static void RequireEngine(string? engineSrc, string what)
    {
        if (engineSrc is null)
            throw new InvalidOperationException(
                $"Generating {what} needs --engine-src (the TypeScript engine's src/ directory)."
            );
    }

    private static string ToolScript(MachineGenerateOptions o, string script) =>
        Path.Combine(o.ToolsDir ?? DefaultToolsDir(o.EngineSrc!), script);

    /// <summary>The engine's <c>tools/</c> directory, a sibling of its <c>src/</c> (<c>--engine-src</c>).</summary>
    internal static string DefaultToolsDir(string engineSrc)
    {
        var trimmed = engineSrc.TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar
        );
        var parent =
            Directory.GetParent(Path.GetFullPath(trimmed))
            ?? throw new InvalidOperationException(
                $"--engine-src has no parent directory to find tools/ under: {engineSrc}"
            );
        return Path.Combine(parent.FullName, "tools");
    }

    /// <summary>
    /// A machine id is kebab-case: lowercase ASCII words of letters and digits joined by single
    /// hyphens, starting with a letter (<c>checkout</c>, <c>write-to-congress</c>). It names every
    /// artifact file, so nothing else may reach a path.
    /// </summary>
    internal static bool IsMachineId(string id) => MachineId().IsMatch(id);

    [System.Text.RegularExpressions.GeneratedRegex(@"^[a-z][a-z0-9]*(-[a-z0-9]+)*\z")]
    private static partial System.Text.RegularExpressions.Regex MachineId();

    private static bool FilesEqual(string a, string b) =>
        File.ReadAllBytes(a).AsSpan().SequenceEqual(File.ReadAllBytes(b));

    private static string CreateStagingDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "trax-machine-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void TryDelete(string dir)
    {
        try
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
        catch (IOException)
        {
            // Best-effort cleanup of a temp dir; a leftover under the OS temp path is harmless.
        }
    }

    private sealed record PlannedArtifact(string Kind, string StagedPath, string FinalPath);
}

/// <summary>The file operations <c>generate</c> places artifacts with; a seam so a failure part-way can be tested.</summary>
internal class PlacementFiles
{
    public virtual void Copy(string source, string destination) =>
        File.Copy(source, destination, overwrite: true);

    public virtual void Move(string source, string destination) => File.Move(source, destination);

    public virtual void Delete(string path) => File.Delete(path);

    public virtual void CreateDirectory(string path) => Directory.CreateDirectory(path);

    /// <summary>Removes <paramref name="path"/> only when it is empty.</summary>
    public virtual void DeleteEmptyDirectory(string path)
    {
        if (Directory.Exists(path) && !Directory.EnumerateFileSystemEntries(path).Any())
            Directory.Delete(path);
    }
}
