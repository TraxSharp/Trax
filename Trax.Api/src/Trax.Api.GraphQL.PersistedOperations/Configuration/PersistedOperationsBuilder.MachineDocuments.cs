namespace Trax.Api.GraphQL.PersistedOperations.Configuration;

public sealed partial class PersistedOperationsBuilder
{
    /// <summary>
    /// How long a version of the generated documents stays active once no node registers it any more. Seven days by
    /// default.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every exposed machine's and train's operations are persisted documents the host registers itself when it
    /// starts and again while it runs, and a change to their shape gives them new ids. A node keeps the documents it
    /// registers marked as registered, so a node still running another version, older or newer, keeps its documents
    /// for as long as it runs. A version no node has registered for this long is deactivated, whatever its number:
    /// after a rolling deploy the older one goes, after a rollback the newer one. An older id sent to a newer node is
    /// answered <c>CLIENT_OUTDATED</c>; a newer id sent to an older node <c>SERVER_OUTDATED</c> while it is active and
    /// <c>CLIENT_OUTDATED</c> once it was deactivated.
    /// </para>
    /// <para>
    /// See <c>Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md</c>.
    /// </para>
    /// </remarks>
    /// <param name="grace">How long an unregistered version stays: longer than any deploy takes.</param>
    public PersistedOperationsBuilder KeepOlderMachineDocumentsFor(TimeSpan grace)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(grace, TimeSpan.Zero);
        _machineDocumentGracePeriod = grace;
        return this;
    }
}
