namespace Trax.Api.GraphQL.PersistedOperations.Configuration;

public sealed partial class PersistedOperationsBuilder
{
    /// <summary>
    /// How long an older version's machine documents stay active after a node registers a newer version's. Seven
    /// days by default.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every exposed machine's operations are persisted documents the host registers itself when it starts, and a
    /// change to the machine gives them new ids. During a rolling deploy a node still running the older version serves
    /// the older documents, so a node never deactivates them when it starts: it deactivates them once its own
    /// documents have been stored for this long. Before and after, an older id sent to a newer node is answered
    /// <c>client-outdated</c>, and a newer id sent to an older node <c>server-outdated</c>.
    /// </para>
    /// <para>
    /// See <c>Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md</c>.
    /// </para>
    /// </remarks>
    /// <param name="grace">How long the older documents stay: longer than any deploy takes.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="grace"/> is not positive.</exception>
    public PersistedOperationsBuilder KeepOlderMachineDocumentsFor(TimeSpan grace)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(grace, TimeSpan.Zero);
        _machineDocumentGracePeriod = grace;
        return this;
    }
}
