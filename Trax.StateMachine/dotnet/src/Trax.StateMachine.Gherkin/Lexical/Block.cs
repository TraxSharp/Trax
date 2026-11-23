using System.Globalization;
using System.Text;

namespace Trax.StateMachine.Gherkin.Lexical;

/// <summary>The blocks a lexicon line can open, and the root every lexicon is.</summary>
internal enum Block
{
    Root,
    Actor,
    Type,
    Machine,
    State,
    Trigger,
    Guard,
    Runs,
    View,
    Field,
}
