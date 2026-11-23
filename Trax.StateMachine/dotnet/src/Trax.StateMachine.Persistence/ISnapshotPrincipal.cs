using System.Runtime.InteropServices;
using Trax.StateMachine;

namespace Trax.StateMachine.Persistence;

/// <summary>
/// The authenticated user behind a request. The draft operations read <see cref="CurrentUserKey"/> to
/// scope every draft to its owner, so the draft id is NOT a bearer capability. In an HTTP host this is
/// backed by the request's principal (e.g. a Trax principal claim); in unit tests it is a fake.
/// </summary>
/// <seealso href="https://traxsharp.net/docs/sdk-reference/statemachine-api/persistence-ports#isnapshotprincipal">SDK reference</seealso>
public interface ISnapshotPrincipal
{
    /// <summary>
    /// The current user's key, or <see langword="null"/> if the request is unauthenticated. An empty or whitespace key is
    /// treated as unauthenticated too.
    /// </summary>
    string? CurrentUserKey { get; }
}
