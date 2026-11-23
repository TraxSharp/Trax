using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Trax.Api.GraphQL.TypeModules;

namespace Trax.Api.GraphQL.PersistedOperations.Services;

/// <summary>What a node does with a request naming a machine document by id.</summary>
internal enum MachineDocumentVerdict
{
    /// <summary>One of this node's own documents: look it up and run it.</summary>
    Own,

    /// <summary>A document of an older version of the machine, or one no version generated: the client reloads.</summary>
    ClientOutdated,

    /// <summary>
    /// A document of a newer version of the machine than this node runs, or one of a machine this node does not serve
    /// that the store holds active: the client retries.
    /// </summary>
    ServerOutdated,

    /// <summary>
    /// A client document another node registered that the store holds active: it runs here if its stored text passes
    /// this node's checks, and answers <see cref="ClientOutdated"/> if not.
    /// </summary>
    Stored,
}
