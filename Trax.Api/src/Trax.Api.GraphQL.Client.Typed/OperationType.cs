namespace Trax.Api.GraphQL.Client.Typed;

/// <summary>The kind of operation a typed request generates. Subscriptions are not supported.</summary>
public enum OperationType
{
    /// <summary>A <c>query</c> operation.</summary>
    Query,

    /// <summary>A <c>mutation</c> operation.</summary>
    Mutation,
}
