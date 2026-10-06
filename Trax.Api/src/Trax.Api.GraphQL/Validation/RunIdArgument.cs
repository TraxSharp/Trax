using HotChocolate;

namespace Trax.Api.GraphQL.Validation;

/// <summary>
/// Checks a run's id given as an argument (<c>metadataId</c>) before it selects anything.
/// </summary>
/// <remarks>
/// A persisted run's id is positive. Zero is what an unsaved run carries, so filtering on it would
/// select every unsaved run at once rather than one run, and a negative id names nothing.
/// </remarks>
internal static class RunIdArgument
{
    /// <summary>The error code a run id that is not positive fails with.</summary>
    public const string ErrorCode = "TRAX_INVALID_ARGUMENT";

    /// <summary>Throws a field error unless <paramref name="metadataId"/> is positive.</summary>
    /// <param name="metadataId">The id the caller gave.</param>
    public static void Require(long metadataId)
    {
        if (metadataId > 0)
            return;

        throw new GraphQLException(
            ErrorBuilder
                .New()
                .SetMessage($"metadataId must be a positive execution id; {metadataId} was given.")
                .SetCode(ErrorCode)
                .Build()
        );
    }
}
