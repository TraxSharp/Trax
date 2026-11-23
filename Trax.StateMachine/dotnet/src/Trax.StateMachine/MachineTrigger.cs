namespace Trax.StateMachine;

/// <summary>One trigger a machine declares, with its kind and the CLR type of its input.</summary>
/// <param name="Name">The trigger's wire name.</param>
/// <param name="Kind">Who may fire it.</param>
/// <param name="InputType">The record declared with <c>WithInput</c>, or <see langword="null"/> when it takes no input.</param>
public sealed record MachineTrigger(string Name, TriggerKind Kind, Type? InputType);
