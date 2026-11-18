using System.Security.Claims;

namespace Trax.Api.GraphQL.Subscriptions;

/// <summary>
/// Told of every socket connection refused at <c>connection_init</c>. The audit package registers
/// one, so credential probing over a socket, which never reaches the executor, is recorded.
/// </summary>
internal interface ISocketRefusalObserver
{
    /// <summary>Records <paramref name="refusal"/>. Must not throw or block.</summary>
    void Refused(SocketRefusal refusal);
}
