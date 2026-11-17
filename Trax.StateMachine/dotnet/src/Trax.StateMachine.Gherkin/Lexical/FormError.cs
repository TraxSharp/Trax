namespace Trax.StateMachine.Gherkin.Lexical;

/// <summary>A line not in its keyword's form: the message names the form.</summary>
internal sealed class FormError(string message) : Exception(message);
