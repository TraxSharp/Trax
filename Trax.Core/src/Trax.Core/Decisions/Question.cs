namespace Trax.Core.Decisions;

/// <summary>
/// One question. The key identifies it in the answers; everything a model needs to know is in
/// <see cref="Instructions"/> and the criteria.
/// </summary>
/// <remarks>
/// The key is <see cref="QuestionKey.For(Type)"/> of the type the question is about: the
/// <see cref="AsksAttribute.Key"/> on it, or else its name without the namespace. It is not meant
/// to inform a model, but it is not hidden from one either: an adapter may send it as the
/// question's id, as the System One adapter does.
/// </remarks>
/// <param name="Key">Identifies the question in <see cref="DecisionResult.Answers"/>.</param>
/// <param name="Instructions">What is being asked, in words.</param>
public abstract record Question(string Key, string Instructions);
