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
}
