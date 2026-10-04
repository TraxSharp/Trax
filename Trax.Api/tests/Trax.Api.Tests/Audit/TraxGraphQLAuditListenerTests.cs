using System.Collections.Concurrent;
using System.Security.Claims;
using System.Text.Json.Nodes;
using AwesomeAssertions;
using HotChocolate;
using HotChocolate.Execution;
using HotChocolate.Types;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Trax.Api.Auth;
using Trax.Api.GraphQL.Audit;

namespace Trax.Api.Tests.Audit;

/// <summary>
/// Drives <see cref="TraxGraphQLAuditListener"/> through a real HotChocolate
/// request pipeline and inspects the entries it enqueues. Covers the zero /
/// one / many variable paths (the direct trigger for the CloudWatch cast bug)
/// plus the full ShouldSkip / redactor / truncation / principal / result
/// interpretation branches.
/// <para>Enforces <c>docs/adr/0027-an-audit-entry-records-no-value-the-caller-sent-unless-the-host-opts-in.md</c>: no literal from the document and no variable reaches the entry
/// unless the host's redactor returns it.</para>
/// <para>Enforces <c>docs/adr/0035-a-refused-request-is-always-audited.md</c>: the skip options
/// drop only a request that succeeded, so a refused or failed subscription is audited.</para>
/// </summary>
[Property(
    "adr",
    "docs/adr/0027-an-audit-entry-records-no-value-the-caller-sent-unless-the-host-opts-in.md"
)]
[Property("adr", "docs/adr/0035-a-refused-request-is-always-audited.md")]
[TestFixture]
public class TraxGraphQLAuditListenerTests
{
    private const string Adr =
        "docs/adr/0027-an-audit-entry-records-no-value-the-caller-sent-unless-the-host-opts-in.md";

    private const string RefusalAdr = "docs/adr/0035-a-refused-request-is-always-audited.md";

    #region BuildVariables — repro and coverage

    [Test]
    public async Task BuildVariables_ZeroVariables_ProducesEntryWithNullVariables()
    {
        // Regression: context.Variables is IReadOnlyList<IVariableValueCollection>
        // in HC 15.x. The old direct cast to IEnumerable<VariableValue> threw on
        // every request, including zero-variable queries, and the listener's
        // catch swallowed it so no entry was ever enqueued.
        await using var host = await TestHost.BuildAsync();

        var result = await host.Executor.ExecuteAsync("{ ping }");
        AssertNoErrors(result);

        var entries = host.DrainEntries();
        entries.Should().HaveCount(1);
        entries[0].Variables.Should().BeNull();
        entries[0].Success.Should().BeTrue();
    }

    [Test]
    public async Task BuildVariables_SingleScalarVariable_CapturesName()
    {
        await using var host = await TestHost.BuildAsync(configureServices: KeepAllVariables);

        var result = await host.Executor.ExecuteAsync(
            QueryRequestBuilder("query Q($s: String!) { echo(s: $s) }")
                .SetVariableValues(new Dictionary<string, object?> { ["s"] = "hi" })
                .Build()
        );
        AssertNoErrors(result);

        var entries = host.DrainEntries();
        entries.Should().HaveCount(1);
        entries[0].Variables.Should().NotBeNull();
        entries[0].Variables!["s"]!.GetValue<string>().Should().Be("hi");
    }

    [Test]
    public async Task BuildVariables_MultipleMixedTypes_CapturesAll()
    {
        await using var host = await TestHost.BuildAsync(configureServices: KeepAllVariables);

        var variables = new Dictionary<string, object?>
        {
            ["s"] = "hello",
            ["i"] = 42,
            ["e"] = "HAPPY",
            ["list"] = new[] { 1, 2, 3 },
            ["obj"] = new Dictionary<string, object?> { ["name"] = "bob", ["count"] = 7 },
        };

        var result = await host.Executor.ExecuteAsync(
            QueryRequestBuilder(
                    "query Q($s: String!, $i: Int!, $e: Mood!, $list: [Int!]!, $obj: FooInput!) "
                        + "{ complex(s: $s, i: $i, e: $e, list: $list, obj: $obj) }"
                )
                .SetVariableValues(variables)
                .Build()
        );
        AssertNoErrors(result);

        var entries = host.DrainEntries();
        entries.Should().HaveCount(1);
        var captured = entries[0].Variables;
        captured.Should().NotBeNull();
        captured!.Select(p => p.Key).Should().BeEquivalentTo(["s", "i", "e", "list", "obj"]);
        captured["s"]!.GetValue<string>().Should().Be("hello");
        captured["i"]!.GetValue<int>().Should().Be(42);
        captured["e"]!.GetValue<string>().Should().Be("HAPPY");
        captured["list"]!.AsArray().Select(n => n!.GetValue<int>()).Should().Equal(1, 2, 3);
        captured["obj"]!["name"]!.GetValue<string>().Should().Be("bob");
        captured["obj"]!["count"]!.GetValue<int>().Should().Be(7);
    }

    [Test]
    public async Task BuildVariables_NullFloatAndBooleanValues_KeepTheirJsonShape()
    {
        await using var host = await TestHost.BuildAsync(configureServices: KeepAllVariables);

        var result = await host.Executor.ExecuteAsync(
            QueryRequestBuilder(
                    "query Q($f: Decimal!, $n: String, $b: Boolean!) "
                        + "{ decimal(f: $f) maybe(s: $n) @include(if: $b) }"
                )
                .SetVariableValues(
                    new Dictionary<string, object?>
                    {
                        ["f"] = 3.25m,
                        ["n"] = null,
                        ["b"] = true,
                    }
                )
                .Build()
        );
        AssertNoErrors(result);

        var captured = host.DrainEntries().Should().ContainSingle().Subject.Variables!;
        captured["f"]!.GetValue<decimal>().Should().Be(3.25m);
        captured.ContainsKey("n").Should().BeTrue();
        captured["n"].Should().BeNull();
        captured["b"]!.GetValue<bool>().Should().BeTrue();
    }

    [Test]
    public async Task DefaultRedactor_RecordsNoVariables()
    {
        await using var host = await TestHost.BuildAsync();

        var result = await host.Executor.ExecuteAsync(
            QueryRequestBuilder("query Q($s: String!) { echo(s: $s) }")
                .SetVariableValues(new Dictionary<string, object?> { ["s"] = "hunter2" })
                .Build()
        );
        AssertNoErrors(result);

        host.DrainEntries()
            .Should()
            .ContainSingle()
            .Which.Variables.Should()
            .BeNull($"the default redactor records no variables ({Adr})");
    }

    private static void KeepAllVariables(IServiceCollection services) =>
        services.AddSingleton<ITraxAuditRedactor>(new KeepAllRedactor());

    #endregion

    #region Result interpretation

    [Test]
    public async Task SuccessfulQuery_EntryHasSuccessTrueAndPositiveDuration()
    {
        await using var host = await TestHost.BuildAsync();

        var before = DateTimeOffset.UtcNow.AddSeconds(-1);
        var result = await host.Executor.ExecuteAsync(
            QueryRequestBuilder("query Ping { ping }").SetOperationName("Ping").Build()
        );
        var after = DateTimeOffset.UtcNow.AddSeconds(1);
        AssertNoErrors(result);

        var entries = host.DrainEntries();
        entries.Should().HaveCount(1);
        var entry = entries[0];
        entry.Success.Should().BeTrue();
        entry.ErrorText.Should().BeNull();
        entry.DurationMs.Should().BeGreaterThanOrEqualTo(0);
        entry.Timestamp.Should().BeOnOrAfter(before).And.BeOnOrBefore(after);
        entry.OperationName.Should().Be("Ping");
    }

    [Test]
    public async Task QueryWithErrors_EntryHasSuccessFalseAndErrorText()
    {
        await using var host = await TestHost.BuildAsync();

        var result = await host.Executor.ExecuteAsync("{ notARealField }");
        (result as OperationResult)!.Errors.Should().NotBeNullOrEmpty();

        var entries = host.DrainEntries();
        entries.Should().HaveCount(1);
        entries[0].Success.Should().BeFalse();
        entries[0].ErrorText.Should().NotBeNullOrEmpty();
    }

    [Test]
    public async Task RequestWithResolverException_EntryHasSuccessFalseWithErrorText()
    {
        await using var host = await TestHost.BuildAsync();

        var result = await host.Executor.ExecuteAsync("{ throws }");
        (result as OperationResult)!.Errors.Should().NotBeNullOrEmpty();

        var entries = host.DrainEntries();
        entries.Should().HaveCount(1);
        entries[0].Success.Should().BeFalse();
        entries[0].ErrorText.Should().NotBeNullOrEmpty();
    }

    #endregion

    #region ShouldSkip

    [Test]
    public async Task IntrospectionDocument_Named_IsSkipped()
    {
        await using var host = await TestHost.BuildAsync();

        var result = await host.Executor.ExecuteAsync(
            QueryRequestBuilder("query IntrospectionQuery { __typename }")
                .SetOperationName("IntrospectionQuery")
                .Build()
        );
        AssertNoErrors(result);

        host.DrainEntries().Should().BeEmpty();
    }

    [Test]
    public async Task IntrospectionDocument_Unnamed_IsSkipped()
    {
        await using var host = await TestHost.BuildAsync();

        var result = await host.Executor.ExecuteAsync("{ __typename }");
        AssertNoErrors(result);

        host.DrainEntries().Should().BeEmpty();
    }

    [Test]
    public async Task DataDocument_NamedIntrospectionQuery_IsCaptured()
    {
        // Whether a request is introspection is read from the operation that executed, so a
        // document selecting data fields is audited whatever its operation is named.
        await using var host = await TestHost.BuildAsync();

        var result = await host.Executor.ExecuteAsync(
            QueryRequestBuilder("query IntrospectionQuery { ping }")
                .SetOperationName("IntrospectionQuery")
                .Build()
        );
        AssertNoErrors(result);

        host.DrainEntries().Should().HaveCount(1);
    }

    [Test]
    public async Task MixedDocument_IntrospectionAndDataFields_IsCaptured()
    {
        await using var host = await TestHost.BuildAsync();

        var result = await host.Executor.ExecuteAsync("{ __typename ping }");
        AssertNoErrors(result);

        host.DrainEntries().Should().HaveCount(1);
    }

    [Test]
    public async Task IntrospectionQuery_WhenBothSkipsAreOff_IsCaptured()
    {
        await using var host = await TestHost.BuildAsync(opts =>
        {
            opts.SkipIntrospection = false;
            opts.SkipSubscriptions = false;
        });

        AssertNoErrors(await host.Executor.ExecuteAsync("{ __typename }"));

        host.DrainEntries().Should().ContainSingle();
    }

    [Test]
    public async Task IntrospectionInsideATopLevelFragment_IsSkipped()
    {
        // Decided from the compiled operation, whose fragments are expanded.
        await using var host = await TestHost.BuildAsync();

        AssertNoErrors(
            await host.Executor.ExecuteAsync("{ ...F } fragment F on TestQuery { __typename }")
        );

        host.DrainEntries().Should().BeEmpty();
    }

    [Test]
    public async Task DataFieldInsideATopLevelFragment_IsCaptured()
    {
        await using var host = await TestHost.BuildAsync();

        AssertNoErrors(
            await host.Executor.ExecuteAsync("{ __typename ...F } fragment F on TestQuery { ping }")
        );

        host.DrainEntries().Should().ContainSingle();
    }

    [Test]
    public async Task IntrospectionQuery_WhenSkipIntrospectionFalse_IsCaptured()
    {
        await using var host = await TestHost.BuildAsync(opts => opts.SkipIntrospection = false);

        var result = await host.Executor.ExecuteAsync(
            QueryRequestBuilder("query IntrospectionQuery { __typename }")
                .SetOperationName("IntrospectionQuery")
                .Build()
        );
        AssertNoErrors(result);

        host.DrainEntries().Should().HaveCount(1);
    }

    #endregion

    #region Redactor

    [Test]
    public async Task Redactor_IsApplied_ToVariables()
    {
        await using var host = await TestHost.BuildAsync(configureServices: s =>
            s.AddSingleton<ITraxAuditRedactor>(new StripKeyRedactor("password"))
        );

        var result = await host.Executor.ExecuteAsync(
            QueryRequestBuilder(
                    "query Q($u: String!, $password: String!) "
                        + "{ a: echo(s: $u) b: echo(s: $password) }"
                )
                .SetVariableValues(
                    new Dictionary<string, object?> { ["u"] = "bob", ["password"] = "hunter2" }
                )
                .Build()
        );
        AssertNoErrors(result);

        var entries = host.DrainEntries();
        entries.Should().HaveCount(1);
        entries[0].Variables.Should().NotBeNull();
        entries[0].Variables!.ContainsKey("u").Should().BeTrue();
        entries[0].Variables!.ContainsKey("password").Should().BeFalse();
    }

    [Test]
    public async Task Redactor_ReachesAFieldNestedInAnInputObject()
    {
        await using var host = await TestHost.BuildAsync(configureServices: s =>
            s.AddSingleton<ITraxAuditRedactor>(new StripKeyRedactor("password"))
        );

        var result = await host.Executor.ExecuteAsync(
            QueryRequestBuilder("mutation M($input: LoginInput!) { login(input: $input) }")
                .SetVariableValues(
                    new Dictionary<string, object?>
                    {
                        ["input"] = new Dictionary<string, object?>
                        {
                            ["user"] = "bob",
                            ["password"] = "hunter2",
                        },
                    }
                )
                .Build()
        );
        AssertNoErrors(result);

        var entry = host.DrainEntries().Should().ContainSingle().Subject;
        entry.Variables!["input"]!["user"]!.GetValue<string>().Should().Be("bob");
        entry.Variables!["input"]!.AsObject().ContainsKey("password").Should().BeFalse();
        entry
            .Variables!.ToJsonString()
            .Should()
            .NotContain("hunter2", $"a redactor reaches a nested input field ({Adr})");
    }

    [Test]
    public async Task Redactor_Throws_VariablesDroppedButEntryStillEnqueued()
    {
        await using var host = await TestHost.BuildAsync(configureServices: s =>
            s.AddSingleton<ITraxAuditRedactor>(new ThrowingRedactor())
        );

        var result = await host.Executor.ExecuteAsync(
            QueryRequestBuilder("query Q($s: String!) { echo(s: $s) }")
                .SetVariableValues(new Dictionary<string, object?> { ["s"] = "hi" })
                .Build()
        );
        AssertNoErrors(result);

        var entries = host.DrainEntries();
        entries.Should().HaveCount(1);
        entries[0].Variables.Should().BeNull();
    }

    #endregion

    #region Subscription filtering

    [Test]
    public async Task SkipSubscriptions_Enabled_SubscriptionIsNotAudited()
    {
        // The operation type is only known once the document is compiled, which happens
        // after the audit scope opens — so this exercises the filter on the way out.
        await using var host = await TestHost.BuildAsync(opts => opts.SkipSubscriptions = true);

        var result = await host.Executor.ExecuteAsync("subscription { onPing }");
        if (result is IResponseStream stream)
            await stream.DisposeAsync();

        host.DrainEntries().Should().BeEmpty();
    }

    [Test]
    public async Task SkipSubscriptions_Disabled_SubscriptionIsAudited()
    {
        await using var host = await TestHost.BuildAsync(opts => opts.SkipSubscriptions = false);

        var result = await host.Executor.ExecuteAsync("subscription { onPing }");
        if (result is IResponseStream stream)
            await stream.DisposeAsync();

        host.DrainEntries().Should().ContainSingle().Which.Document.Should().Contain("onPing");
    }

    [Test]
    public async Task UnauthorizedSubscription_IsAudited_UnderTheDefaultOptions()
    {
        await using var host = await TestHost.BuildAsync();

        var result = await host.Executor.ExecuteAsync("subscription { onSecret }");
        result.Should().BeOfType<OperationResult>("authorization refused the subscription");

        var entry = host.DrainEntries()
            .Should()
            .ContainSingle(
                $"a refused subscription is audited under the default options ({RefusalAdr})"
            )
            .Subject;
        entry.Success.Should().BeFalse();
        entry.ErrorText.Should().Be("TRAX_AUTHORIZATION");
        entry.Document.Should().Contain("onSecret");
    }

    [Test]
    public async Task SubscriptionWhoseSubscribeStepFails_IsAudited_UnderTheDefaultOptions()
    {
        await using var host = await TestHost.BuildAsync();

        var result = await host.Executor.ExecuteAsync("subscription { onBroken }");
        if (result is IResponseStream stream)
            await stream.DisposeAsync();

        var entry = host.DrainEntries()
            .Should()
            .ContainSingle(
                $"a failed subscription is audited under the default options ({RefusalAdr})"
            )
            .Subject;
        entry.Success.Should().BeFalse();
        entry.Document.Should().Contain("onBroken");
    }

    [Test]
    public async Task SubscriptionThatFailsValidation_IsAudited_UnderTheDefaultOptions()
    {
        await using var host = await TestHost.BuildAsync();

        await host.Executor.ExecuteAsync("subscription { onPing notAField }");

        host.DrainEntries()
            .Should()
            .ContainSingle($"an invalid subscription is audited ({RefusalAdr})")
            .Which.Success.Should()
            .BeFalse();
    }

    [Test]
    public async Task SkipSubscriptions_Enabled_QueriesAreStillAudited()
    {
        await using var host = await TestHost.BuildAsync(opts => opts.SkipSubscriptions = true);

        await host.Executor.ExecuteAsync("{ ping }");

        host.DrainEntries().Should().ContainSingle();
    }

    #endregion

    #region Request-level faults

    [Test]
    public async Task RequestPipelineException_IsAuditedWithTheExceptionMessage()
    {
        await using var host = await TestHost.BuildAsync();

        var request = OperationRequestBuilder
            .New()
            .SetDocument("query Boom { ping }")
            .SetOperationName("Boom")
            .Build();

        var result = await host.Executor.ExecuteAsync(request);

        result.ExpectOperationResult().Errors.Should().NotBeNullOrEmpty();

        // HotChocolate masks the exception in the response. The entry names the exception type,
        // which is server vocabulary; its message can quote what the caller sent.
        var entry = host.DrainEntries().Should().ContainSingle().Subject;
        entry.Success.Should().BeFalse();
        entry.ErrorText.Should().Be("System.InvalidOperationException");
    }

    [Test]
    public async Task RequestPipelineException_WithRecordErrorMessages_IsAuditedWithTheMessage()
    {
        await using var host = await TestHost.BuildAsync(opts => opts.RecordErrorMessages = true);

        await host.Executor.ExecuteAsync(
            OperationRequestBuilder
                .New()
                .SetDocument("query Boom { ping }")
                .SetOperationName("Boom")
                .Build()
        );

        host.DrainEntries()
            .Should()
            .ContainSingle()
            .Which.ErrorText.Should()
            .Be("request pipeline exploded");
    }

    [Test]
    public async Task PrincipalCaptureThrows_RequestStillSucceeds_AndIsNotAudited()
    {
        // The listener must never take a request down with it: a broken
        // IHttpContextAccessor drops the audit entry, it does not fail the query.
        await using var host = await TestHost.BuildAsync(configureServices: services =>
            services.Replace(
                ServiceDescriptor.Singleton<IHttpContextAccessor>(new ThrowingHttpContextAccessor())
            )
        );

        var result = await host.Executor.ExecuteAsync("{ ping }");

        result.ExpectOperationResult().Errors.Should().BeNullOrEmpty();
        host.DrainEntries().Should().BeEmpty();
        host.Channel.TotalDropped.Should()
            .Be(1, "an entry that could not be captured is counted in trax.audit.dropped");
    }

    [Test]
    public async Task EntryThatCannotBeBuilt_IsCountedAsDropped()
    {
        await using var host = await TestHost.BuildAsync(configureServices: services =>
            services.Replace(ServiceDescriptor.Singleton<TimeProvider>(new FailingClock()))
        );

        var result = await host.Executor.ExecuteAsync("{ ping }");

        result.ExpectOperationResult().Errors.Should().BeNullOrEmpty();
        host.DrainEntries().Should().BeEmpty();
        host.Channel.TotalDropped.Should()
            .Be(1, "an entry that could not be built is counted in trax.audit.dropped");
    }

    /// <summary>Reads the clock once, for the start of the request, and fails every read after it.</summary>
    private sealed class FailingClock : TimeProvider
    {
        private int _reads;

        public override long GetTimestamp() =>
            Interlocked.Increment(ref _reads) == 1
                ? 0
                : throw new InvalidOperationException("clock unavailable");
    }

    private sealed class ThrowingHttpContextAccessor : IHttpContextAccessor
    {
        public HttpContext? HttpContext
        {
            get => throw new InvalidOperationException("no ambient context");
            set => throw new NotSupportedException();
        }
    }

    #endregion

    #region Error text

    [Test]
    public async Task ResolverErrorQuotingAnInput_IsNotRecorded_ByDefault()
    {
        await using var host = await TestHost.BuildAsync();

        var result = await host.Executor.ExecuteAsync("{ checkPassword(password: \"hunter2\") }");
        result
            .ExpectOperationResult()
            .Errors!.Single()
            .Message.Should()
            .Contain("hunter2", "the resolver quotes the input");

        var entry = host.DrainEntries().Should().ContainSingle().Subject;
        entry.Success.Should().BeFalse();
        entry
            .ErrorText.Should()
            .NotContain("hunter2", $"error messages are not recorded by default ({Adr})");
        entry.ErrorText.Should().Be("PASSWORD_REJECTED at checkPassword");
    }

    [Test]
    public async Task VariableCoercionError_DoesNotRecordTheValue_ByDefault()
    {
        await using var host = await TestHost.BuildAsync();

        await host.Executor.ExecuteAsync(
            QueryRequestBuilder("query Q($i: Int!) { complexInt(i: $i) }")
                .SetVariableValues(new Dictionary<string, object?> { ["i"] = "hunter2" })
                .Build()
        );

        var entry = host.DrainEntries().Should().ContainSingle().Subject;
        entry.Success.Should().BeFalse();
        entry.ErrorText.Should().NotBeNullOrEmpty().And.NotContain("hunter2");
    }

    [Test]
    public async Task ErrorWithoutACode_IsRecordedAsMasked_ByDefault()
    {
        await using var host = await TestHost.BuildAsync();

        await host.Executor.ExecuteAsync("{ uncoded(secret: \"hunter2\") }");

        host.DrainEntries()
            .Should()
            .ContainSingle()
            .Which.ErrorText.Should()
            .Be("<masked> at uncoded");
    }

    [Test]
    public async Task ResolverErrorQuotingAnInput_IsRecorded_WhenTheHostOptsIn()
    {
        await using var host = await TestHost.BuildAsync(opts => opts.RecordErrorMessages = true);

        await host.Executor.ExecuteAsync("{ checkPassword(password: \"hunter2\") }");

        host.DrainEntries()
            .Should()
            .ContainSingle()
            .Which.ErrorText.Should()
            .Be("Password hunter2 was rejected.");
    }

    #endregion

    #region Validation failures

    [Test]
    public async Task ValidationError_IsAuditedAsUnsuccessful()
    {
        // The request never reaches a resolver, so the entry has to come from the result's
        // errors rather than from a captured exception.
        await using var host = await TestHost.BuildAsync();

        await host.Executor.ExecuteAsync("{ fieldThatDoesNotExist }");

        var entry = host.DrainEntries().Should().ContainSingle().Subject;
        entry.Success.Should().BeFalse();
        entry.ErrorText.Should().NotBeNullOrEmpty();
    }

    #endregion

    #region Document literals

    [Test]
    public async Task InlineArgumentLiteral_IsNotRecorded()
    {
        await using var host = await TestHost.BuildAsync();

        var result = await host.Executor.ExecuteAsync(
            "mutation { login(input: { user: \"bob\", password: \"hunter2\" }) }"
        );
        AssertNoErrors(result);

        var document = host.DrainEntries().Should().ContainSingle().Subject.Document;
        document
            .Should()
            .NotContain("hunter2", $"an inline literal is replaced by a placeholder ({Adr})")
            .And.NotContain("bob");
        document.Should().Contain("login").And.Contain("password: \"\"");
    }

    [Test]
    public async Task EveryStringAndNumberLiteral_IsReplaced_StructureIsKept()
    {
        await using var host = await TestHost.BuildAsync();

        var result = await host.Executor.ExecuteAsync(
            "query Q($s: String = \"default-secret\") { "
                + "a: echo(s: $s) "
                + "b: complex(s: \"s-secret\", i: 4242, e: HAPPY, list: [7, 8], obj: { name: \"n-secret\", count: 99 }) "
                + "c: ping @include(if: true) "
                + "d: decimal(f: 3.25) }"
        );
        AssertNoErrors(result);

        var document = host.DrainEntries().Should().ContainSingle().Subject.Document;
        document
            .Should()
            .NotContainAny(
                "default-secret",
                "s-secret",
                "n-secret",
                "4242",
                "99",
                "3.25",
                "7",
                "8"
            );
        document.Should().ContainAll("a: echo", "b: complex", "e: HAPPY", "name: \"\"", "count: 0");
        document.Should().ContainAll("if: true", "$s: String = \"\"", "list: [0, 0]");
    }

    [Test]
    public async Task DirectiveArgumentLiterals_AtEveryLocation_AreReplaced_StructureIsKept()
    {
        // A directive can sit on the operation, a variable definition, a field, an inline
        // fragment and a fragment definition. Every string and number in its arguments is
        // replaced like any other literal; the directive names and argument names remain.
        await using var host = await TestHost.BuildAsync();

        var result = await host.Executor.ExecuteAsync(
            "query Q($s: String! = \"x\" @audited(name: \"var-secret\", weight: 1111)) "
                + "@audited(name: \"op-secret\", weight: 2222) { "
                + "a: echo(s: $s) @audited(name: \"field-secret\", weight: 3333) "
                + "... on TestQuery @audited(name: \"inline-secret\", weight: 4444) { ping } "
                + "...F } "
                + "fragment F on TestQuery @audited(name: \"fragment-secret\", weight: 5.55) { ping }"
        );
        AssertNoErrors(result);

        var document = host.DrainEntries().Should().ContainSingle().Subject.Document;
        document
            .Should()
            .NotContainAny(
                [
                    "var-secret",
                    "op-secret",
                    "field-secret",
                    "inline-secret",
                    "fragment-secret",
                    "1111",
                    "2222",
                    "3333",
                    "4444",
                    "5.55",
                ],
                $"a literal in a directive argument is replaced like any other ({Adr})"
            );
        document
            .Should()
            .ContainAll("@audited(name: \"\", weight: 0)", "a: echo", "fragment F on TestQuery");
    }

    [Test]
    public async Task DirectiveArgumentLiterals_InADocumentThatFailsValidation_AreReplaced()
    {
        await using var host = await TestHost.BuildAsync();

        await host.Executor.ExecuteAsync(
            "{ ping @unknownDirective(secret: \"directive-secret\", pin: 9876) }"
        );

        var entry = host.DrainEntries().Should().ContainSingle().Subject;
        entry.Success.Should().BeFalse();
        entry.Document.Should().NotContainAny("directive-secret", "9876");
        entry.Document.Should().Contain("@unknownDirective(secret: \"\", pin: 0)");
    }

    #endregion

    #region Size limits

    [Test]
    public async Task LongOperationName_IsCutToTheLimit()
    {
        await using var host = await TestHost.BuildAsync(opts => opts.MaxOperationNameLength = 64);
        var name = "Q" + new string('x', 1_000_000);

        await host.Executor.ExecuteAsync(
            QueryRequestBuilder($"query {name} {{ ping }}").SetOperationName(name).Build()
        );

        var entry = host.DrainEntries().Should().ContainSingle().Subject;
        entry.OperationName.Should().StartWith("Qxxx").And.EndWith("...[truncated]");
        entry.OperationName!.Length.Should().Be(64 + "...[truncated]".Length);
    }

    [Test]
    public async Task OperationName_UnderTheLimit_IsKept()
    {
        await using var host = await TestHost.BuildAsync();

        await host.Executor.ExecuteAsync(
            QueryRequestBuilder("query Ping { ping }").SetOperationName("Ping").Build()
        );

        host.DrainEntries().Should().ContainSingle().Which.OperationName.Should().Be("Ping");
    }

    [Test]
    public async Task LongErrorText_IsCutToTheLimit()
    {
        await using var host = await TestHost.BuildAsync(opts =>
        {
            opts.MaxErrorTextLength = 128;
            opts.RecordErrorMessages = true;
        });
        // Every unknown field is its own error, so the joined text grows with the request.
        var fields = string.Join(" ", Enumerable.Range(0, 2_000).Select(i => $"missing{i}"));

        await host.Executor.ExecuteAsync($"{{ {fields} }}");

        var entry = host.DrainEntries().Should().ContainSingle().Subject;
        entry.Success.Should().BeFalse();
        entry.ErrorText.Should().EndWith("...[truncated]");
        entry.ErrorText!.Length.Should().Be(128 + "...[truncated]".Length);
    }

    #endregion

    #region Document truncation

    [Test]
    public async Task DocumentTruncation_AppliesMaxDocumentLength()
    {
        await using var host = await TestHost.BuildAsync(opts => opts.MaxDocumentLength = 4);

        var result = await host.Executor.ExecuteAsync("{ ping }");
        AssertNoErrors(result);

        var entries = host.DrainEntries();
        entries.Should().HaveCount(1);
        entries[0].Document.Should().StartWith("{\n  ").And.Contain("...[truncated]");
        entries[0].Document.Should().EndWith("[selected fields: TestQuery.ping]");
    }

    [Test]
    public async Task DocumentTruncation_PaddedHead_StillRecordsTheExecutedFields()
    {
        // The field that matters comes after enough padding to push it past the limit, so the
        // head alone would show only the padding.
        await using var host = await TestHost.BuildAsync(opts => opts.MaxDocumentLength = 64);
        var padding = string.Join(" ", Enumerable.Range(0, 50).Select(i => $"p{i}: ping"));

        var result = await host.Executor.ExecuteAsync($"{{ {padding} echo(s: \"x\") }}");
        AssertNoErrors(result);

        var document = host.DrainEntries().Should().ContainSingle().Subject.Document;
        document.Should().Contain("...[truncated]");
        document.Should().Contain("TestQuery.echo");
        document.Should().Contain("TestQuery.ping");
    }

    [Test]
    public async Task DocumentTruncation_FieldsReachedThroughFragments_AreRecorded()
    {
        await using var host = await TestHost.BuildAsync(opts => opts.MaxDocumentLength = 32);
        var padding = string.Join(" ", Enumerable.Range(0, 20).Select(i => $"p{i}: ping"));

        var result = await host.Executor.ExecuteAsync(
            $"{{ {padding} ...F ... on TestQuery {{ throwsNot: ping }} }} "
                + "fragment F on TestQuery { echo(s: \"x\") }"
        );
        AssertNoErrors(result);

        host.DrainEntries()
            .Should()
            .ContainSingle()
            .Subject.Document.Should()
            .Contain("TestQuery.echo");
    }

    [Test]
    public async Task DocumentTruncation_NestedSelections_ListEveryField()
    {
        await using var host = await TestHost.BuildAsync(opts => opts.MaxDocumentLength = 8);

        AssertNoErrors(await host.Executor.ExecuteAsync("{ owner { name pet { name } } }"));

        host.DrainEntries()
            .Should()
            .ContainSingle()
            .Subject.Document.Should()
            .EndWith("[selected fields: Owner.name, Owner.pet, Pet.name, TestQuery.owner]");
    }

    [Test]
    public async Task DocumentTruncation_DocumentThatFailsValidation_KeepsOnlyTheHead()
    {
        // No operation was compiled, so there is no field list to add.
        await using var host = await TestHost.BuildAsync(opts => opts.MaxDocumentLength = 8);

        await host.Executor.ExecuteAsync("{ notARealField anotherOne }");

        var document = host.DrainEntries().Should().ContainSingle().Subject.Document;
        document.Should().EndWith("...[truncated]").And.NotContain("[selected fields");
        document.Length.Should().Be(8 + "...[truncated]".Length);
    }

    [Test]
    public async Task RequestWithOnlyADocumentIdThatIsNotFound_IsAuditedWithAnEmptyDocument()
    {
        await using var host = await TestHost.BuildAsync();

        await host.Executor.ExecuteAsync(
            OperationRequestBuilder.New().SetDocumentId("not-a-stored-document").Build()
        );

        var entry = host.DrainEntries().Should().ContainSingle().Subject;
        entry.Success.Should().BeFalse();
        entry.Document.Should().BeEmpty();
    }

    [Test]
    public async Task RequestRefusedBeforeItsTextIsParsed_IsAuditedWithAnEmptyDocument()
    {
        // A request refused ahead of the document cache, before HotChocolate parsed its text,
        // has no document to record. Over HTTP the transport parses first, so this is only an
        // executor called with unparsed text.
        await using var host = await TestHost.BuildAsync();

        await host.Executor.ExecuteAsync(
            OperationRequestBuilder
                .New()
                .SetDocument("query Early { ping }")
                .SetOperationName("Early")
                .Build()
        );

        var entry = host.DrainEntries().Should().ContainSingle().Subject;
        entry.Success.Should().BeFalse();
        entry.Document.Should().BeEmpty();
    }

    [Test]
    public async Task DocumentTruncation_NoMarkerWhenUnderLimit()
    {
        await using var host = await TestHost.BuildAsync(opts => opts.MaxDocumentLength = 65_536);

        var result = await host.Executor.ExecuteAsync("{ ping }");
        AssertNoErrors(result);

        var entries = host.DrainEntries();
        entries.Should().HaveCount(1);
        entries[0].Document.Should().NotContain("[truncated]");
    }

    #endregion

    #region Principal capture

    [Test]
    public async Task Principal_AuthenticatedUser_IsCaptured()
    {
        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(
                new ClaimsIdentity(
                    [
                        new Claim(TraxAuthClaimTypes.PrincipalId, "user-42"),
                        new Claim(TraxAuthClaimTypes.PrincipalType, "api-key"),
                    ],
                    authenticationType: "test"
                )
            ),
        };
        await using var host = await TestHost.BuildAsync(configureServices: s =>
            s.AddSingleton<IHttpContextAccessor>(new FixedHttpContextAccessor(httpContext))
        );

        var result = await host.Executor.ExecuteAsync("{ ping }");
        AssertNoErrors(result);

        var entries = host.DrainEntries();
        entries.Should().HaveCount(1);
        entries[0].PrincipalId.Should().Be("user-42");
        entries[0].PrincipalType.Should().Be("api-key");
    }

    [Test]
    public async Task Principal_WithoutATypeClaim_HasNoPrincipalType()
    {
        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(
                new ClaimsIdentity(
                    [new Claim(TraxAuthClaimTypes.PrincipalId, "user-7")],
                    authenticationType: "test"
                )
            ),
        };
        await using var host = await TestHost.BuildAsync(configureServices: s =>
            s.AddSingleton<IHttpContextAccessor>(new FixedHttpContextAccessor(httpContext))
        );

        AssertNoErrors(await host.Executor.ExecuteAsync("{ ping }"));

        var entry = host.DrainEntries().Should().ContainSingle().Subject;
        entry.PrincipalId.Should().Be("user-7");
        entry.PrincipalType.Should().BeNull();
    }

    [Test]
    public async Task Principal_Unauthenticated_UsesDefaultPrincipalId()
    {
        await using var host = await TestHost.BuildAsync(opts => opts.DefaultPrincipalId = "ghost");

        var result = await host.Executor.ExecuteAsync("{ ping }");
        AssertNoErrors(result);

        var entries = host.DrainEntries();
        entries.Should().HaveCount(1);
        entries[0].PrincipalId.Should().Be("ghost");
        entries[0].PrincipalType.Should().BeNull();
    }

    #endregion

    #region Helpers

    private static OperationRequestBuilder QueryRequestBuilder(string document) =>
        OperationRequestBuilder.New().SetDocument(document);

    private static void AssertNoErrors(IExecutionResult result)
    {
        var op = result as OperationResult;
        op.Should().NotBeNull();
        op!.Errors.Should().BeNullOrEmpty();
    }

    /// <summary>The redactor the docs show: removes a named field at any depth.</summary>
    private sealed class StripKeyRedactor(string keyToStrip) : ITraxAuditRedactor
    {
        public JsonObject? Redact(JsonObject? variables)
        {
            Strip(variables);
            return variables;
        }

        private void Strip(JsonNode? node)
        {
            switch (node)
            {
                case JsonObject obj:
                    obj.Remove(keyToStrip);
                    foreach (var (_, child) in obj)
                        Strip(child);
                    break;
                case JsonArray array:
                    foreach (var child in array)
                        Strip(child);
                    break;
            }
        }
    }

    private sealed class KeepAllRedactor : ITraxAuditRedactor
    {
        public JsonObject? Redact(JsonObject? variables) => variables;
    }

    private sealed class FixedHttpContextAccessor(HttpContext context) : IHttpContextAccessor
    {
        public HttpContext? HttpContext
        {
            get => context;
            set => throw new NotSupportedException();
        }
    }

    private sealed class ThrowingRedactor : ITraxAuditRedactor
    {
        public JsonObject? Redact(JsonObject? variables) =>
            throw new InvalidOperationException("boom");
    }

    private sealed class TestHost : IAsyncDisposable
    {
        public required IRequestExecutor Executor { get; init; }
        public required TraxAuditChannel Channel { get; init; }
        public required ServiceProvider Provider { get; init; }

        public IReadOnlyList<TraxAuditEntry> DrainEntries()
        {
            var list = new List<TraxAuditEntry>();
            while (Channel.Reader.TryRead(out var entry))
                list.Add(entry);
            return list;
        }

        public async ValueTask DisposeAsync() => await Provider.DisposeAsync();

        public static async Task<TestHost> BuildAsync(
            Action<TraxAuditOptions>? configureOptions = null,
            Action<IServiceCollection>? configureServices = null
        )
        {
            var services = new ServiceCollection();
            services.AddLogging(b => b.AddProvider(NullLoggerProvider.Instance));
            services.AddSingleton(TimeProvider.System);
            services.AddSingleton<IHttpContextAccessor, HttpContextAccessor>();
            services.AddSingleton<TraxAuditChannel>();
            services.AddSingleton<ITraxAuditRedactor, DefaultAuditRedactor>();
            services.AddSingleton<TraxGraphQLAuditListener>();

            if (configureOptions is not null)
                services.Configure(configureOptions);
            else
                services.AddOptions<TraxAuditOptions>();

            configureServices?.Invoke(services);

            services
                .AddGraphQLServer()
                .AddQueryType<TestQuery>()
                .AddMutationType<TestMutation>()
                .AddSubscriptionType<TestSubscription>()
                .AddInMemorySubscriptions()
                // A request-level fault (as opposed to a resolver fault) reaches the
                // listener through RequestError, not through the result's errors.
                .UseRequest(
                    next =>
                        context =>
                            context.Request.OperationName == "Boom"
                                ? throw new InvalidOperationException("request pipeline exploded")
                                : next(context),
                    key: "TraxAuditTestFault",
                    after: "DocumentValidationMiddleware"
                )
                .UseRequest(
                    next =>
                        context =>
                            context.Request.OperationName == "Early"
                                ? throw new InvalidOperationException("refused before parsing")
                                : next(context),
                    key: "TraxAuditTestEarlyFault",
                    before: "DocumentCacheMiddleware"
                )
                .AddDirectiveType(new AuditedDirectiveType())
                .AddType<EnumType<Mood>>()
                .AddType<InputObjectType<FooInput>>()
                // Mirrors AddTraxAudit: HotChocolate 16 activates diagnostic listeners
                // from the schema container, so the listener's application services have
                // to be bridged across.
                .AddApplicationService<IHttpContextAccessor>()
                .AddApplicationService<TraxAuditChannel>()
                .AddApplicationService<IOptions<TraxAuditOptions>>()
                .AddApplicationService<ITraxAuditRedactor>()
                .AddApplicationService<TimeProvider>()
                .AddApplicationService<ILogger<TraxGraphQLAuditListener>>()
                .AddDiagnosticEventListener<TraxGraphQLAuditListener>();

            var provider = services.BuildServiceProvider();
            var executor = await provider
                .GetRequiredService<IRequestExecutorProvider>()
                .GetExecutorAsync();

            return new TestHost
            {
                Executor = executor,
                Channel = provider.GetRequiredService<TraxAuditChannel>(),
                Provider = provider,
            };
        }
    }

    /// <summary>
    /// A custom executable directive allowed at every executable location, carrying a string and
    /// a number, so the literal stripper is checked wherever a directive can appear.
    /// </summary>
    private sealed class AuditedDirectiveType : DirectiveType
    {
        protected override void Configure(IDirectiveTypeDescriptor descriptor)
        {
            descriptor.Name("audited");
            descriptor.Argument("name").Type<StringType>();
            descriptor.Argument("weight").Type<FloatType>();
            descriptor.Location(
                DirectiveLocation.Query
                    | DirectiveLocation.Field
                    | DirectiveLocation.InlineFragment
                    | DirectiveLocation.FragmentDefinition
                    | DirectiveLocation.VariableDefinition
            );
        }
    }

    public class TestSubscription
    {
        [Subscribe]
        [Topic("ping")]
        public string OnPing([EventMessage] string message) => message;

        /// <summary>Refuses every subscriber when subscribing, as Trax's lifecycle feeds refuse one who could receive nothing.</summary>
        public IAsyncEnumerable<string> SubscribeToSecret() =>
            throw new GraphQLException(
                ErrorBuilder
                    .New()
                    .SetMessage("Not authorized.")
                    .SetCode("TRAX_AUTHORIZATION")
                    .Build()
            );

        [Subscribe(With = nameof(SubscribeToSecret))]
        public string OnSecret([EventMessage] string message) => message;

        public IAsyncEnumerable<string> SubscribeToBroken() =>
            throw new InvalidOperationException("event source unavailable");

        [Subscribe(With = nameof(SubscribeToBroken))]
        public string OnBroken([EventMessage] string message) => message;
    }

    public enum Mood
    {
        Happy,
        Sad,
    }

    public sealed class FooInput
    {
        public string Name { get; set; } = "";
        public int Count { get; set; }
    }

    public sealed record Owner(string Name, Pet Pet);

    public sealed record Pet(string Name);

    public sealed class LoginInput
    {
        public string User { get; set; } = "";
        public string Password { get; set; } = "";
    }

    public sealed class TestMutation
    {
        public bool Login(LoginInput input) => input.Password.Length > 0;
    }

    public sealed class TestQuery
    {
        public decimal Decimal(decimal f) => f;

        public string Ping() => "pong";

        public string Echo(string s) => s;

        public string Complex(string s, int i, Mood e, int[] list, FooInput obj) =>
            $"{s}-{i}-{e}-{list.Length}-{obj.Name}-{obj.Count}";

        public string Throws() => throw new InvalidOperationException("resolver exploded");

        public int ComplexInt(int i) => i;

        public string? Maybe(string? s) => s;

        public Owner Owner() => new("o", new Pet("p"));

        /// <summary>A resolver whose error message quotes what the caller sent, with a code.</summary>
        public bool CheckPassword(string password) =>
            throw new GraphQLException(
                ErrorBuilder
                    .New()
                    .SetMessage($"Password {password} was rejected.")
                    .SetCode("PASSWORD_REJECTED")
                    .Build()
            );

        /// <summary>The same, with no code.</summary>
        public bool Uncoded(string secret) => throw new GraphQLException($"Rejected {secret}.");
    }

    #endregion
}
