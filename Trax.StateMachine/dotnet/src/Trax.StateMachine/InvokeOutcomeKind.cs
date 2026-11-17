using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Trax.StateMachine;

/// <summary>How an invoked run ended. Its outcome trigger is named <c>&lt;State&gt;.done</c>, <c>.failed</c> or <c>.cancelled</c>.</summary>
internal enum InvokeOutcomeKind
{
    Done,
    Failed,
    Cancelled,
}
