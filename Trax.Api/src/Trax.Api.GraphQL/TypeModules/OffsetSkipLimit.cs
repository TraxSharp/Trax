using System.Globalization;
using HotChocolate;
using HotChocolate.Resolvers;

namespace Trax.Api.GraphQL.TypeModules;

/// <summary>
/// Refuses a <c>skip</c> above the host's <c>MaxOffsetSkip</c> on an offset-paged query model.
/// The database reads and throws away every skipped row, so an unbounded <c>skip</c> lets one
/// request scan a whole table. Runs before the paging middleware, so a refused request sends no
/// query.
/// </summary>
internal static class OffsetSkipLimit
{
    internal const string Code = "TRAX_OFFSET_SKIP_LIMIT";

    public static FieldMiddleware Create(int maxSkip) =>
        next =>
            context =>
            {
                if (context.ArgumentValue<int?>("skip") is { } skip && skip > maxSkip)
                    throw new GraphQLException(
                        ErrorBuilder
                            .New()
                            .SetMessage(
                                string.Create(
                                    CultureInfo.InvariantCulture,
                                    $"skip is {skip}; this field accepts at most {maxSkip}."
                                )
                            )
                            .SetCode(Code)
                            .Build()
                    );
                return next(context);
            };
}
