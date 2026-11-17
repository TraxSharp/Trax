using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Enums;

namespace Trax.StateMachine.Persistence;

/// <summary>What a <see cref="SnapshotWatch"/> hands on.</summary>
internal enum SnapshotRowChangeKind
{
    /// <summary>The row as it is now, at a revision newer than the watcher had.</summary>
    Changed,

    /// <summary>The row no longer exists: deleted or expired. The end of a watch on it.</summary>
    Gone,

    /// <summary>This host cannot read the row (written by another version, or by hand). The end of a watch on it.</summary>
    Unreadable,

    /// <summary>For a watch on every row only: changes may have been missed, so list again.</summary>
    Reload,
}
