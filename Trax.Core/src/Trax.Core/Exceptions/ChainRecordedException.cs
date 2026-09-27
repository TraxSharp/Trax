using System.ComponentModel;

namespace Trax.Core.Exceptions;

/// <summary>
/// The sentinel a monad returns from <c>Resolve()</c> while a chain is being read rather than
/// run. It never escapes chain reading: the reader discards the result and keeps the recorded
/// steps. It exists so <c>Resolve()</c> has something to return that is not a null Right.
/// </summary>
/// <remarks>
/// Public only because it shipped public, and making it internal now would be a breaking change
/// in the same release as <c>Train.NewMonad()</c> (docs/adr/0002). It is not part of the surface a
/// consumer programs against: nothing should catch it, match on it, or throw it, and a train that
/// observes one has a bug in Trax rather than in the train.
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class ChainRecordedException()
    : Exception("Chain recorded. This result is a sentinel and should not be observed.");
