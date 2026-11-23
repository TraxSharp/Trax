using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;

namespace Trax.StateMachine.Client;

/// <summary>The codes a client reports of its own, beside the server's refusal codes.</summary>
public static class ClientProblemCodes
{
    /// <summary>The instance was deleted, or the server holds none the caller may read.</summary>
    public const string Gone = "GONE";

    /// <summary>The request never reached the server, or its answer never came back (offline, a 5xx, an auth error).</summary>
    public const string TransportError = "TRANSPORT_ERROR";

    /// <summary>A retry was asked for, but nothing has failed since the last one was retried.</summary>
    public const string NothingToRetry = "NOTHING_TO_RETRY";

    /// <summary>The controller was disposed before the server answered.</summary>
    public const string Disposed = "DISPOSED";

    /// <summary>
    /// There is no instance to send to, and none is being started or read: the start was refused or failed, or the first
    /// read gave up. <see cref="ServerMachineController.RetryAsync"/> resends a start that failed, and reads again after a
    /// first read that gave up.
    /// </summary>
    public const string NotReady = "NOT_READY";
}
