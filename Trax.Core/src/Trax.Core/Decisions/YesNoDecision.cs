namespace Trax.Core.Decisions;

/// <summary>
/// The probability of yes to the question <typeparamref name="TQuestion"/> asks, put in Memory
/// when it is decided.
/// </summary>
/// <typeparam name="TQuestion">
/// A marker type naming the question, usually carrying <see cref="AsksAttribute"/>. Memory is keyed
/// by type, so each yes/no question a train asks needs its own.
/// </typeparam>
/// <param name="Probability">The probability of yes, from 0 to 1.</param>
/// <param name="Model">The model that decided, as the decider named it, or null for one that is not a model.</param>
public sealed record YesNoDecision<TQuestion>(double Probability, string? Model);
