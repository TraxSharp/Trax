using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using Trax.Core.Utils;

namespace Trax.Core.Decisions;

/// <summary>An answer that does not fit its question. The run fails rather than act on it.</summary>
/// <param name="message">What is wrong, naming the answer, for a live answer.</param>
/// <param name="replayReason">
/// What is wrong without naming the answer, for one recorded by an earlier run.
/// </param>
internal sealed class InvalidAnswerException(string message, string replayReason)
    : Exception(message)
{
    public string ReplayReason { get; } = replayReason;
}
