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
/// other. See <c>docs/adr/0001-a-misconfigured-host-fails-at-startup.md</c>.
/// </summary>
/// <remarks>
/// Contributions are read per extension, after names are complete and before HotChocolate merges
/// the extensions into their types, which is the last point at which two fields of one name are
/// still two. Trax's own fields are named by <see cref="SchemaNameCensus"/>, which checked them
/// against each other before the schema existed.
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

    public override void OnAfterCompleteName(
        ITypeCompletionContext completionContext,
        TypeSystemConfiguration configuration
    )
    {
        if (
            completionContext.Type is not ObjectTypeExtension
            || configuration is not ObjectTypeConfiguration extension
        )
            return;

        // An extension that names its target by CLR type carries that type; its own name is not
        // the target's, so the type decides when there is one.
        var typeName = extension.ExtendsType is { } extended
            ? TypeNames.GetValueOrDefault(extended)
            : extension.Name;

        if (typeName is null || !Tracked(typeName))
            return;

        foreach (var field in extension.Fields)
        {
            if (field.Ignore)
                continue;

            var owner = Owner(typeName, field);
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
    /// nobody Trax can name unless the census says the field is Trax's own, in which case it was
    /// already checked and is skipped.
    /// </summary>
    private SchemaNameOwner? Owner(string typeName, ObjectFieldConfiguration field)
    {
        if ((field.ResolverMember ?? field.Member) is { DeclaringType: { } declaring } member)
        {
            var described = $"{declaring.FullName}.{member.Name}";
            return new SchemaNameOwner($"member:{described}", $"'{described}'");
        }

        return census.FieldOwner(typeName, field.Name) is null
            ? new SchemaNameOwner(
                $"code:{typeName}.{field.Name}:{field.GetHashCode()}",
                "a field a type extension builds in code"
            )
            : null;
    }
}
