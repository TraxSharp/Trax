namespace Trax.StateMachine.Gherkin.Lexical;

/// <summary>A line not in its keyword's form: the message names the form.</summary>
internal sealed class FormErrorException(string message) : Exception(message);
