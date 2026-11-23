using System.Data.Common;
using System.Globalization;
using System.Reflection;
using Npgsql;
using Trax.Effect.Enums;

namespace Trax.Effect.Data.Testing;

/// <summary>
/// Marks a test, or a whole fixture, that leaves a Trax database inconsistent on purpose (a run
/// stuck in progress, an effect claim abandoned in flight), so the fixture's teardown skips
/// <see cref="TraxInvariants"/> for it.
/// </summary>
/// <param name="reason">Why the test leaves that state; it is read by the next person to wonder.</param>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, Inherited = true)]
public sealed class LeavesStuckRunsAttribute(string reason) : Attribute
{
    /// <summary>Why the test leaves the database inconsistent.</summary>
    public string Reason { get; } = reason;
}
