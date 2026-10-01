using HotChocolate.Language;
using HotChocolate.Types;
using HotChocolate.Types.Descriptors.Configurations;
using Trax.Api.GraphQL.Validation;

namespace Trax.Api.GraphQL.TypeModules;

/// <summary>
/// Builds the types behind a declared GraphQL namespace (<c>Namespace = "alerts"</c> on a train
/// or query model): an empty object type the namespace's fields extend, and the field on the
/// parent type that reaches it. One module owns each namespace, so each is declared once.
/// </summary>
internal static class NamespaceTypes
{
    /// <summary>
    /// The empty object type a namespace's fields are added to, marked so the type-extension
    /// posture census can tell a declared namespace from any other untyped object type.
    /// </summary>
    internal static ObjectType Base(string namespaceTypeName) =>
        new(d =>
        {
            d.Name(namespaceTypeName);
            d.Extend().Configuration.Features.Set(DeclaredNamespace.Marker);
        });

    /// <summary>True when <paramref name="objectType"/> is a namespace type built by <see cref="Base"/>.</summary>
    internal static bool IsDeclaredNamespace(ObjectTypeConfiguration objectType) =>
        objectType.Features.Get<DeclaredNamespace>() is not null;

    private sealed class DeclaredNamespace
    {
        internal static readonly DeclaredNamespace Marker = new();

        private DeclaredNamespace() { }
    }

    /// <summary>
    /// The field on <paramref name="parentTypeName"/> that reaches the namespace, marked as a
    /// namespace so the per-request operation cap counts what is selected under it.
    /// </summary>
    internal static ObjectTypeExtension Field(
        string parentTypeName,
        string fieldName,
        string namespaceTypeName
    ) =>
        new(d =>
        {
            d.Name(parentTypeName);
            NamespaceField
                .Mark(d.Field(fieldName))
                .Type(new NamedTypeNode(namespaceTypeName))
                .Resolve(_ => new object());
        });
}
