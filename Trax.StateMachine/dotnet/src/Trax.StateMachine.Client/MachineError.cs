using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;

namespace Trax.StateMachine.Client;

/// <summary>
/// Why a controller stopped for good: the instance is gone, this client is out of date and must reload, or the host
/// refused the socket.
/// </summary>
/// <param name="Code">
/// <see cref="ClientProblemCodes.Gone"/>, <c>CLIENT_OUTDATED</c>, or the code of a stream that ended for good
/// (<see cref="StreamMessage.Refused"/>): <c>UNAUTHENTICATED</c> or <c>FORBIDDEN</c> for a socket or a stream the host
/// refused, a code of <see cref="GraphQLMachineServerOptions.TerminalStreamErrors"/>, or
/// <see cref="ClientProblemCodes.TransportError"/> for a stream that could not be opened or followed.
/// </param>
/// <param name="Message">What to tell a person.</param>
/// <param name="Deleted">
/// With <see cref="ClientProblemCodes.Gone"/>: true when the instance was deleted or expired, false when the server
/// answered <c>NOT_FOUND</c>, which does not tell a missing instance from another caller's. Null otherwise.
/// </param>
public sealed record MachineError(string Code, string Message, bool? Deleted = null);
