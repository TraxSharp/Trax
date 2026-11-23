namespace Trax.Effect.Attributes;

/// <summary>
/// Controls which execution modes are available for a train's GraphQL mutation.
/// Pass one or more values to <see cref="TraxMutationAttribute"/> to configure behavior.
/// </summary>
[Flags]
public enum GraphQLOperation
{
    /// <summary>Synchronous execution — the mutation runs the train and returns the result immediately.</summary>
    Run = 1,

    /// <summary>Asynchronous execution — the mutation queues the train for later processing (includes priority parameter).</summary>
    Queue = 2,
}
