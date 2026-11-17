using Gherkin;
using Gherkin.CucumberMessages;
using Gherkin.CucumberMessages.Pickles;
using Io.Cucumber.Messages.Types;

namespace Trax.StateMachine.Gherkin;

/// <summary>Something derivation cannot accept, at the file and line it is about.</summary>
internal sealed record Problem(string File, long Line, string Message)
{
    /// <summary>The file a problem with the glossary itself names, rather than a feature.</summary>
    internal const string Glossary = "glossary";

    /// <summary>For a problem with the glossary, the JSON pointer of the key it is about, when there is one.</summary>
    internal string? Pointer { get; init; }

    public override string ToString() => $"{File}:{Line}: {Message}";
}
