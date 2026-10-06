using HotChocolate.Configuration;
using HotChocolate.Data.Filters;
using HotChocolate.Data.Sorting;
using HotChocolate.Types;
using HotChocolate.Types.Descriptors.Configurations;

namespace Trax.Api.GraphQL.Authorization;

/// <summary>
/// Attaches <see cref="NavigationInputAuthorization"/> to every object field that takes a filter
/// or sort argument, so a <c>where</c> or <c>order</c> that reaches a gated type is authorized on
/// whatever field it arrives through, whoever contributed that field. See
/// <c>docs/adr/0025-every-entity-a-query-model-reaches-declares-its-posture.md</c>.
/// </summary>
/// <remarks>
/// <para>
/// Runs at <c>OnBeforeCompleteType</c>, on the merged type, so a field a type extension adds is
/// seen, and before HotChocolate compiles the field's middleware pipeline. A
/// <c>[UseFiltering]</c> or <c>[UseSorting]</c> field already carries its argument by then, with
/// the input type it will take, and the filtering middleware is still a placeholder: inserting at
/// the front of the list puts the check ahead of paging, projection, filtering and sorting, so it
/// runs before anything reaches the database.
/// </para>
/// <para>
/// The argument is recognised by the type it resolves to, not by its name, so a convention that
/// renames <c>where</c> or <c>order</c> changes nothing. A field that already carries the
/// middleware, a query model's entry field, is left as it is.
/// </para>
/// </remarks>
internal sealed class NavigationInputAuthorizationInterceptor : TypeInterceptor
{
    public override void OnBeforeCompleteType(
        ITypeCompletionContext completionContext,
        TypeSystemConfiguration configuration
    )
    {
        if (configuration is not ObjectTypeConfiguration objectType)
            return;

        foreach (var field in objectType.Fields)
        {
            if (
                field.MiddlewareConfigurations.Any(m =>
                    m.Key == NavigationInputAuthorization.MiddlewareKey
                )
            )
                continue;

            if (!field.Arguments.Any(a => IsFilterOrSortInput(completionContext, a)))
                continue;

            field.MiddlewareConfigurations.Insert(
                0,
                new FieldMiddlewareConfiguration(
                    NavigationInputAuthorization.Create(coveredEntity: null),
                    key: NavigationInputAuthorization.MiddlewareKey
                )
            );
        }
    }

    private static bool IsFilterOrSortInput(
        ITypeCompletionContext context,
        ArgumentConfiguration argument
    ) =>
        argument.Type is { } reference
        && context.TryGetType<IType>(reference, out var type)
        && type.NamedType() is IFilterInputType or ISortInputType;
}
