---
title: Try a decider in shadow
description: "Run a candidate decider beside the live one on real traffic, with its answers recorded and never acted on, before switching a train over to it."
type: how-to
area: trains
nav_order: 6
source:
  - Trax.Core/src/Trax.Core/Decisions/Questions.cs
  - Trax.Core/src/Trax.Core/Decisions/Tracks.cs
---

# Try a decider in shadow

A shadow is asked every question the live decider is asked, alongside it. Its answers are recorded
with whether each agreed, and never acted on: a shadow that fails, disagrees or is slow changes
nothing. Use one to move from a rule table to a model, or from one model version to the next.
[Decisions](/docs/core/decisions#comparing-a-new-decider-without-trusting-it) explains the design.

## 1. Give the candidate its own type

A shadow is named by type, so the candidate needs a type the live decider does not have. An
interface that extends `IDecider` works both from `AddServices` and from the container:

```csharp compile=shadow
using Trax.Core.Decisions;

public interface ICandidateDecider : IDecider;
```

A shadow handed to `AddServices` is passed as an interface, as every service there is. From the
container, register it under the type the step names; a concrete class works there too.

## 2. Declare the shadow on the step that asks

On a `Decide`, add `Shadow<T>()` to the questions:

```csharp compile=shadow
using Trax.Core.Decisions;
using Trax.Core.Functional;
using Trax.Core.Train;

public class ModeratePost(IDecider decider, ICandidateDecider candidate)
    : Train<Post, ModerationOutcome>
{
    protected override Task<Either<Exception, ModerationOutcome>> Junctions() =>
        AddServices(decider, candidate)
            .Decide<Post>(q => q.Choice<Verdict>().Shadow<ICandidateDecider>())
            .Switch<Verdict>(tracks =>
                tracks
                    .When(Verdict.Allow, t => t.Chain<PublishPost>())
                    .When(Verdict.Remove, t => t.Chain<TakeDownPost>())
                    .When(Verdict.Review, t => t.Chain<QueueForModerator>())
            )
            .Resolve();
}
```

A `Switch`, `Gate` or `Scale` that asks its own question declares shadows on its tracks instead:

```csharp compile=shadow
using Trax.Core.Decisions;
using Trax.Core.Functional;
using Trax.Core.Train;

public class ModeratePostInline(IDecider decider, ICandidateDecider candidate)
    : Train<Post, ModerationOutcome>
{
    protected override Task<Either<Exception, ModerationOutcome>> Junctions() =>
        AddServices(decider, candidate)
            .Switch<Post, Verdict>(tracks =>
                tracks
                    .When(Verdict.Allow, t => t.Chain<PublishPost>())
                    .Otherwise(t => t.Chain<QueueForModerator>())
                    .Shadow<ICandidateDecider>()
                    .WaitForShadows(TimeSpan.FromSeconds(2))
            )
            .Resolve();
}
```

A routing step that only routes on an earlier `Decide` asks nothing, so a shadow declared on it is
refused at startup; shadow the `Decide`. A shadow that neither `AddServices` nor the container
supplies is refused at startup too, and a run that reaches it fails `Permanent`, as it does for a
missing live decider.

## 3. Bound the wait

Once the live answer is in, the run waits for the shadows at most `WaitForShadows(TimeSpan)`, five
seconds by default, then cancels them and records them as not having answered, whether or not they
honour the cancellation. Every run can wait that long for a slow shadow, so keep it short. Zero
waits only for shadows that have already answered. A negative or unbounded wait is refused at
startup.

## 4. Read the results

Each shadow's answer arrives with the live one, in `DecisionMade.Shadows`, as a `ShadowAnswer`:
the shadow's type, its `Answer` (null when it gave none), `Agrees`, and `Error`. Trax.Effect's
decision recording stores them in the `shadows` column of the run's decisions; see
[Recording decisions](/docs/effect/decisions#recording-decisions). A host that records some other
way implements `IDecisionObserver` ([Decision replay and state hash rules](/docs/core/decision-replay#idecisionobserver)).

What `Agrees` means depends on the step:

| Shadow on | Agrees when |
|---|---|
| A `Switch`, `Gate` or `Scale` that asks its own question | The shadow's answer would have sent the train down the same track, by the step's own bars and bands. |
| A `Decide` | The same option, the same nearest level, or the same side of one half for a yes/no. |

A shadow whose answer does not fit the question never agrees, and neither does any shadow when the
live answer takes no track (a gate's band with no `Unsure` track, a choice with no track and no
`Otherwise`). A question whose answer is replayed is not put to the shadows at all.

## 5. Switch over

When the shadow agrees often enough on the cases that matter, register it as the live
`IDecider` and remove the `Shadow<T>()` call, or keep the old decider as a shadow for a while to
watch the change from the other side.

## What a shadow can touch

- **Its own scope.** A shadow from the container is built in a DI scope of its own, disposed when
  the shadow ends, so it never shares the run's scoped services (a `DbContext`, say) with the live
  decider or with the junctions after the decision. A shadow that has not finished a second after
  it was cancelled has its scope disposed under it, on a timer the run never waits for, and
  whatever it then fails with is recorded as its own failure. One handed to `AddServices` is the
  instance the run holds, used as it is.
- **Its own copy of the state.** The state is written to JSON once per asking, with the web
  defaults the System One adapter uses, and read back separately for each shadow, so a shadow that
  changes its copy changes nothing the run or another shadow sees. A state type JSON cannot write
  and read back is refused at startup when shadows are declared on it and the type alone says so;
  otherwise a shadow whose copy cannot be made is recorded as not having answered, and the run
  goes on.

## SDK Reference

> [Decide](/docs/sdk-reference/train-methods/decide) | [Switch](/docs/sdk-reference/train-methods/switch) | [Gate](/docs/sdk-reference/train-methods/gate) | [Scale](/docs/sdk-reference/train-methods/scale) | [AddServices](/docs/sdk-reference/train-methods/add-services)
