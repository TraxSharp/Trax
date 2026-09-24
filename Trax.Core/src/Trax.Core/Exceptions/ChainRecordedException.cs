namespace Trax.Core.Exceptions;

/// <summary>
/// The sentinel a monad returns from <c>Resolve()</c> while a chain is being read rather than
/// run. It never escapes chain reading: the reader discards the result and keeps the recorded
/// steps. It exists so <c>Resolve()</c> has something to return that is not a null Right.
/// </summary>
public sealed class ChainRecordedException()
    : Exception("Chain recorded. This result is a sentinel and should not be observed.");
