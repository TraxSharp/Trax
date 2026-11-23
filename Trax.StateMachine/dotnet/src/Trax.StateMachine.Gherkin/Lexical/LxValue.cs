namespace Trax.StateMachine.Gherkin.Lexical;

// What a lexicon says, line by line, before anything is inferred. Every node keeps the line it came from, so the
// elaborator can name it in a problem and map each key it writes back to it.

internal abstract record LxValue(int Line);
