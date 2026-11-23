namespace Trax.Client;

/// <summary>What a document's one operation is.</summary>
public enum TraxOperationKind
{
    /// <summary>A <c>query</c>.</summary>
    Query,

    /// <summary>A <c>mutation</c>.</summary>
    Mutation,

    /// <summary>A <c>subscription</c>: a stream, followed with <see cref="TraxClient.Subscribe{TVariables, TResult}"/>.</summary>
    Subscription,
}
