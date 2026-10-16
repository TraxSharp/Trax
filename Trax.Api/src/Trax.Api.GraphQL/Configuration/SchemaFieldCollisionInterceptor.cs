using HotChocolate.Configuration;
using HotChocolate.Types;
using HotChocolate.Types.Descriptors.Configurations;
using Trax.Api.GraphQL.Mutations;
using Trax.Api.GraphQL.Queries;
using Trax.Api.GraphQL.TypeModules;

namespace Trax.Api.GraphQL.Configuration;

/// <summary>
/// Refuses a field a type extension adds to one of Trax's root or namespace types when another
/// surface already puts a field of that name there: Trax itself (a query model, a train, a
/// namespace, an entry field) or another type extension. HotChocolate merges the two into one
/// field whose resolver is whichever it built last, so one of them would silently answer for the
/// other. A host's extension that builds a field in code under the name of one of Trax's own, or
/// ignores a field of a Trax-owned type, is refused too: the first replaces Trax's field, the
/// second removes it, and neither leaves a member behind for the name census to see. See
/// <c>docs/adr/0001-a-misconfigured-host-fails-at-startup.md</c>.
/// </summary>
/// <remarks>
/// Contributions are read per extension, after names are complete and before HotChocolate merges
/// the extensions into their types, which is the last point at which two fields of one name are
/// still two. Trax's own fields are named by <see cref="SchemaNameCensus"/>, which checked them
/// against each other before the schema existed.
/// <para>
/// A host's extension is one whose class is the host's: Trax builds its own as inline
/// <see cref="ObjectTypeExtension"/>s, so an extension a host also builds inline cannot be told
/// from Trax's and is not checked for a code-built field or an ignore.
/// </para>
/// </remarks>
internal sealed class SchemaFieldCollisionInterceptor(
    SchemaNameCensus census,
    TypeExtensionExposureReport report
) : TypeInterceptor
{
    private static readonly Dictionary<Type, string> TypeNames = new()
    {
        [typeof(RootQuery)] = "RootQuery",
        [typeof(RootMutation)] = "RootMutation",
        [typeof(DiscoverQueries)] = "DiscoverQueries",
        [typeof(DispatchMutations)] = "DispatchMutations",
    };

    private readonly Dictionary<
        (string Type, string Field),
        List<SchemaNameOwner>
    > _contributions = [];

    private readonly HashSet<string> _traxTypes = new(StringComparer.Ordinal);

    private readonly List<(string Type, string Field, string Extension)> _ignored = [];

    public override void OnAfterCompleteName(
        ITypeCompletionContext completionContext,
        TypeSystemConfiguration configuration
    )
    {
        if (configuration is not ObjectTypeConfiguration objectType)
            return;

        if (completionContext.Type is not ObjectTypeExtension)
        {
            if (
                Tracked(objectType.Name)
                || objectType.RuntimeType is { } runtime && IsTrax(runtime.Assembly)
            )
                _traxTypes.Add(objectType.Name);
            return;
        }

        var extension = objectType;

        // An extension that names its target by CLR type carries that type; its own name is not
        // the target's, so the type decides when there is one.
        var typeName = extension.ExtendsType is { } extended
            ? TypeNames.GetValueOrDefault(extended)
            : extension.Name;

        if (typeName is null)
            return;

        var hostExtension = HostExtension(completionContext.Type);

        // Field(name).Ignore() on an extension lands in FieldIgnores, not Fields.
        if (hostExtension is not null)
            foreach (
                var ignored in extension
                    .FieldIgnores.Select(b => b.Name)
                    .Concat(extension.Fields.Where(f => f.Ignore).Select(f => f.Name))
                    .Distinct()
            )
                _ignored.Add((typeName, ignored, hostExtension));

        if (!Tracked(typeName))
            return;

        foreach (var field in extension.Fields)
        {
            if (field.Ignore)
                continue;

            var owner = Owner(typeName, field, hostExtension);
            if (owner is null)
                continue;

            if (!_contributions.TryGetValue((typeName, field.Name), out var owners))
                _contributions[(typeName, field.Name)] = owners = [];
            if (!owners.Any(o => o.Key == owner.Key))
                owners.Add(owner);
        }
    }

    public override void OnBeforeMergeTypeExtensions()
    {
        foreach (var (typeName, fieldName, extension) in _ignored)
        {
            if (!_traxTypes.Contains(typeName))
                continue;

            var path = $"{typeName}.{fieldName}";
            report.Add(
                new TypeExtensionExposureViolation(
                    $"ignored:{path}",
                    $"GraphQL field '{path}' is Trax's, and type extension '{extension}' removes it "
                        + "with Ignore(). A host does not remove Trax's fields; leave it out of "
                        + "the extension. See docs/adr/0001-a-misconfigured-host-fails-at-startup.md."
                )
            );
        }

        foreach (var ((typeName, fieldName), owners) in _contributions)
        {
            var all = census.FieldOwner(typeName, fieldName) is { } trax
                ? owners.Prepend(trax).ToList()
                : owners;

            if (all.Count < 2)
                continue;

            var path = $"{typeName}.{fieldName}";
            report.Add(
                new TypeExtensionExposureViolation(
                    $"collision:{path}",
                    $"GraphQL field '{path}' is claimed by "
                        + string.Join(" and ", all.Select(o => o.Description))
                        + ". HotChocolate would merge them into one field, keeping whichever it "
                        + "built last. Rename one of them. See "
                        + "docs/adr/0001-a-misconfigured-host-fails-at-startup.md."
                )
            );
        }
    }

    private bool Tracked(string typeName) =>
        TypeNames.ContainsValue(typeName) || census.FieldParents.Contains(typeName);

    /// <summary>
    /// Who contributed the field: the member it was built from, or, for a field built in code,
    /// the host's extension class when there is one. A field built in code by Trax under a name the
    /// census says is Trax's own was already checked, and is skipped.
    /// </summary>
    private SchemaNameOwner? Owner(
        string typeName,
        ObjectFieldConfiguration field,
        string? hostExtension
    )
    {
        if ((field.ResolverMember ?? field.Member) is { DeclaringType: { } declaring } member)
        {
            var described = $"{declaring.FullName}.{member.Name}";
            return new SchemaNameOwner($"member:{described}", $"'{described}'");
        }

        if (hostExtension is not null)
            return new SchemaNameOwner(
                $"code:{typeName}.{field.Name}:{hostExtension}",
                $"a field type extension '{hostExtension}' builds in code"
            );

        return census.FieldOwner(typeName, field.Name) is null
            ? new SchemaNameOwner(
                $"code:{typeName}.{field.Name}:{field.GetHashCode()}",
                "a field a type extension builds in code"
            )
            : null;
    }

    /// <summary>
    /// The class name of a type extension a host wrote, or null for one Trax built: Trax builds
    /// its own inline, as HotChocolate's own <see cref="ObjectTypeExtension"/>, or in this assembly.
    /// </summary>
    private static string? HostExtension(object extension)
    {
        var type = extension.GetType();
        return type.Assembly == typeof(ObjectTypeExtension).Assembly || IsTrax(type.Assembly)
            ? null
            : type.FullName;
    }

    private static bool IsTrax(System.Reflection.Assembly assembly) =>
        assembly == typeof(SchemaFieldCollisionInterceptor).Assembly;
}
