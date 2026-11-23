using System.Globalization;
using System.Text;

namespace Trax.StateMachine.Gherkin.Lexical;

/// <summary>One keyword of the closed set: its text, its one form, where it may stand and the block it opens.</summary>
internal sealed record Keyword(string Text, string Form, Block[] Under, Block? Opens = null)
{
    /// <summary>Whether the keyword ends in a colon and takes words after it, or stands alone (<c>Differential</c>).</summary>
    internal bool TakesWords => Text.EndsWith(':');
}
