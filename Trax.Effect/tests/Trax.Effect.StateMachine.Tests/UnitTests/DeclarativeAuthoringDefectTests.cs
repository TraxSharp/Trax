using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using AwesomeAssertions;
using AwesomeAssertions.Execution;

namespace Trax.Effect.StateMachine.Tests.UnitTests;

/// <summary>
/// Declarative authoring promises that a context record yields its schema for free and that a
/// <see cref="Rule.Custom"/> / <see cref="Reduction.Custom"/> is the escape hatch "hand-written per runtime".
/// These pin both promises on the C# side.
/// </summary>
public class DeclarativeAuthoringDefectTests
{
    public enum Kind
    {
        Standard,
        Express,
    }

    public sealed record GuidContext(Guid OrderId);

    public sealed record TimeContext(DateTimeOffset PlacedAt);

    public sealed record EnumContext(Kind Kind);

    public sealed record UnsignedContext(uint Quantity);

    public sealed record MapContext(Dictionary<string, int> Counts);

    private static readonly JsonSerializerOptions CamelCase = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    private static string? ValidateOwnJson<TContext>(TContext value) =>
        SchemaValidator.Validate(
            SchemaReflection.For<TContext>(),
            (JsonObject)JsonSerializer.SerializeToNode(value, CamelCase)!
        );

    [Test]
    public void A_context_record_must_accept_its_own_serialized_form()
    {
        // Guid and DateTimeOffset serialize as strings, an enum as a string (or a number without the
        // converter), uint as a number and a dictionary as an object. The reflected schema calls the first
        // four "object" and the dictionary an "array" of strings, so each record's own JSON is rejected.
        using (new AssertionScope())
        {
            ValidateOwnJson(new GuidContext(Guid.NewGuid())).Should().BeNull("Guid");
            ValidateOwnJson(new TimeContext(DateTimeOffset.UnixEpoch))
                .Should()
                .BeNull("DateTimeOffset");
            ValidateOwnJson(new EnumContext(Kind.Express)).Should().BeNull("enum");
            ValidateOwnJson(new UnsignedContext(3)).Should().BeNull("uint");
            ValidateOwnJson(new MapContext(new() { ["a"] = 1 }))
                .Should()
                .BeNull("Dictionary<string,int>");
        }
    }

    public sealed record WideContext(
        Guid? MaybeId,
        DateOnly Day,
        TimeSpan Wait,
        ulong Big,
        sbyte Small,
        char Letter,
        List<Guid> Ids,
        Kind[] Kinds,
        IReadOnlyDictionary<string, string> Labels,
        Dictionary<int, bool> Flags
    );

    [Test]
    public void The_reflected_types_match_what_System_Text_Json_writes()
    {
        var value = new WideContext(
            null,
            new DateOnly(2026, 9, 28),
            TimeSpan.FromMinutes(5),
            ulong.MaxValue,
            -3,
            'x',
            [Guid.NewGuid()],
            [Kind.Standard, Kind.Express],
            new Dictionary<string, string> { ["a"] = "b" },
            new Dictionary<int, bool> { [1] = true }
        );

        ValidateOwnJson(value).Should().BeNull();

        var fields = SchemaReflection.For<WideContext>().Fields.ToDictionary(f => f.Name);
        fields["labels"].Type.Should().Be(JsonFieldType.Object);
        fields["labels"]
            .Constraints.Should()
            .BeEmpty("a dictionary's key type is not an element type");
        fields["ids"]
            .Constraints.Should()
            .ContainSingle()
            .Which.Should()
            .Be(new Rule.ArrayOf(RuleSource.Context, "ids", JsonFieldType.String));
    }

    public sealed record LooseContext(object Anything);

    public sealed record RawNodeContext(JsonNode Payload);

    [Test]
    public void A_field_with_no_fixed_JSON_type_is_refused_when_the_context_is_declared()
    {
        var builder = new MachineBuilder<S, T>();

        Action loose = () => builder.In(S.A).Context<LooseContext>();
        Action raw = () => builder.In(S.A).Context<RawNodeContext>();

        loose.Should().Throw<InvalidOperationException>().WithMessage("*LooseContext.Anything*");
        raw.Should().Throw<InvalidOperationException>().WithMessage("*RawNodeContext.Payload*");
    }

    public enum S
    {
        A,
        B,
    }

    public enum T
    {
        Go,
    }

    [TestCase(true, "B", null)]
    [TestCase(false, "A", RejectionReasons.GuardFailed)]
    public void A_custom_guard_bound_on_the_builder_decides_the_transition(
        bool verdict,
        string expectedState,
        string? expectedRejection
    )
    {
        var builder = new MachineBuilder<S, T>();
        builder.Id("custom").StartsAt(S.A, () => new JsonObject());
        builder.CustomGuard("always", (_, _) => verdict);
        builder.In(S.A).Context().On(T.Go).When(new Rule.Custom("always")).To(S.B);
        builder.In(S.B).Context();
        var engine = builder.Build().Engine;

        var result = engine.Advance(engine.Definition.CreateInitialSnapshot(), "Go");

        if (expectedRejection is null)
            result
                .Should()
                .BeOfType<AdvanceResult.Transitioned>()
                .Which.Snapshot.State.Should()
                .Be(expectedState);
        else
            result
                .Should()
                .BeOfType<AdvanceResult.Rejected>()
                .Which.Reason.Should()
                .Be(expectedRejection);
    }

    [Test]
    public void A_custom_guard_bound_on_the_builder_resolves_inside_a_state_requirement()
    {
        var builder = new MachineBuilder<S, T>();
        builder.Id("custom").StartsAt(S.A, () => new JsonObject());
        builder.In(S.A).On(T.Go).To(S.B);
        builder.In(S.B).Requires(Rules.Any(new Rule.Custom("hasFlag")));
        builder.CustomGuard("hasFlag", (context, _) => context.ContainsKey("flag"));
        var engine = builder.Build().Engine;

        engine
            .Advance(engine.Definition.CreateInitialSnapshot(), "Go")
            .Should()
            .BeOfType<AdvanceResult.Rejected>()
            .Which.Reason.Should()
            .Be(RejectionReasons.InvalidContext);
        engine
            .Rehydrate(
                "{\"machine\":\"custom\",\"version\":1,\"state\":\"B\",\"context\":{\"flag\":1}}"
            )
            .Should()
            .BeOfType<RehydrationResult.Ok>();
    }

    [Test]
    public void A_custom_reducer_bound_on_the_builder_produces_the_destination_context()
    {
        var builder = new MachineBuilder<S, T>();
        builder.Id("custom").StartsAt(S.A, () => new JsonObject { ["n"] = 1 });
        builder.CustomReducer(
            "fresh",
            (context, input) => new JsonObject { ["n"] = input?["n"]?.DeepClone() }
        );
        builder.In(S.A).On(T.Go).Reduce(new Reduction.Custom("fresh")).To(S.B);
        var engine = builder.Build().Engine;

        var result = engine.Advance(
            engine.Definition.CreateInitialSnapshot(),
            "Go",
            new JsonObject { ["n"] = 7 }
        );

        result.Should().BeOfType<AdvanceResult.Transitioned>().Which.Snapshot.Context["n"]!
            .GetValue<int>()
            .Should()
            .Be(7);
    }

    [Test]
    public void A_custom_guard_with_no_CSharp_handler_must_be_refused_at_build()
    {
        var builder = new MachineBuilder<S, T>();
        builder.Id("custom").StartsAt(S.A, () => new JsonObject());
        builder.In(S.A).Context().On(T.Go).When(new Rule.Custom("always")).To(S.B);

        // With no handler bound, the compiled guard would be false forever while a TypeScript twin given its
        // handler takes the edge, so the machine is refused when it is built.
        Action build = () => builder.Build();

        build.Should().Throw<InvalidOperationException>().WithMessage("*always*");
    }

    [Test]
    public void A_custom_guard_nested_in_a_state_requirement_with_no_handler_is_refused_at_build()
    {
        var builder = new MachineBuilder<S, T>();
        builder.Id("custom").StartsAt(S.A, () => new JsonObject());
        builder.In(S.A).Requires(Rules.All(new Rule.Custom("deep")));

        Action build = () => builder.Build();

        build.Should().Throw<InvalidOperationException>().WithMessage("*deep*");
    }

    [Test]
    public void A_custom_reducer_with_no_CSharp_handler_must_be_refused_at_build()
    {
        var builder = new MachineBuilder<S, T>();
        builder.Id("custom").StartsAt(S.A, () => new JsonObject());
        builder.In(S.A).Context().On(T.Go).Reduce(new Reduction.Custom("fresh")).To(S.B);

        // With no handler bound the reducer would silently carry the context forward instead of running "fresh".
        Action build = () => builder.Build();

        build.Should().Throw<InvalidOperationException>().WithMessage("*fresh*");
    }
}
