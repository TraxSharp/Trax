using System.Security.Cryptography;
using System.Text.Json;

namespace Trax.Core.Decisions;

/// <summary>
/// Identifies one asking of a question as the chain declares it, so a replay can tell an answer
/// recorded for this asking from one recorded for a different asking of the same key.
/// </summary>
/// <remarks>
/// It covers the step that asks, the type the step asks about, the kind of question, its key, its
/// instructions, and every option or level with its description (or what a yes and a no mean).
/// It never covers the state's value, which differs from run to run by design. Any change to what
/// it covers, such as a reworded question, an option added or removed, or another step asking the
/// same question inserted ahead of it, gives a different fingerprint, and the recorded answer is
/// not replayed.
/// </remarks>
internal static class QuestionFingerprint
{
    public static string Of(string step, Type state, Question question)
    {
        var parts = new List<string?> { step, QuestionKey.For(state) };

        switch (question)
        {
            case ChoiceQuestion choice:
                parts.Add("choice");
                Criteria(choice.Options);
                break;
            case ScoreQuestion score:
                parts.Add("score");
                Criteria(score.Levels);
                break;
            case YesNoQuestion yesNo:
                parts.AddRange(["yesno", yesNo.Yes, yesNo.No]);
                break;
            default:
                parts.Add(question.GetType().FullName);
                break;
        }

        parts.AddRange([question.Key, question.Instructions]);

        // A JSON array of the parts, so no two lists of parts serialize to the same text.
        var canonical = JsonSerializer.SerializeToUtf8Bytes(parts);

        return Convert.ToHexStringLower(SHA256.HashData(canonical));

        void Criteria(IEnumerable<Criterion> criteria)
        {
            foreach (var criterion in criteria)
                parts.AddRange([criterion.Name, criterion.Description]);
        }
    }
}
