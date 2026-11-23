namespace Trax.StateMachine;

/// <summary>One state a machine declares, with the record its context is declared with.</summary>
/// <param name="Name">The state's wire name.</param>
/// <param name="ContextType">
/// The record the state declares with <c>Context&lt;TContext&gt;()</c>, or <see langword="null"/> when it declares an empty
/// context (<c>Context()</c>) or none.
/// </param>
public sealed record MachineState(string Name, Type? ContextType);
