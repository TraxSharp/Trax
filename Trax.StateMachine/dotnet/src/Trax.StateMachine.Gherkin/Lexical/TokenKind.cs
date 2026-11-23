using System.Globalization;
using System.Text;

namespace Trax.StateMachine.Gherkin.Lexical;

internal enum TokenKind
{
    /// <summary>A run of anything but whitespace, a quote, a comma or a bracket.</summary>
    Word,

    /// <summary>A quoted text, its escapes read.</summary>
    Text,
    Comma,
    Open,
    Close,
}
