using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Trax.StateMachine.Features;

namespace Trax.StateMachine.Gherkin;

/// <summary>What a When did, as its Thens say.</summary>
internal abstract record Result
{
    internal sealed record Moved(string To) : Result;

    internal sealed record Refused(string? Message) : Result;

    internal sealed record Denied(string Person) : Result;
}
