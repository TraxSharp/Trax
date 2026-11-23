using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Enums;

namespace Trax.StateMachine.Persistence;

/// <summary>One page of the live tokens whose runs have ended, from <see cref="InvokeOutcomeDelivery.Ended"/>.</summary>
/// <param name="Tokens">The tokens whose runs have ended, in token order.</param>
/// <param name="Next">Where the next page starts, or null when this was the last.</param>
internal sealed record EndedPage(IReadOnlyList<string> Tokens, string? Next);
