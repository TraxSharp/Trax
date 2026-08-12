using System.Text.Json.Nodes;
using FluentAssertions;
using static Trax.Effect.StateMachine.Rules;

namespace Trax.Effect.StateMachine.Tests.UnitTests;

/// <summary>
/// A state's declarative validator is its context schema AND its <c>.Requires(...)</c> policy, composed. This
/// is what lets a state demand more than its shape (a complete draft) while sharing one context record across
/// states, and it is what the IR carries as the per-state <c>invariants</c> block.
/// </summary>
public class StateInvariantTests
{
    private enum S
    {
        Draft,
        Done,
    }

    private enum T
    {
        Finish,
    }

    private sealed record DraftContext
    {
        public string Body { get; init; } = "";
        public bool Guided { get; init; }
        public int[] Ids { get; init; } = [];
    }

    private static JsonObject Fresh() =>
        new()
        {
            ["body"] = "",
            ["guided"] = false,
            ["ids"] = new JsonArray(),
        };

    private static BuiltMachine<S, T> Build()
    {
        var m = new MachineBuilder<S, T>();
        m.Id("inv").StartsAt(S.Draft, Fresh);
        m.In(S.Draft).Context<DraftContext>().On(T.Finish).To(S.Done);
        m.In(S.Done)
            .Context<DraftContext>()
            .Requires(Field((DraftContext d) => d.Body).LengthAtLeast(6))
            .Requires(Field((DraftContext d) => d.Guided).IsTrue());
        return m.Build();
    }

    private static JsonObject DoneContext(string body, bool guided) =>
        new()
        {
            ["body"] = body,
            ["guided"] = guided,
            ["ids"] = new JsonArray(),
        };

    [Test]
    public void A_requirement_composes_with_the_schema_in_the_state_validator()
    {
        var validate = Build().Definition.ContextValidators[S.Done];

        validate(DoneContext("123456", guided: true))
            .Should()
            .BeNull("a complete, guided draft is valid in Done");
        validate(DoneContext("short", guided: true))
            .Should()
            .NotBeNull("Done requires a body of length >= 6");
        validate(DoneContext("123456", guided: false))
            .Should()
            .NotBeNull("Done requires guided = true");

        var wrongType = DoneContext("123456", guided: true);
        wrongType["body"] = 5;
        validate(wrongType)
            .Should()
            .NotBeNull("the schema is still enforced: body must be a string");
    }

    [Test]
    public void Requirements_export_as_a_per_state_invariants_block()
    {
        var ir = IrExporter.Export(Build());

        ir.Should()
            .Contain("\"invariants\"")
            .And.Contain("\"Done\"")
            .And.Contain("\"length\"")
            .And.Contain("\"boolEquals\"")
            .And.Contain("\"arrayOf\"", "the int[] field's element type is a schema constraint");
        // A shape-only machine emits no invariants block.
        DeclarativeTurnstileIrHasNoInvariants();
    }

    private static void DeclarativeTurnstileIrHasNoInvariants() =>
        IrExporter
            .Export(Fakes.DeclarativeTurnstile.Built)
            .Should()
            .NotContain("\"invariants\"", "turnstile has no .Requires policy");
}
