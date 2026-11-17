namespace Trax.Core.Decisions;

/// <summary>
/// The question a choice, scale or yes/no type asks, written once on the type.
/// </summary>
/// <remarks>
/// Put it on the enum a <c>Switch</c> or <c>Scale</c> routes on, or on the marker type a
/// <c>Gate</c> routes on. A model sees only the question and the criteria, never the type's name,
/// so the question has to say everything that is being judged. An <c>asking:</c> argument where
/// the decision is declared overrides it.
/// </remarks>
[AttributeUsage(AttributeTargets.Enum | AttributeTargets.Class | AttributeTargets.Struct)]
public sealed class AsksAttribute(string question) : Attribute
{
    /// <summary>What is being asked.</summary>
    public string Question { get; } = question;

    /// <summary>For a yes/no question, what a yes means.</summary>
    public string? Yes { get; init; }

    /// <summary>For a yes/no question, what a no means.</summary>
    public string? No { get; init; }

    /// <summary>
    /// The key the question is asked under, in place of the type's name: 1 to
    /// <see cref="QuestionKey.MaxLength"/> ASCII letters, digits, <c>_</c>, <c>-</c> or <c>.</c>,
    /// such as <c>refund_choice</c>.
    /// </summary>
    /// <remarks>
    /// The key identifies the question to a decider, to a model's adapter, and to a replay of an
    /// earlier run. Set it to keep the key a type had before it was renamed, so a requeued run
    /// still replays what the original answered, or to tell apart two types in one train that
    /// share a name. See <see cref="QuestionKey"/>.
    /// </remarks>
    public string? Key { get; init; }
}
