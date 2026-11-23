using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace Trax.StateMachine.Persistence;

/// <summary>How a host hears that a state-machine instance changed (<see cref="StateMachineOptions.ChangeFeed"/>).</summary>
public enum StateMachineChangeFeed
{
    /// <summary>
    /// On Postgres, one listening session per host hears the notice the database sends for every committed write.
    /// Changes arrive at once; every write pays for the notice.
    /// </summary>
    Notify,

    /// <summary>
    /// Every host with an open watch reads the watched rows' revisions once a second, as on SQLite. Once the database
    /// turns its snapshot notices off (<c>ALTER DATABASE ... SET trax.snapshot_notify = 'off'</c>), a snapshot write
    /// sends none, and takes the notify lock only when its transaction sends another notice.
    /// </summary>
    Poll,
}
