using System.Globalization;
using System.Text.Json.Nodes;
using Trax.StateMachine.Features;

namespace Trax.StateMachine.Gherkin;

/// <summary>
/// Something the glossary names that no scenario shows, and, when one can be written, a scenario to paste that shows it.
/// A stub is offered only if the features with it pasted derive with no problem, and what they derive differs only by
/// what the stub claims (ADR <c>Trax.Docs/adr/0058</c>). A stub guesses where the move goes: read it before pasting it.
/// </summary>
internal sealed record Gap(GapKind Kind, string Machine, string What, string Message, string? Stub);
