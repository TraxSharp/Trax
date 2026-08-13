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
internal sealed class MachineGenerator
{
    private readonly INodeRunner _node;

    public MachineGenerator(INodeRunner node) => _node = node;

    /// <summary>Produce every requested artifact and place it at its output root. Atomic against a step failure.</summary>
    public GenerateResult Generate(MachineGenerateOptions options)
    {
        var staging = CreateStagingDir();
        try
        {
            var written = new List<GeneratedArtifact>();
            foreach (var artifact in Produce(options, staging))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(artifact.FinalPath)!);
                File.Copy(artifact.StagedPath, artifact.FinalPath, overwrite: true);
                written.Add(new GeneratedArtifact(artifact.Kind, artifact.FinalPath));
            }
            return new GenerateResult(written);
        }
        finally
        {
            TryDelete(staging);
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

        var id = o.Machine.Name;
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
            RunNode(ToolScript(o, "generate-twin.mjs"), args, "the twin");
            planned.Add(
                new(
                    "contexts",
                    Path.Combine(twinStage, $"{id}.contexts.g.ts"),
                    Path.Combine(o.TwinOut, $"{id}.contexts.g.ts")
                )
            );
            planned.Add(
                new(
                    "machine",
                    Path.Combine(twinStage, $"{id}.machine.g.ts"),
                    Path.Combine(o.TwinOut, $"{id}.machine.g.ts")
                )
            );
        }

        if (o.CorpusOut is not null)
        {
            RequireEngine(o.EngineSrc, "the corpus");
            var corpusStage = Path.Combine(staging, "differential.json");
            RunNode(
                ToolScript(o, "generate-corpus.mjs"),
                ["--ir", stagedIr, "--engine-src", o.EngineSrc!, "--out", corpusStage],
                "the corpus"
            );
            planned.Add(new("corpus", corpusStage, Path.Combine(o.CorpusOut, "differential.json")));
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
