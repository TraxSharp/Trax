using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Trax.Core.Testing;
using Trax.Core.Testing.Infrastructure;

namespace Trax.Effect.Data.Testing;

/// <summary>
/// Architecture-guard checkers for the data layer. Source-scanning guards verify the shape of domain
/// contexts on disk; the reflection guard verifies the EF model. Each returns a
/// <see cref="GuardResult"/> the consumer asserts on with its own test framework.
/// </summary>
public static class DataLayerGuards
{
    private const string DefaultBaseTypeName = "DomainDataContext";

    private static readonly Regex ContextClass = new(
        @"\bclass\s+(\w+DbContext)\b",
        RegexOptions.Compiled
    );

    /// <summary>
    /// Every domain <c>*DbContext</c> under the source scan roots must derive the shared base
    /// (<c>DomainDataContext&lt;TSelf&gt;</c>), which enforces one-project-one-schema-one-context.
    /// </summary>
    public static GuardResult DomainContextsDeriveBase(
        ArchitectureGuardOptions options,
        string baseTypeName = DefaultBaseTypeName,
        IReadOnlySet<string>? knownExceptions = null
    )
    {
        ArgumentNullException.ThrowIfNull(options);
        knownExceptions ??= new HashSet<string>(StringComparer.Ordinal);

        var root = options.RepoRootOverride ?? RepoRoot.Path;
        var inheritsBase = new Regex($@":\s*{Regex.Escape(baseTypeName)}<", RegexOptions.Compiled);
        var offenders = new List<string>();
        var inspected = 0;

        foreach (var file in SourceFiles.CSharpUnder(root, [.. options.SourceScanRoots]))
        {
            var stripped = SourceText.StripCommentsAndStrings(File.ReadAllText(file));
            if (!ContextClass.IsMatch(stripped))
                continue;

            inspected++;
            var rel = Rel(root, file);
            if (knownExceptions.Contains(rel))
                continue;

            if (!inheritsBase.IsMatch(stripped))
                offenders.Add(rel);
        }

        var message =
            $"Every domain *DbContext must derive {baseTypeName}<TSelf> (one project : one schema : "
            + "one context). Add the base, implement Schema and ConfigureModel(...). If a context "
            + "legitimately cannot, pass it in knownExceptions with a justification. Offenders:\n  "
            + string.Join("\n  ", offenders);

        return new GuardResult(offenders, inspected, message);
    }

    /// <summary>
    /// Every context deriving the shared base must ship a companion <c>I{Name}</c> interface in the
    /// same directory (application code depends on the interface, not the concrete context).
    /// </summary>
    public static GuardResult CompanionInterfaces(
        ArchitectureGuardOptions options,
        string baseTypeName = DefaultBaseTypeName
    )
    {
        ArgumentNullException.ThrowIfNull(options);
        var root = options.RepoRootOverride ?? RepoRoot.Path;
        var baseContextClass = new Regex(
            $@"\bclass\s+(\w+DbContext)\s*\([^)]*\)\s*:\s*{Regex.Escape(baseTypeName)}<",
            RegexOptions.Compiled | RegexOptions.Singleline
        );
        var offenders = new List<string>();
        var inspected = 0;

        foreach (var file in SourceFiles.CSharpUnder(root, [.. options.SourceScanRoots]))
        {
            var stripped = SourceText.StripCommentsAndStrings(File.ReadAllText(file));
            var match = baseContextClass.Match(stripped);
            if (!match.Success)
                continue;

            inspected++;
            var contextName = match.Groups[1].Value;
            var companion = Path.Combine(Path.GetDirectoryName(file)!, $"I{contextName}.cs");
            if (!File.Exists(companion))
                offenders.Add($"{Rel(root, file)} (expected I{contextName}.cs alongside it)");
        }

        var message =
            "Every shared-base context needs a companion I{Name} interface in the same directory, "
            + "declaring its DbSets (and cross-schema reads). Offenders:\n  "
            + string.Join("\n  ", offenders);

        return new GuardResult(offenders, inspected, message);
    }

    /// <summary>
    /// Each domain context owns a distinct, non-null default schema (the schema half of 1:1:1). The
    /// model is built offline against PostgreSQL (no database connection) to read the applied schema.
    /// </summary>
    public static GuardResult OneSchemaPerContext(IReadOnlyList<Type> contextTypes)
    {
        ArgumentNullException.ThrowIfNull(contextTypes);

        var offenders = new List<string>();
        var schemas = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        foreach (var contextType in contextTypes)
        {
            var schema = SchemaOf(contextType);
            if (string.IsNullOrEmpty(schema))
            {
                offenders.Add($"{contextType.Name} declares no default schema");
                continue;
            }

            (schemas.TryGetValue(schema, out var owners) ? owners : schemas[schema] = []).Add(
                contextType.Name
            );
        }

        foreach (var (schema, owners) in schemas.Where(kv => kv.Value.Count > 1))
            offenders.Add($"schema '{schema}' is shared by: {string.Join(", ", owners)}");

        var message =
            "Each domain context must own a distinct, non-null PostgreSQL schema (1:1:1). Give each "
            + "context its own Schema value. Offenders:\n  "
            + string.Join("\n  ", offenders);

        return new GuardResult(offenders, contextTypes.Count, message);
    }

    private static string? SchemaOf(Type contextType)
    {
        var builderType = typeof(DbContextOptionsBuilder<>).MakeGenericType(contextType);
        var builder = (DbContextOptionsBuilder)Activator.CreateInstance(builderType)!;
        builder.UseNpgsql("Host=localhost;Database=offline_model_only");

        // DbContextOptionsBuilder<T> shadows the base Options property (DbContextOptions<T> vs the
        // non-generic DbContextOptions), so restrict to the one declared on the generic builder.
        var options = builderType
            .GetProperty(
                "Options",
                System.Reflection.BindingFlags.Public
                    | System.Reflection.BindingFlags.Instance
                    | System.Reflection.BindingFlags.DeclaredOnly
            )!
            .GetValue(builder);
        using var context = (DbContext)Activator.CreateInstance(contextType, options)!;
        return context.Model.GetDefaultSchema();
    }

    private static string Rel(string root, string file) =>
        Path.GetRelativePath(root, file).Replace('\\', '/');
}
