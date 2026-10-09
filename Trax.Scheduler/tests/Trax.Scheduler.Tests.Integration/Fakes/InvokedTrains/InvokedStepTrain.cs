using Trax.Core.Functional;
using Trax.Effect.Services.EffectJunction;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Scheduler.Tests.Integration.Fakes.InvokedTrains;

/// <summary>
/// What an <see cref="IInvokedStepTrain"/> run does, named by its input: <c>ok</c> succeeds; <c>chain</c> succeeds
/// with an output that routes to a state invoking a run of its own; <c>unaccepted</c> succeeds with an output no
/// <c>OnDone</c> guard accepts; <c>fail</c> fails; <c>big</c> succeeds with an output past 64 KiB; <c>grow</c>
/// succeeds with an output under the cap that grows the snapshot past it; <c>gate</c> waits at
/// <see cref="InvokedStepGate"/> between its two junctions.
/// </summary>
public static class InvokedStepModes
{
    public const string Ok = "ok";
    public const string Chain = "chain";
    public const string Unaccepted = "unaccepted";
    public const string Fail = "fail";
    public const string Big = "big";
    public const string Grow = "grow";
    public const string Gate = "gate";

    /// <summary>Short-circuits, in <see cref="IInvokedShortCircuitTrain"/>.</summary>
    public const string Short = "short";

    /// <summary>Succeeds with an artifact that is the note reversed, so it appears in no input.</summary>
    public const string Secret = "secret";

    public static string Reversed(string note) => new(note.Reverse().ToArray());
}

public sealed record InvokedStepInput(string Mode, string Note);

/// <summary>A pointer, never data, except where a test needs a large one.</summary>
public sealed record InvokedStepOutput
{
    public string Artifact { get; init; } = "";

    public bool Accepted { get; init; }

    public bool Chain { get; init; }
}

public sealed record InvokedStepGated(string Mode, string Note);

public interface IInvokedStepTrain : IServiceTrain<InvokedStepInput, InvokedStepOutput>;

/// <summary>Two effect junctions, so the run reads its cancel flag between them.</summary>
public class InvokedStepTrain : ServiceTrain<InvokedStepInput, InvokedStepOutput>, IInvokedStepTrain
{
    protected override Task<Either<Exception, InvokedStepOutput>> Junctions() =>
        Chain<InvokedStepEnter>().Chain<InvokedStepFinish>().Resolve();
}

/// <summary>A test's hold on a <c>gate</c> run: it signals once the run is inside, and waits for the test to release it.</summary>
public static class InvokedStepGate
{
    private static TaskCompletionSource _entered = New();
    private static TaskCompletionSource _release = New();

    public static Task Entered => _entered.Task;

    public static void Release() => _release.TrySetResult();

    public static Task Released => _release.Task;

    public static void Reset()
    {
        _release.TrySetResult();
        _entered = New();
        _release = New();
    }

    internal static void Enter() => _entered.TrySetResult();

    private static TaskCompletionSource New() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);
}

public sealed class InvokedStepEnter : EffectJunction<InvokedStepInput, InvokedStepGated>
{
    public override async Task<InvokedStepGated> Run(InvokedStepInput input)
    {
        if (input.Mode == InvokedStepModes.Gate)
        {
            InvokedStepGate.Enter();
            await InvokedStepGate.Released.WaitAsync(CancellationToken);
        }

        return new InvokedStepGated(input.Mode, input.Note);
    }
}

public sealed class InvokedStepFinish : EffectJunction<InvokedStepGated, InvokedStepOutput>
{
    public override Task<InvokedStepOutput> Run(InvokedStepGated input) =>
        input.Mode switch
        {
            InvokedStepModes.Fail => throw new InvalidOperationException("the invoked step failed"),
            InvokedStepModes.Chain => Task.FromResult(
                new InvokedStepOutput { Artifact = "chain:" + input.Note, Chain = true }
            ),
            InvokedStepModes.Unaccepted => Task.FromResult(
                new InvokedStepOutput { Artifact = "unaccepted:" + input.Note }
            ),
            InvokedStepModes.Big => Task.FromResult(
                new InvokedStepOutput { Artifact = new string('b', 70 * 1024), Accepted = true }
            ),
            InvokedStepModes.Secret => Task.FromResult(
                new InvokedStepOutput
                {
                    Artifact = InvokedStepModes.Reversed(input.Note),
                    Accepted = true,
                }
            ),
            InvokedStepModes.Grow => Task.FromResult(
                new InvokedStepOutput { Artifact = new string('g', 40 * 1024), Accepted = true }
            ),
            _ => Task.FromResult(
                new InvokedStepOutput { Artifact = "artifact:" + input.Note, Accepted = true }
            ),
        };
}
