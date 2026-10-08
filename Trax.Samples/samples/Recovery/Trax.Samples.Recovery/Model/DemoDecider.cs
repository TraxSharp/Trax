using Trax.Core.Decisions;
using Trax.Samples.Recovery.Trains.Ingest;
using Trax.Samples.Recovery.Trains.Refund;
using Trax.Samples.Recovery.Trains.Research;
using Trax.Samples.Recovery.Trains.Topics;

namespace Trax.Samples.Recovery.Model;

/// <summary>
/// A stand-in for a decision model: it takes as long as one (0.5 to 1.5 seconds by default) and
/// answers the same way every time for the same state, so a run can be repeated and compared. Set
/// <c>Recovery:Model</c> to <c>Nimble</c> to ask a Nimble server instead.
/// </summary>
/// <remarks>
/// The answers read only the state they are handed. A decider that looked something up elsewhere would
/// defeat the replay check, which compares the hash of the state and nothing else.
/// </remarks>
public sealed class DemoDecider(DemoPace pace) : IDecider
{
    public const string ModelName = "demo-rules-1.0";

    public async Task<DecisionResult> Decide(
        DecisionRequest request,
        CancellationToken cancellationToken
    )
    {
        await Task.Delay(Latency(), cancellationToken);
        return Answer(request);
    }

    /// <summary>The answers, without the latency.</summary>
    public static DecisionResult Answer(DecisionRequest request)
    {
        var answers = new Dictionary<string, Answer>();
        foreach (var question in request.Questions)
            answers[question.Key] = (request.State, question) switch
            {
                (ResearchBrief brief, ChoiceQuestion) => ChooseSource(brief),
                (Findings findings, ScoreQuestion) => ScoreDepth(findings),
                (RefundCase refund, YesNoQuestion) => ApproveRefund(refund),
                (CoCitationEvidence evidence, YesNoQuestion) => SameTopic(evidence),
                (MatchEvidence evidence, YesNoQuestion) => SameWork(evidence),
                _ => throw new InvalidOperationException(
                    $"The demo model cannot answer {question.Key} about {request.State.GetType().Name}."
                ),
            };
        return new DecisionResult(answers);
    }

    private static ChoiceAnswer ChooseSource(ResearchBrief brief)
    {
        if (brief.Audience == "executives")
            return new ChoiceAnswer(nameof(Source.Web), 0.81) { Model = ModelName };

        var topic = brief.Topic.ToLowerInvariant();
        return topic.Contains("study") || topic.Contains("paper") || topic.Contains("research")
            ? new ChoiceAnswer(nameof(Source.Papers), 0.88) { Model = ModelName }
            : new ChoiceAnswer(nameof(Source.Wiki), 0.74) { Model = ModelName };
    }

    private static ScoreAnswer ScoreDepth(Findings findings) =>
        findings.Source == nameof(Source.Web)
            ? new ScoreAnswer(0.2, 0.77) { Model = ModelName }
            : new ScoreAnswer(0.9, 0.83) { Model = ModelName };

    private static YesNoAnswer ApproveRefund(RefundCase refund)
    {
        var probability = refund.Amount <= 250m ? 0.93 : 0.62;
        probability -= 0.35 * refund.PriorRefunds;
        return new YesNoAnswer(Math.Round(Math.Clamp(probability, 0.02, 0.98), 2))
        {
            Model = ModelName,
        };
    }

    // Shared references mean more the more of the slice they connect: in a slice where most papers
    // cite something another one cites, they mark topics; where few do, they are noise.
    private static YesNoAnswer SameTopic(CoCitationEvidence evidence)
    {
        var linked = evidence.Papers == 0 ? 0 : (double)evidence.LinkedPapers / evidence.Papers;
        return new YesNoAnswer(Math.Round(Math.Clamp(0.1 + 0.9 * linked, 0.02, 0.98), 2))
        {
            Model = ModelName,
        };
    }

    // Titles that match word for word are the same paper; titles that are only close might not be.
    // With nothing close, there is nothing to merge.
    private static YesNoAnswer SameWork(MatchEvidence evidence)
    {
        var probability =
            evidence.Candidates.Count == 0 ? 0.05
            : evidence.Candidates.All(c => c.Similarity >= 0.9) ? 0.95
            : 0.55;
        return new YesNoAnswer(probability) { Model = ModelName };
    }

    private TimeSpan Latency()
    {
        var min = pace.ModelLatencyMin;
        var max = pace.ModelLatencyMax;
        if (max <= min)
            return min;
        return min
            + TimeSpan.FromMilliseconds(Random.Shared.NextDouble() * (max - min).TotalMilliseconds);
    }
}
