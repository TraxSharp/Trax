using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Trax.StateMachine.Features;

/// <summary>One machine a view reads: the caller's own instances of a user machine, or a system instance at its key.</summary>
internal sealed record ViewRead(string Machine, string? Key);
