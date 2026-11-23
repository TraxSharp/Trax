using System.Globalization;
using System.Text;

namespace Trax.StateMachine.Gherkin.Lexical;

/// <summary>A problem with a lexicon, at its line (0 for the file as a whole).</summary>
internal sealed record LexProblem(int Line, string Message);
