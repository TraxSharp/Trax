using System.Diagnostics;
using Trax.Cli.Models;

namespace Trax.Cli.Generator;

public partial class TraxProjectGenerator
{
    private readonly CodeRenderer _renderer = new();
    private readonly Action<string, string> _scaffoldHub;

    public TraxProjectGenerator()
        : this(RunDotnetNew) { }

    /// <summary>Takes the step that scaffolds the hub (name, directory), so a test can make it fail.</summary>
    internal TraxProjectGenerator(Action<string, string> scaffoldHub) => _scaffoldHub = scaffoldHub;

    public void Generate(ApiSchema schema, string outputDir, string projectName, bool force)
    {
        // Before the output directory is touched, so a refused schema deletes nothing.
        SchemaNames.Validate(schema, projectName);

        outputDir = Path.GetFullPath(outputDir);
        var replacing = Directory.Exists(outputDir);
        if (replacing)
        {
            if (!force)
                throw new InvalidOperationException(
                    $"Output directory already exists: {outputDir}. Use --force to overwrite."
                );
            EnsureReplaceable(outputDir, Directory.GetCurrentDirectory());
        }

        // Build the project in a sibling directory and swap it in only when every step has succeeded, so a
        // failure (most often `dotnet new`) leaves the existing directory as it was. A sibling keeps the swap
        // a rename on one volume.
        var parent = Path.GetDirectoryName(outputDir)!;
        Directory.CreateDirectory(parent);
        var staging = Path.Combine(
            parent,
            $".{Path.GetFileName(outputDir)}.trax-generate-{Guid.NewGuid():N}"
        );
        try
        {
            Directory.CreateDirectory(staging);
            GenerateInto(schema, staging, projectName);
        }
        catch
        {
            TryDeleteDirectory(staging);
            throw;
        }

        if (!replacing)
        {
            Directory.Move(staging, outputDir);
            return;
        }

        var previous = Path.Combine(
            parent,
            $".{Path.GetFileName(outputDir)}.trax-previous-{Guid.NewGuid():N}"
        );
        Directory.Move(outputDir, previous);
        try
        {
            Directory.Move(staging, outputDir);
        }
        catch
        {
            Directory.Move(previous, outputDir);
            TryDeleteDirectory(staging);
            throw;
        }
        TryDeleteDirectory(previous);
    }

    private void GenerateInto(ApiSchema schema, string outputDir, string projectName)
    {
        // 1. Scaffold the hub project via dotnet new
        var hubProjectName = $"{projectName}.Hub";
        var hubDir = Path.Combine(outputDir, hubProjectName);
        _scaffoldHub(hubProjectName, hubDir);

        // 2. Create the trains library
        var trainsProjectName = $"{projectName}.Trains";
        var trainsDir = Path.Combine(outputDir, trainsProjectName);
        GenerateTrainsLibrary(
            schema,
            trainsDir,
            projectName,
            HubPackageVersions(hubDir, hubProjectName)
        );

        // 3. Add ProjectReference from hub to trains library
        AddProjectReference(hubDir, hubProjectName, trainsProjectName);

        // 4. Patch hub Program.cs to scan the trains assembly
        PatchProgramCs(hubDir, projectName);
    }

    /// <summary>
    /// Refuses a <c>--force</c> target that replacing would do real damage to: the current directory or one of
    /// its parents (<c>--output .</c>), and any directory holding a git repository or worktree, at any depth.
    /// </summary>
    internal static void EnsureReplaceable(string outputDir, string currentDirectory)
    {
        var target = TrimSeparators(Path.GetFullPath(outputDir));
        var current = TrimSeparators(Path.GetFullPath(currentDirectory));
        var comparison = OperatingSystem.IsLinux()
            ? StringComparison.Ordinal
            : StringComparison.OrdinalIgnoreCase;

        if (
            string.Equals(target, current, comparison)
            || current.StartsWith(target + Path.DirectorySeparatorChar, comparison)
            || Path.GetPathRoot(target) == target + Path.DirectorySeparatorChar
            || Path.GetPathRoot(target) == target
        )
            throw new InvalidOperationException(
                $"Refusing to replace {outputDir}: it is the current directory or one of its parents. "
                    + "Choose a new output directory."
            );

        if (ContainsGitRepository(target))
            throw new InvalidOperationException(
                $"Refusing to replace {outputDir}: it holds a git repository (.git) at some depth. "
                    + "Choose a new output directory."
            );
    }

    // A .git directory or a worktree's .git file, in the target itself or anywhere below it. Dot-files carry the
    // Hidden attribute on Unix, which the default enumeration skips, so nothing is skipped except symlinks: they
    // are not followed, and deleting the directory removes a link, not what it points at.
    private static bool ContainsGitRepository(string target) =>
        Directory
            .EnumerateFileSystemEntries(
                target,
                ".git",
                new EnumerationOptions
                {
                    RecurseSubdirectories = true,
                    IgnoreInaccessible = true,
                    AttributesToSkip = FileAttributes.ReparsePoint,
                    MatchCasing = MatchCasing.CaseSensitive,
                }
            )
            .Any();

    private static string TrimSeparators(string path) =>
        path.Length > 1
            ? path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            : path;

    private static void TryDeleteDirectory(string dir)
    {
        try
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
        catch (IOException)
        {
            // Best effort: a leftover staging directory is visible, named, and harmless.
        }
        catch (UnauthorizedAccessException) { }
    }

    /// <summary>
    /// The version the hub resolves each Trax package the trains library references at: the pins the template
    /// ships in the hub's <c>Directory.Packages.props</c>, else a <c>Version</c> on the hub's own
    /// <c>PackageReference</c>. The trains library sits beside the hub, outside that file's reach, so it gets
    /// the same versions written into its csproj; a float there would let the pair resolve different Trax
    /// releases. A hub that pins none of them is refused rather than floated.
    /// </summary>
    internal static IReadOnlyDictionary<string, string> HubPackageVersions(
        string hubDir,
        string hubProjectName
    )
    {
        var versions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        ReadVersions(Path.Combine(hubDir, "Directory.Packages.props"), "PackageVersion", versions);
        ReadVersions(
            Path.Combine(hubDir, $"{hubProjectName}.csproj"),
            "PackageReference",
            versions
        );

        var missing = CodeRenderer.TrainsPackages.Where(p => !versions.ContainsKey(p)).ToList();
        if (missing.Count > 0)
            throw new InvalidOperationException(
                $"The hub scaffolded at {hubDir} pins no version for {string.Join(", ", missing)}, so the "
                    + "trains library cannot reference the same Trax release. The installed trax-hub template "
                    + "does not match this version of trax: run dotnet new install Trax.Samples.Templates."
            );
        // The version is written into the trains csproj as an attribute value, so anything that is not a
        // NuGet version or range (a quote, say) would break that file or add to it.
        var malformed = CodeRenderer
            .TrainsPackages.Where(p => !IsNuGetVersion(versions[p]))
            .ToList();
        if (malformed.Count > 0)
            throw new InvalidOperationException(
                $"The hub scaffolded at {hubDir} pins "
                    + string.Join(", ", malformed.Select(p => $"{p} at '{versions[p]}'"))
                    + ", which is not a NuGet version or version range, so it cannot be written into the "
                    + "trains library's csproj. Fix the pin in the hub."
            );
        return CodeRenderer.TrainsPackages.ToDictionary(p => p, p => versions[p]);
    }

    /// <summary>A NuGet version (<c>1.2.3</c>, <c>1.2.3-beta.1</c>, a float such as <c>1.*</c>) or a range (<c>[1.0,2.0)</c>).</summary>
    internal static bool IsNuGetVersion(string version) => NuGetVersion().IsMatch(version.Trim());

    private const string VersionPattern =
        @"(\*|\d+(\.(\d+|\*)){0,3}(-[0-9A-Za-z.*-]+)?(\+[0-9A-Za-z.-]+)?)";

    [System.Text.RegularExpressions.GeneratedRegex(
        "^("
            + VersionPattern
            + @"|[\[(]\s*"
            + VersionPattern
            + @"?\s*(,\s*"
            + VersionPattern
            + @"?\s*)?[\])])\z"
    )]
    private static partial System.Text.RegularExpressions.Regex NuGetVersion();

    private static void ReadVersions(
        string path,
        string element,
        Dictionary<string, string> versions
    )
    {
        if (!File.Exists(path))
            return;
        System.Xml.Linq.XDocument document;
        try
        {
            document = System.Xml.Linq.XDocument.Load(path);
        }
        catch (Exception ex)
            when (ex is System.Xml.XmlException or IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException($"Cannot read {path}: {ex.Message}", ex);
        }
        foreach (var item in document.Descendants().Where(e => e.Name.LocalName == element))
        {
            var include = (string?)item.Attribute("Include");
            var version = (string?)item.Attribute("Version");
            if (!string.IsNullOrWhiteSpace(include) && !string.IsNullOrWhiteSpace(version))
                versions.TryAdd(include, version);
        }
    }

    internal void GenerateTrainsLibrary(
        ApiSchema schema,
        string trainsDir,
        string projectName,
        IReadOnlyDictionary<string, string>? packageVersions = null
    )
    {
        SchemaNames.Validate(schema, projectName);

        var trainsProjectName = $"{projectName}.Trains";
        Directory.CreateDirectory(trainsDir);

        // If the schema has shared types or enums, set the models namespace
        // so generated code includes the correct using directive
        var hasSharedTypes =
            schema.Types.Any(t => !t.IsBuiltIn && t.Fields.Count > 0) || schema.Enums.Count > 0;
        if (hasSharedTypes)
        {
            _renderer.SetModelsNamespace($"{projectName}.Trains.Models");
            _renderer.SetModelNames(
                schema
                    .Types.Where(t => !t.IsBuiltIn && t.Fields.Count > 0)
                    .Select(t => t.Name)
                    .Concat(schema.Enums.Select(e => e.Name))
            );
        }

        // Write trains csproj
        WriteFile(
            Path.Combine(trainsDir, $"{trainsProjectName}.csproj"),
            packageVersions is null
                ? _renderer.RenderTrainsCsproj()
                : _renderer.RenderTrainsCsproj(packageVersions)
        );

        // Write ManifestNames.cs
        WriteFile(
            Path.Combine(trainsDir, "ManifestNames.cs"),
            _renderer.RenderManifestNames(schema.Operations, projectName)
        );

        // Write GraphQLNamespaces.cs
        var groups = schema
            .Operations.Select(o => o.Group)
            .Where(g => g != null)
            .Cast<string>()
            .Distinct()
            .OrderBy(g => g)
            .ToList();

        if (groups.Count > 0)
        {
            WriteFile(
                Path.Combine(trainsDir, "GraphQLNamespaces.cs"),
                _renderer.RenderGraphQLNamespaces(groups, projectName)
            );
        }

        // Write shared types in Models/
        foreach (var apiType in schema.Types)
        {
            if (apiType.IsBuiltIn || apiType.Fields.Count == 0)
                continue;

            var modelsDir = Path.Combine(trainsDir, "Models");
            Directory.CreateDirectory(modelsDir);
            WriteFile(
                Path.Combine(modelsDir, $"{apiType.Name}.cs"),
                _renderer.RenderTypeRecord(apiType, projectName, null)
            );
        }

        // Write enums in Models/
        foreach (var apiEnum in schema.Enums)
        {
            var modelsDir = Path.Combine(trainsDir, "Models");
            Directory.CreateDirectory(modelsDir);
            WriteFile(
                Path.Combine(modelsDir, $"{apiEnum.Name}.cs"),
                _renderer.RenderEnum(apiEnum, projectName)
            );
        }

        // Write trains
        foreach (var operation in schema.Operations)
        {
            var group = operation.Group ?? "General";
            var trainDir = Path.Combine(trainsDir, "Trains", group, operation.Name);
            var junctionDir = Path.Combine(trainDir, "Junctions");
            Directory.CreateDirectory(junctionDir);

            WriteFile(
                Path.Combine(trainDir, $"I{operation.Name}Train.cs"),
                _renderer.RenderTrainInterface(operation, projectName)
            );

            WriteFile(
                Path.Combine(trainDir, $"{operation.Name}Train.cs"),
                _renderer.RenderTrainImplementation(operation, projectName)
            );

            WriteFile(
                Path.Combine(trainDir, $"{operation.InputType.Name}.cs"),
                _renderer.RenderInput(operation, projectName)
            );

            if (!operation.OutputType.IsBuiltIn && operation.OutputType.Fields.Count > 0)
            {
                WriteFile(
                    Path.Combine(trainDir, $"{operation.OutputType.Name}.cs"),
                    _renderer.RenderOutput(operation, projectName)
                );
            }

            WriteFile(
                Path.Combine(junctionDir, $"{operation.Name}Junction.cs"),
                _renderer.RenderJunction(operation, projectName)
            );
        }
    }

    internal static void AddProjectReference(
        string apiDir,
        string apiProjectName,
        string trainsProjectName
    )
    {
        var csprojPath = Path.Combine(apiDir, $"{apiProjectName}.csproj");
        var content = File.ReadAllText(csprojPath);

        content = content.Replace(
            "</Project>",
            $"""

              <ItemGroup>
                <ProjectReference Include="..\{trainsProjectName}\{trainsProjectName}.csproj" />
              </ItemGroup>
            </Project>
            """
        );

        File.WriteAllText(csprojPath, content);
    }

    /// <summary>
    /// Adds the trains assembly to the hub's <c>AddMediator(typeof(Program).Assembly)</c> scan. The name is
    /// written fully qualified, so no using directive is inserted: finding where one may go needs a parse, and
    /// the template's last <c>using</c> is a statement inside a block. A template that no longer has the scan
    /// is refused, since the hub would otherwise build and register none of the generated trains.
    /// </summary>
    internal static void PatchProgramCs(string hubDir, string projectName)
    {
        const string scan = "typeof(Program).Assembly";
        var programPath = Path.Combine(hubDir, "Program.cs");
        if (!File.Exists(programPath))
            throw new InvalidOperationException(
                $"The hub template produced no Program.cs at {programPath}, so the generated trains cannot be "
                    + "registered. The installed trax-hub template does not match this version of trax."
            );

        var content = File.ReadAllText(programPath);
        if (!content.Contains(scan, StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"The hub's Program.cs ({programPath}) has no '{scan}' to add the trains assembly to, so the "
                    + "generated trains would not be registered. The installed trax-hub template does not match "
                    + "this version of trax."
            );

        content = content.Replace(
            scan,
            $"{scan}, typeof({projectName}.Trains.ManifestNames).Assembly",
            StringComparison.Ordinal
        );

        File.WriteAllText(programPath, content);
    }

    private static void RunDotnetNew(string name, string outputDir)
    {
        var (exitCode, _, stderr) = ChildProcess.Run(
            new ProcessStartInfo
            {
                FileName = "dotnet",
                ArgumentList = { "new", "trax-hub", "-n", name, "-o", outputDir },
            }
        );

        if (exitCode != 0)
            throw new InvalidOperationException(DotnetNewFailure(exitCode, stderr));
    }

    /// <summary>The message for a failed <c>dotnet new trax-hub</c>.</summary>
    internal static string DotnetNewFailure(int exitCode, string stderr) =>
        stderr.Contains("No templates or subcommands found", StringComparison.Ordinal)
            ? "The 'trax-hub' template is not installed. Run: dotnet new install Trax.Samples.Templates"
            : $"dotnet new failed (exit code {exitCode}): {stderr}";

    public static bool IsDotnetAvailable()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "dotnet",
                ArgumentList = { "--version" },
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            using var process = Process.Start(psi);
            process?.WaitForExit();
            return process?.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private static void WriteFile(string path, string content)
    {
        var dir = Path.GetDirectoryName(path);
        if (dir != null)
            Directory.CreateDirectory(dir);

        File.WriteAllText(path, content);
    }
}
