using System.Text.Json.Nodes;
using Trax.StateMachine.Features;

namespace Trax.StateMachine.Gherkin;

/// <summary>What a host answered a request with, as a caller sees it.</summary>
internal abstract record Answer
{
    /// <summary>The request was taken: the instance is now in <see cref="State"/>.</summary>
    internal sealed record Moved(string State) : Answer;

    /// <summary>The request was refused with the code and message the host sends.</summary>
    internal sealed record Refused(RefusalCode Code, string Message) : Answer;

    /// <summary>The caller has no such operation: the host does not serve it to them.</summary>
    internal sealed record NotServed(string What) : Answer;
}
