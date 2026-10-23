using System.Text.Json.Nodes;

namespace Trax.StateMachine.Tests.Fakes;

public enum VaultState
{
    Draft,
    Stamped,
    Sealed,
}

public enum VaultTrigger
{
    Seal,
    Stamp,
    Force,
    Detonate,
}

/// <summary>
/// A machine whose rules are custom: <c>Seal</c> is guarded by the custom rule <c>approved</c>, <c>Stamp</c>
/// reduces with the custom reduction <c>stamp</c>, <c>Sealed</c> requires the custom rule <c>sealable</c>, and
/// <c>Detonate</c> is guarded by <c>throws</c>. Its exported IR is the committed <c>machines/vault/vault.ir.json</c>,
/// which the conformance kit serves with no handler bound for the first three, so it pins what an engine does with a
/// custom rule it cannot evaluate: the guard does not hold, the reduction keeps the context, and the requirement
/// fails, except on a snapshot the server sent. Every testee binds <c>throws</c> to a handler that throws, so
/// <c>Detonate</c> pins <c>INTERNAL_ERROR</c>. The handlers here are bound only because Build refuses a custom name
/// without one.
/// </summary>
public static class VaultMachine
{
    public sealed record VaultContext
    {
        public string Title { get; init; } = "";
    }

    public static readonly BuiltMachine<VaultState, VaultTrigger> Built = Build();

    private static BuiltMachine<VaultState, VaultTrigger> Build()
    {
        var m = new MachineBuilder<VaultState, VaultTrigger>();
        m.Id("vault")
            .Version(1)
            .StartsAt(VaultState.Draft, () => new JsonObject { ["title"] = "memo" });

        m.In(VaultState.Draft)
            .Context<VaultContext>()
            .On(VaultTrigger.Seal)
            .When(new Rule.Custom("approved"))
            .To(VaultState.Sealed)
            .On(VaultTrigger.Stamp)
            .Reduce(new Reduction.Custom("stamp"))
            .To(VaultState.Stamped)
            .On(VaultTrigger.Force)
            .To(VaultState.Sealed)
            .On(VaultTrigger.Detonate)
            .When(new Rule.Custom("throws"))
            .To(VaultState.Sealed);

        m.In(VaultState.Stamped).Context<VaultContext>();
        m.In(VaultState.Sealed).Context<VaultContext>().Requires(new Rule.Custom("sealable"));

        m.CustomGuard("approved", (_, _) => true)
            .CustomGuard("sealable", (_, _) => true)
            .CustomGuard("throws", (_, _) => throw new InvalidOperationException("throws"))
            .CustomReducer(
                "stamp",
                (context, _) => new JsonObject { ["title"] = $"{context["title"]} (stamped)" }
            );

        return m.Build();
    }
}
