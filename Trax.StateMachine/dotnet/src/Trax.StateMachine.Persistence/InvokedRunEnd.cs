using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Enums;

namespace Trax.StateMachine.Persistence;

/// <summary>How an invoked run ended, as the machine that invoked it reads it.</summary>
/// <param name="Kind">Done, Failed or Cancelled.</param>
/// <param name="Output">A completed run's recorded output, or null.</param>
/// <param name="Oversize">True when a completed run's output was too large to record.</param>
/// <param name="Reason">Why the end is applied as a failure the run did not report itself, one of <see cref="InvokeOutcomeReasons"/>; null otherwise.</param>
internal sealed record InvokedRunEnd(
    InvokeOutcomeKind Kind,
    string? Output,
    bool Oversize,
    string? Reason = null
);
