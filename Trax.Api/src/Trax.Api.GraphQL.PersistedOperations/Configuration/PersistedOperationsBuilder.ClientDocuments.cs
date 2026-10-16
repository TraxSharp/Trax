namespace Trax.Api.GraphQL.PersistedOperations.Configuration;

public sealed partial class PersistedOperationsBuilder
{
    /// <summary>
    /// Registers the documents a frontend wrote, from the <c>trax.documents.json</c> that <c>trax client --documents</c>
    /// writes, when the host starts, beside the documents it generates. Repeated calls add files.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <paramref name="path"/> is resolved against the host's content root. Each document is one named query or
    /// mutation of the trains this host exposes and, in a query, its query models, under <c>dispatch</c> or
    /// <c>discover</c>, using no directive, with any filter or sort written in the document rather than passed whole as
    /// a variable. The host checks every document itself and runs it through its own validation before it serves
    /// anything: a missing or malformed file, a name used twice, a document reaching anything but those, a filter or
    /// sort variable, or one the host's validation refuses (its depth limit, its operation cap, a field it no longer
    /// has) refuses the host's start, naming the document.
    /// </para>
    /// <para>
    /// Each document is stored under <c>trax_client_&lt;name&gt;_&lt;hash of its text&gt;</c>, which the generated client
    /// sends. A version a frontend no longer holds keeps running on every node, while it still validates, until no node
    /// has registered it for the grace period (<see cref="KeepOlderMachineDocumentsFor"/>), so a page built before a
    /// deploy keeps working through the frontend's own; after that it answers <c>CLIENT_OUTDATED</c>. Deploy the host
    /// before the frontend. See <c>Trax.Docs/adr/0053-a-client-document-is-registered-by-the-host-at-boot.md</c>.
    /// </para>
    /// </remarks>
    /// <param name="path">The file <c>trax client --documents</c> wrote, relative to the content root.</param>
    public PersistedOperationsBuilder ClientDocuments(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _clientDocumentFiles.Add(path);
        return this;
    }
}
