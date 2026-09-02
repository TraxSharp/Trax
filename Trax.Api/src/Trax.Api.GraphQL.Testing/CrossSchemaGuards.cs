using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Trax.Api.GraphQL.DataLoaders.CrossSchema;
using Trax.Core.Testing;
using Trax.Core.Testing.Infrastructure;

namespace Trax.Api.GraphQL.Testing;

/// <summary>
/// Architecture-guard checkers for the cross-schema GraphQL pattern. <see cref="EdgeManifestIsValid"/>
/// reflects over a declared <see cref="CrossSchemaEdge"/> manifest; the source guards verify that edge
/// resolvers live in (and route through) the dedicated cross-schema project.
/// </summary>
public static class CrossSchemaGuards
{
    private static readonly Regex CamelCase = new("^[a-z][A-Za-z0-9]*$", RegexOptions.Compiled);
    private static readonly Regex UsesLoader = new(@"\bCrossSchemaLoader<", RegexOptions.Compiled);
    private static readonly Regex ExtendObjectType = new(
        @"\[\s*ExtendObjectType",
        RegexOptions.Compiled
    );

    /// <summary>
    /// A <c>[Parent]</c> resolver parameter: captures any <c>requires:</c> declaration and
    /// the parameter name, e.g. <c>[Parent(requires: nameof(Article.BillId))] Article a</c>.
    /// </summary>
    private static readonly Regex ParentParameter = new(
        @"\[\s*Parent\s*(?:\((?<requires>(?:[^()]|\([^()]*\))*)\))?\s*\]\s*[\w.<>?\[\]]+\s+(?<name>\w+)",
        RegexOptions.Compiled
    );

    /// <summary>The identifiers named inside a <c>requires:</c> argument.</summary>
    private static readonly Regex RequiredNames = new(@"\w+", RegexOptions.Compiled);

    /// <summary>
    /// Each edge in the manifest must reference a real integer foreign key on its source, a target
    /// owned (as a <c>DbSet</c>) by the declared target context, and a camelCase field name.
    /// </summary>
    public static GuardResult EdgeManifestIsValid(IReadOnlyList<CrossSchemaEdge> edges)
    {
        ArgumentNullException.ThrowIfNull(edges);
        var offenders = new List<string>();

        foreach (var edge in edges)
        {
            var fk = edge.Source.GetProperty(edge.Fk, BindingFlags.Public | BindingFlags.Instance);
            if (fk is null)
            {
                offenders.Add($"{edge.Source.Name}.{edge.Fk} does not exist");
            }
            else
            {
                var fkType = Nullable.GetUnderlyingType(fk.PropertyType) ?? fk.PropertyType;
                if (fkType != typeof(int))
                    offenders.Add($"{edge.Source.Name}.{edge.Fk} must be an int foreign key");
            }

            var ownsTarget = edge
                .TargetContext.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Any(p =>
                    p.PropertyType.IsGenericType
                    && p.PropertyType.GetGenericTypeDefinition() == typeof(DbSet<>)
                    && p.PropertyType.GetGenericArguments()[0] == edge.Target
                );
            if (!ownsTarget)
                offenders.Add(
                    $"{edge.TargetContext.Name} must expose DbSet<{edge.Target.Name}> (it is the declared owner)"
                );

            if (!CamelCase.IsMatch(edge.FieldName))
                offenders.Add($"edge field '{edge.FieldName}' must be camelCase");
        }

        var message =
            "Each cross-schema edge must map to a real int foreign key on its source, a DbSet-owned "
            + "target on the declared context, and a camelCase field. Offenders:\n  "
            + string.Join("\n  ", offenders);

        return new GuardResult(offenders, edges.Count, message);
    }

    /// <summary>
    /// Every <c>[ExtendObjectType]</c> resolver in a cross-schema project must route through a
    /// <c>CrossSchemaLoader</c>, so a cross-schema field can never become a hidden N+1.
    /// </summary>
    public static GuardResult EdgeResolversUseLoader(
        ArchitectureGuardOptions options,
        string crossSchemaProjectSuffix = ".CrossSchema"
    )
    {
        ArgumentNullException.ThrowIfNull(options);
        var root = options.RepoRootOverride ?? RepoRoot.Path;
        var offenders = new List<string>();
        var inspected = 0;

        foreach (var file in SourceFiles.CSharpUnder(root, [.. options.SourceScanRoots]))
        {
            var rel = Path.GetRelativePath(root, file).Replace('\\', '/');
            if (!rel.Contains($"{crossSchemaProjectSuffix}/", StringComparison.Ordinal))
                continue;

            var stripped = SourceText.StripCommentsAndStrings(File.ReadAllText(file));
            if (!ExtendObjectType.IsMatch(stripped))
                continue;

            inspected++;
            if (!UsesLoader.IsMatch(stripped))
                offenders.Add(rel);
        }

        var message =
            $"Every [ExtendObjectType] resolver in a {crossSchemaProjectSuffix} project must resolve "
            + "through a CrossSchemaLoader<>, never an ad-hoc DbContext query, so the field is batched. "
            + "Offenders:\n  "
            + string.Join("\n  ", offenders);

        return new GuardResult(offenders, inspected, message);
    }

    /// <summary>
    /// Every property a <c>[Parent]</c> resolver reads off its parent must reach it. Trax adds
    /// the entity key to the projection for hand-written resolvers automatically, so a resolver
    /// reading <c>Id</c> needs nothing; a resolver reading anything else — a cross-schema foreign
    /// key, a column it aggregates on — must declare it with <c>[Parent(requires: ...)]</c> or
    /// silently receive a default value.
    /// </summary>
    /// <remarks>
    /// Source-scanning, so the key it recognises is the <c>Id</c> convention. An entity whose key
    /// is declared with <c>[Key]</c> under another name still needs an explicit <c>requires:</c>
    /// here, which costs one annotation and documents the dependency at the call site.
    /// </remarks>
    public static GuardResult ExtensionResolversDeclareParentRequirements(
        ArchitectureGuardOptions options
    )
    {
        ArgumentNullException.ThrowIfNull(options);
        var root = options.RepoRootOverride ?? RepoRoot.Path;
        var offenders = new List<string>();
        var inspected = 0;

        foreach (var file in SourceFiles.CSharpUnder(root, [.. options.SourceScanRoots]))
        {
            var source = File.ReadAllText(file);
            if (!ExtendObjectType.IsMatch(source))
                continue;

            var stripped = SourceText.StripCommentsAndStrings(source);
            if (!ExtendObjectType.IsMatch(stripped))
                continue;

            var rel = Path.GetRelativePath(root, file).Replace('\\', '/');

            foreach (Match parameter in ParentParameter.Matches(stripped))
            {
                inspected++;

                var name = parameter.Groups["name"].Value;
                var declared = RequiredNames
                    .Matches(parameter.Groups["requires"].Value)
                    .Select(m => m.Value)
                    .ToHashSet(StringComparer.Ordinal);

                foreach (var read in PropertyReads(stripped, name))
                {
                    if (read is "Id" || declared.Contains(read))
                        continue;

                    offenders.Add($"{rel}: reads {name}.{read} without [Parent(requires: ...)]");
                }
            }
        }

        var message =
            "A resolver that reads a property of its [Parent] only receives it when projection "
            + "selected it. The entity key is added automatically; every other property must be "
            + "declared with [Parent(requires: nameof(Entity.Property))]. Offenders:\n  "
            + string.Join("\n  ", offenders);

        return new GuardResult(offenders, inspected, message);
    }

    /// <summary>
    /// Distinct property names read off <paramref name="parameterName"/>, ignoring method calls
    /// (<c>p.Foo(</c>) — those are behaviour on the instance, not a projected column.
    /// </summary>
    private static IEnumerable<string> PropertyReads(string source, string parameterName)
    {
        var pattern = new Regex(
            $@"\b{Regex.Escape(parameterName)}\.(?<property>\w+)\s*(?<call>\()?",
            RegexOptions.Compiled
        );

        return pattern
            .Matches(source)
            .Where(m => !m.Groups["call"].Success)
            .Select(m => m.Groups["property"].Value)
            .Distinct(StringComparer.Ordinal);
    }
}
