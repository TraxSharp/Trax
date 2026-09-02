using System.Collections.Concurrent;
using System.Security.Claims;
using FluentAssertions;
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
/// </summary>
[TestFixture]
public class TraxGraphQLAuditListenerTests
{
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
        await using var host = await TestHost.BuildAsync();

        var result = await host.Executor.ExecuteAsync(
            QueryRequestBuilder("query Q($s: String!) { echo(s: $s) }")
                .SetVariableValues(new Dictionary<string, object?> { ["s"] = "hi" })
                .Build()
        );
        AssertNoErrors(result);

        var entries = host.DrainEntries();
        entries.Should().HaveCount(1);
        entries[0].Variables.Should().NotBeNull();
        entries[0].Variables!.Should().ContainKey("s");
        entries[0].Variables!["s"].Should().NotBeNull();
    }

    [Test]
    public async Task BuildVariables_MultipleMixedTypes_CapturesAll()
    {
        await using var host = await TestHost.BuildAsync();

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
        captured!.Keys.Should().BeEquivalentTo(new[] { "s", "i", "e", "list", "obj" });
        captured.Values.Should().NotContainNulls();
    }

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
    public async Task IntrospectionQuery_ByOperationName_IsSkipped()
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
        entries[0].Variables!.Should().ContainKey("u").And.NotContainKey("password");
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

        // HotChocolate masks the exception in the response, so the audit trail is the only
        // place the real cause survives.
        var entry = host.DrainEntries().Should().ContainSingle().Subject;
        entry.Success.Should().BeFalse();
        entry.ErrorText.Should().Be("request pipeline exploded");
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

    #region Document truncation

    [Test]
    public async Task DocumentTruncation_AppliesMaxDocumentLength()
    {
        await using var host = await TestHost.BuildAsync(opts => opts.MaxDocumentLength = 4);

        var result = await host.Executor.ExecuteAsync("{ ping }");
        AssertNoErrors(result);

        var entries = host.DrainEntries();
        entries.Should().HaveCount(1);
        entries[0].Document.Should().EndWith("...[truncated]");
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

    private sealed class StripKeyRedactor(string keyToStrip) : ITraxAuditRedactor
    {
        public IReadOnlyDictionary<string, object?>? Redact(
            IReadOnlyDictionary<string, object?>? variables
        )
        {
            if (variables is null)
                return null;
            var copy = new Dictionary<string, object?>(variables, StringComparer.Ordinal);
            copy.Remove(keyToStrip);
            return copy;
        }
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
        public IReadOnlyDictionary<string, object?>? Redact(
            IReadOnlyDictionary<string, object?>? variables
        ) => throw new InvalidOperationException("boom");
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

    public class TestSubscription
    {
        [Subscribe]
        [Topic("ping")]
        public string OnPing([EventMessage] string message) => message;
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

    public sealed class TestQuery
    {
        public string Ping() => "pong";

        public string Echo(string s) => s;

        public string Complex(string s, int i, Mood e, int[] list, FooInput obj) =>
            $"{s}-{i}-{e}-{list.Length}-{obj.Name}-{obj.Count}";

        public string Throws() => throw new InvalidOperationException("resolver exploded");
    }

    #endregion
}
