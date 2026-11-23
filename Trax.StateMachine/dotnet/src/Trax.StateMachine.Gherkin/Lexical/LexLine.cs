using System.Globalization;
using System.Text;

namespace Trax.StateMachine.Gherkin.Lexical;

/// <summary>One lexicon line: where it is, its keyword and the tokens after it.</summary>
internal sealed record LexLine(int Number, Keyword Keyword, IReadOnlyList<Token> Tokens)
{
    /// <summary>
    /// A line already reported as a problem, kept only because it opens a block: the lines under it attach to it and
    /// are not each refused for having no block to go under.
    /// </summary>
    internal bool Broken { get; init; }
}
