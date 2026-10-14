---
title: Test a train's decisions
description: "Drive a deciding train down each of its tracks in a unit test with ScriptedDecider, and check what a model would have been asked."
type: how-to
area: testing
nav_order: 4
source:
  - Trax.Core/src/Trax.Core/Decisions/ScriptedDecider.cs
---

# Test a train's decisions

`ScriptedDecider`, in `Trax.Core.Decisions`, answers each question with what the test says and
keeps every request it was asked, so a test can send a train down any track without a model.
The train here is an underwriter whose `Approve` and `Decline` tracks need a confidence of 0.95
and which declares no `Otherwise` track (see [Decisions](/docs/core/decisions#when-the-answer-is-not-followed)).

## Script the answers

The examples use NUnit and AwesomeAssertions, as Trax's own tests do. Hand the train a
`ScriptedDecider` that answers the way the case needs, run it, and assert on the
result and on `Requests`:

```csharp fragment
using AwesomeAssertions;
using NUnit.Framework;
using Trax.Core.Decisions;

public class UnderwriteLoanTests
{
    [Test]
    public async Task AnUnsureApproval_FailsTheRun()
    {
        var decider = new ScriptedDecider().Choose(Underwriting.Approve, confidence: 0.62);

        var result = await new UnderwriteLoan(decider).RunEither(
            new LoanApplication("a3", 700, 0.4m)
        );

        result.IsLeft.Should().BeTrue(); // below Approve's 0.95, and no Otherwise
        decider.Requests.Should().ContainSingle(); // what a model would have been shown
    }

    [Test]
    public async Task ManualReview_NeedsNoConfidence()
    {
        var decider = new ScriptedDecider().Choose(Underwriting.ManualReview, confidence: 0.1);

        var decision = await new UnderwriteLoan(decider).Run(new LoanApplication("a4", 690, 0.4m));

        decision.Outcome.Should().Be("assigned to an underwriter");
    }
}
```

The train takes its decider as a constructor argument and hands it to the chain with
`AddServices`, so the test needs no container. A train that resolves its decider from the
container gets the `ScriptedDecider` registered as its `IDecider` instead.

## What ScriptedDecider can answer

| Method | Answers |
|---|---|
| `Choose<TTrack>(choice, confidence, probabilities, model)` | The choice question about `TTrack` with that member. |
| `Score<TLevel>(score, confidence, model)` | The score question about `TLevel` with that position. |
| `YesNo<TQuestion>(probability, model)` | The yes or no question about `TQuestion`. |
| `Answer(key, answer)` | The question with that key, with whatever the function returns, including an answer that does not fit, to test how the train refuses it. |
| `Throws(exception)` | Nothing: every request fails with the exception, as a model that is down would. |

| Property | Holds |
|---|---|
| `Requests` | Every `DecisionRequest` it was asked, in order: the train's name, the state, and the questions with their words and options. |
| `LastToken` | The cancellation token the last request was given. |

A question it has no answer for is left unanswered, which fails the run the way a model that
skipped one would.

## Check the declaration as well

A scripted answer sends the run down one track. To check every track, verify the declared chain,
which is what the host's startup check does for each registered train:

```csharp fragment
using AwesomeAssertions;
using NUnit.Framework;
using Trax.Core.Decisions;
using Trax.Core.Monad;

public class UnderwriteLoanChainTests
{
    [Test]
    public void EveryTrackVerifies() =>
        ChainVerification
            .Verify(
                new UnderwriteLoan(new ScriptedDecider()).DeclaredChain(),
                typeof(LoanApplication),
                typeof(LoanDecision)
            )
            .Should()
            .BeEmpty();
}
```

See [DeclaredChain](/docs/sdk-reference/train-methods/declared-chain) for what the recording holds.

## SDK Reference

> [Switch](/docs/sdk-reference/train-methods/switch) | [AddServices](/docs/sdk-reference/train-methods/add-services) | [Run / RunEither](/docs/sdk-reference/train-methods/run) | [DeclaredChain](/docs/sdk-reference/train-methods/declared-chain)
