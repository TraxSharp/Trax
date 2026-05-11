using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Trax.Api.GraphQL.PersistedOperations.Configuration;
using Trax.Api.GraphQL.PersistedOperations.Middleware;

namespace Trax.Api.Tests.PersistedOperations.UnitTests;

[TestFixture]
public class PersistedOperationsMiddlewareTests
{
    private static (PersistedOperationsMiddleware Mw, MiddlewareInvocations Invocations) Build(
        Action<PersistedOperationsBuilder> configure
    )
    {
        var builder = new PersistedOperationsBuilder().UseDatabase("Host=fake;Database=fake");
        configure(builder);
        var options = builder.Build();

        var invoked = new MiddlewareInvocations();
        var mw = new PersistedOperationsMiddleware(
            next: ctx =>
            {
                invoked.NextCalls++;
                return Task.CompletedTask;
            },
            options,
            new AllowlistMatcher(options),
            NullLogger<PersistedOperationsMiddleware>.Instance
        );
        return (mw, invoked);
    }

    private static HttpContext BuildContext(
        string? query,
        string? id = null,
        string? operationName = null,
        string method = "POST",
        string contentType = "application/json"
    )
    {
        var context = new DefaultHttpContext();
        context.Request.Method = method;
        context.Request.ContentType = contentType;

        if (query is not null || id is not null || operationName is not null)
        {
            var body = "{";
            var fields = new List<string>();
            if (query is not null)
                fields.Add($"\"query\":\"{query.Replace("\"", "\\\"")}\"");
            if (id is not null)
                fields.Add($"\"id\":\"{id}\"");
            if (operationName is not null)
                fields.Add($"\"operationName\":\"{operationName}\"");
            body += string.Join(",", fields);
            body += "}";

            context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
            context.Request.ContentLength = context.Request.Body.Length;
        }

        context.Response.Body = new MemoryStream();
        return context;
    }

    private static string ResponseBody(HttpContext ctx)
    {
        ctx.Response.Body.Position = 0;
        using var reader = new StreamReader(ctx.Response.Body, leaveOpen: true);
        return reader.ReadToEnd();
    }

    [Test]
    public async Task InlineQuery_RequirePersisted_RejectsWith400()
    {
        var (mw, calls) = Build(b => b.RequirePersisted(true));
        var ctx = BuildContext(query: "{ user { id } }");

        await mw.InvokeAsync(ctx);

        calls.NextCalls.Should().Be(0);
        ctx.Response.StatusCode.Should().Be(400);
        ResponseBody(ctx).Should().Contain("PERSISTED_OPERATION_REQUIRED");
    }

    [Test]
    public async Task PersistedIdOnly_PassesThrough()
    {
        var (mw, calls) = Build(b => b.RequirePersisted(true));
        var ctx = BuildContext(query: null, id: "userProfile_v1");

        await mw.InvokeAsync(ctx);

        calls.NextCalls.Should().Be(1);
        ctx.Response.StatusCode.Should().Be(200);
    }

    [Test]
    public async Task InlineQuery_ShadowMode_PassesThrough()
    {
        var (mw, calls) = Build(b => b.RequirePersisted(false).LogNonPersistedRequests(true));
        var ctx = BuildContext(query: "{ user { id } }");

        await mw.InvokeAsync(ctx);

        calls.NextCalls.Should().Be(1);
        ctx.Response.StatusCode.Should().Be(200);
    }

    [Test]
    public async Task InlineQuery_AllowlistedByName_PassesThrough()
    {
        var (mw, calls) = Build(b => b.RequirePersisted(true).AllowOperations("DevExplore"));
        var ctx = BuildContext(
            query: "query DevExplore { user { id } }",
            operationName: "DevExplore"
        );

        await mw.InvokeAsync(ctx);

        calls.NextCalls.Should().Be(1);
    }

    [Test]
    public async Task InlineQuery_ManagementMutation_BypassesEnforcement()
    {
        // The management mutations (uploadPersistedOperation et al.) and
        // queries (persistedOperations, persistedOperationHistory) live under
        // operations.persistedOperations and MUST bypass RequirePersisted —
        // persisting the upload mutation by id is a chicken-and-egg. The
        // bypass detects any document referencing the persistedOperations
        // namespace.
        var (mw, calls) = Build(b => b.RequirePersisted(true));
        var ctx = BuildContext(
            query: """
            mutation {
              operations {
                persistedOperations {
                  uploadPersistedOperation(input: { id: "x", document: "{ x }" }) { success }
                }
              }
            }
            """,
            operationName: null
        );

        await mw.InvokeAsync(ctx);

        calls.NextCalls.Should().Be(1, "the management mutation must reach the GraphQL endpoint");
    }

    [Test]
    public async Task InlineQuery_ManagementQuery_BypassesEnforcement()
    {
        var (mw, calls) = Build(b => b.RequirePersisted(true));
        var ctx = BuildContext(
            query: "query { operations { persistedOperations { persistedOperations { totalCount } } } }"
        );

        await mw.InvokeAsync(ctx);

        calls.NextCalls.Should().Be(1);
    }

    [Test]
    public async Task InlineQuery_AllowlistedByPredicate_PassesThrough()
    {
        var (mw, calls) = Build(b =>
            b.RequirePersisted(true).AllowOperationsMatching(s => s.StartsWith("dev_"))
        );
        var ctx = BuildContext(
            query: "query dev_something { user { id } }",
            operationName: "dev_something"
        );

        await mw.InvokeAsync(ctx);

        calls.NextCalls.Should().Be(1);
    }

    [Test]
    public async Task IntrospectionQuery_AllowedByDefault()
    {
        var (mw, calls) = Build(b => b.RequirePersisted(true));
        var ctx = BuildContext(
            query: "query IntrospectionQuery { __schema { types { name } } }",
            operationName: "IntrospectionQuery"
        );

        await mw.InvokeAsync(ctx);

        calls.NextCalls.Should().Be(1);
    }

    [Test]
    public async Task IntrospectionQuery_WithDisableIntrospection_IsRejected()
    {
        var (mw, calls) = Build(b => b.RequirePersisted(true).DisableIntrospection());
        var ctx = BuildContext(
            query: "query IntrospectionQuery { __schema { types { name } } }",
            operationName: "IntrospectionQuery"
        );

        await mw.InvokeAsync(ctx);

        calls.NextCalls.Should().Be(0);
        ctx.Response.StatusCode.Should().Be(400);
    }

    [Test]
    public async Task PureIntrospectionByBody_AllowedByDefault()
    {
        // No operation name; detection falls back to AST inspection.
        var (mw, calls) = Build(b => b.RequirePersisted(true));
        var ctx = BuildContext(query: "{ __schema { types { name } } }");

        await mw.InvokeAsync(ctx);

        calls.NextCalls.Should().Be(1);
    }

    [Test]
    public async Task NonPostRequest_PassesThrough()
    {
        var (mw, calls) = Build(b => b.RequirePersisted(true));
        var ctx = BuildContext(query: "{ user { id } }", method: "GET");

        await mw.InvokeAsync(ctx);

        calls.NextCalls.Should().Be(1);
    }

    [Test]
    public async Task NonJsonContentType_PassesThrough()
    {
        var (mw, calls) = Build(b => b.RequirePersisted(true));
        var ctx = BuildContext(query: "{ user { id } }", contentType: "text/plain");

        await mw.InvokeAsync(ctx);

        calls.NextCalls.Should().Be(1);
    }

    [Test]
    public async Task ApplicationGraphQLContentType_IsInspected()
    {
        var (mw, calls) = Build(b => b.RequirePersisted(true));
        var ctx = BuildContext(query: "{ user { id } }", contentType: "application/graphql");

        await mw.InvokeAsync(ctx);

        calls.NextCalls.Should().Be(0);
        ctx.Response.StatusCode.Should().Be(400);
    }

    [Test]
    public async Task MalformedJsonBody_PassesThrough()
    {
        var (mw, calls) = Build(b => b.RequirePersisted(true));
        var ctx = BuildContext(query: null);
        ctx.Request.ContentType = "application/json";
        ctx.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes("not json"));

        await mw.InvokeAsync(ctx);

        // Malformed bodies pass through; HC will return its own parse error.
        calls.NextCalls.Should().Be(1);
    }

    [Test]
    public async Task NoBody_PassesThrough()
    {
        var (mw, calls) = Build(b => b.RequirePersisted(true));
        var ctx = new DefaultHttpContext();
        ctx.Request.Method = "POST";
        ctx.Request.ContentType = "application/json";
        ctx.Request.Body = Stream.Null;

        await mw.InvokeAsync(ctx);

        calls.NextCalls.Should().Be(1);
    }

    [Test]
    public async Task DocumentIdField_TreatedSameAsId()
    {
        var (mw, calls) = Build(b => b.RequirePersisted(true));
        var ctx = new DefaultHttpContext();
        ctx.Request.Method = "POST";
        ctx.Request.ContentType = "application/json";
        ctx.Request.Body = new MemoryStream(
            Encoding.UTF8.GetBytes("{\"documentId\":\"userProfile_v1\"}")
        );
        ctx.Response.Body = new MemoryStream();

        await mw.InvokeAsync(ctx);

        calls.NextCalls.Should().Be(1);
    }

    [Test]
    public async Task RequestBody_IsRewindedForDownstream()
    {
        var (mw, _) = Build(b => b.RequirePersisted(true));
        var ctx = BuildContext(query: null, id: "userProfile_v1");

        await mw.InvokeAsync(ctx);

        ctx.Request.Body.Position.Should().Be(0);
    }

    [Test]
    public async Task BatchedRequest_AllPersisted_PassesThrough()
    {
        var (mw, calls) = Build(b => b.RequirePersisted(true));
        var ctx = BuildBatchContext("[{\"id\":\"a_v1\"},{\"id\":\"b_v1\"}]");
        await mw.InvokeAsync(ctx);
        calls.NextCalls.Should().Be(1);
    }

    [Test]
    public async Task BatchedRequest_OneInlineQuery_RejectsWholeBatch()
    {
        var (mw, calls) = Build(b => b.RequirePersisted(true));
        var ctx = BuildBatchContext("[{\"id\":\"a_v1\"},{\"query\":\"{ user { id } }\"}]");
        await mw.InvokeAsync(ctx);
        calls.NextCalls.Should().Be(0);
        ctx.Response.StatusCode.Should().Be(400);
        ResponseBody(ctx).Should().Contain("PERSISTED_OPERATION_REQUIRED");
    }

    [Test]
    public async Task BatchedRequest_OneInlineAllowlisted_PassesThrough()
    {
        var (mw, calls) = Build(b =>
            b.RequirePersisted(true).AllowOperationsMatching(s => s.StartsWith("dev_"))
        );
        var ctx = BuildBatchContext(
            "[{\"id\":\"a_v1\"},{\"query\":\"query dev_x { x }\",\"operationName\":\"dev_x\"}]"
        );
        await mw.InvokeAsync(ctx);
        calls.NextCalls.Should().Be(1);
    }

    [Test]
    public async Task BatchedRequest_Empty_PassesThrough()
    {
        var (mw, calls) = Build(b => b.RequirePersisted(true));
        var ctx = BuildBatchContext("[]");
        await mw.InvokeAsync(ctx);
        calls.NextCalls.Should().Be(1);
    }

    [Test]
    public async Task GraphQLResponseJsonContentType_IsInspected()
    {
        var (mw, calls) = Build(b => b.RequirePersisted(true));
        var ctx = BuildContext(
            query: "{ user { id } }",
            contentType: "application/graphql-response+json"
        );
        await mw.InvokeAsync(ctx);
        calls.NextCalls.Should().Be(0);
        ctx.Response.StatusCode.Should().Be(400);
    }

    [Test]
    public async Task ContentTypeWithCharset_IsInspected()
    {
        var (mw, calls) = Build(b => b.RequirePersisted(true));
        var ctx = BuildContext(
            query: "{ user { id } }",
            contentType: "application/json; charset=utf-8"
        );
        await mw.InvokeAsync(ctx);
        calls.NextCalls.Should().Be(0);
        ctx.Response.StatusCode.Should().Be(400);
    }

    [Test]
    public async Task EmptyJsonObject_PassesThrough()
    {
        var (mw, calls) = Build(b => b.RequirePersisted(true));
        var ctx = BuildBatchContext("{}");
        await mw.InvokeAsync(ctx);
        calls.NextCalls.Should().Be(1);
    }

    [Test]
    public async Task LargeBody_IsProcessedCorrectly()
    {
        var (mw, calls) = Build(b => b.RequirePersisted(true));
        // ~64KB inline query — exceeds typical default buffering thresholds.
        var bigQuery = "{ user { name } " + new string(' ', 64 * 1024) + "}";
        var ctx = BuildContext(query: bigQuery);
        await mw.InvokeAsync(ctx);
        calls.NextCalls.Should().Be(0);
        ctx.Response.StatusCode.Should().Be(400);
    }

    [Test]
    public async Task ScalarRootJsonBody_PassesThrough()
    {
        var (mw, calls) = Build(b => b.RequirePersisted(true));
        var ctx = BuildBatchContext("\"just a string\"");
        await mw.InvokeAsync(ctx);
        calls.NextCalls.Should().Be(1);
    }

    private static HttpContext BuildBatchContext(string body)
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Method = "POST";
        ctx.Request.ContentType = "application/json";
        ctx.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
        ctx.Request.ContentLength = ctx.Request.Body.Length;
        ctx.Response.Body = new MemoryStream();
        return ctx;
    }

    [Test]
    public void Constructor_NullArgs_Throw()
    {
        var options = new PersistedOperationsBuilder().UseDatabase("Host=x").Build();
        var allowlist = new AllowlistMatcher(options);

        (
            (Action)(
                () =>
                    _ = new PersistedOperationsMiddleware(
                        null!,
                        options,
                        allowlist,
                        NullLogger<PersistedOperationsMiddleware>.Instance
                    )
            )
        )
            .Should()
            .Throw<ArgumentNullException>();
        (
            (Action)(
                () =>
                    _ = new PersistedOperationsMiddleware(
                        _ => Task.CompletedTask,
                        null!,
                        allowlist,
                        NullLogger<PersistedOperationsMiddleware>.Instance
                    )
            )
        )
            .Should()
            .Throw<ArgumentNullException>();
        (
            (Action)(
                () =>
                    _ = new PersistedOperationsMiddleware(
                        _ => Task.CompletedTask,
                        options,
                        null!,
                        NullLogger<PersistedOperationsMiddleware>.Instance
                    )
            )
        )
            .Should()
            .Throw<ArgumentNullException>();
        (
            (Action)(
                () =>
                    _ = new PersistedOperationsMiddleware(
                        _ => Task.CompletedTask,
                        options,
                        allowlist,
                        null!
                    )
            )
        )
            .Should()
            .Throw<ArgumentNullException>();
    }

    private sealed class MiddlewareInvocations
    {
        public int NextCalls { get; set; }
    }
}
