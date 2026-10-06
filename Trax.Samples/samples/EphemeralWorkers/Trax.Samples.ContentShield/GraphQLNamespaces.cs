namespace Trax.Samples.ContentShield;

/// <summary>
/// GraphQL namespace constants for the content shield API.
/// Trains sharing a namespace are grouped under the same
/// sub-field in the schema (e.g. <c>dispatch { moderation { reviewContent } }</c>).
/// </summary>
public static class GraphQLNamespaces
{
    public const string Moderation = "moderation";
    public const string Reports = "reports";
}
