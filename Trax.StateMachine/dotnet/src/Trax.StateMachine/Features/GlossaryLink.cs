using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Trax.StateMachine.Features;

internal sealed record GlossaryLink(string Phrase, string Machine, string Field);
