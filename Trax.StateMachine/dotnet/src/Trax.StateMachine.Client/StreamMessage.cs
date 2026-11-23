using System.Text.Json.Nodes;

namespace Trax.StateMachine.Client;

/// <summary>What an instance's change stream delivers.</summary>
public abstract record StreamMessage
{
    private StreamMessage() { }

    /// <summary>The instance as it is now. The first message of every new stream is one.</summary>
    public sealed record Changed(ServerSnapshot Snapshot) : StreamMessage;

    /// <summary>
    /// The instance is gone. <see cref="NotFound"/> is true when the server answered <c>NOT_FOUND</c>, which it
    /// answers alike for an instance that is missing and one that is not the caller's; false when it was deleted.
    /// </summary>
    public sealed record Gone(bool NotFound) : StreamMessage;

    /// <summary>The server asks for the instance to be read again: anything may have been missed.</summary>
    public sealed record Reload : StreamMessage;

    /// <summary>The server no longer serves this client's version of the machine.</summary>
    public sealed record ClientOutdated : StreamMessage;

    /// <summary>
    /// The stream ended for good: subscribing again would get the same answer. The host refused the socket, closing it
    /// with 4401 or 4403 or answering its upgrade 401 or 403, and <see cref="Code"/> is <c>UNAUTHENTICATED</c> for 4401
    /// and 401, <c>FORBIDDEN</c> for 4403 and 403, with a <see cref="Message"/> that names the code the host closed or
    /// answered with. Or the host refused the stream itself with a GraphQL error coded <c>TRAX_AUTHORIZATION</c> or
    /// <c>AUTH_NOT_AUTHORIZED</c> (<c>FORBIDDEN</c>) or <c>AUTH_NOT_AUTHENTICATED</c> (<c>UNAUTHENTICATED</c>); or with
    /// a code the caller named terminal (<see cref="GraphQLMachineServerOptions.TerminalStreamErrors"/>), which is the
    /// <see cref="Code"/>; or there is no stream to follow (the socket was disposed, or the stream's handler threw), and
    /// <see cref="Code"/> is <see cref="ClientProblemCodes.TransportError"/>.
    /// </summary>
    public sealed record Refused(string Code, string Message) : StreamMessage;
}
