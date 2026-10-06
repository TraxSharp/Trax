// ─────────────────────────────────────────────────────────────────────────────
// Trax Auth Sample
//
// Securing a Trax GraphQL server end to end, in one host:
//
//   * Two authentication schemes side by side: Trax API keys (X-Api-Key) and JWT bearer
//     (Authorization: Bearer). Either one authenticates any request.
//   * [TraxAuthorize] on trains and query models: a bare gate, a role gate, a policy AND a role,
//     and two stacked role attributes, which union. [TraxAllowAnonymous] on the public surfaces.
//   * GateOperations: the `operations` namespace (executions, health, queueing work) needs the
//     Operator role, while the public trains and articles stay reachable anonymously.
//   * Scheme-qualified principal ids: Alice's key is `TraxApiKey:alice`, her token `TraxJwt:alice`.
//   * The audit trail: every GraphQL request is stored with the principal that made it, and
//     auditors read it back over GraphQL.
//
// Credentials. Development (`dotnet run` sets it through Properties/launchSettings.json) gets
// demo keys and a demo JWT signing key, published in Auth/DemoCredentials.cs. Any other
// environment gets only what configuration supplies (Auth:ApiKeys, Auth:Jwt), so the demo
// credentials can never authenticate a deployed host.
//
//   user   API key                              roles               email verified
//   alice  alice-key-do-not-use-in-production   Editor              yes
//   erin   erin-key-do-not-use-in-production    Editor              no
//   bob    bob-key-do-not-use-in-production     Reader              yes
//   oscar  oscar-key-do-not-use-in-production   Operator, Auditor   yes
//
//   A token for any of them: curl http://localhost:5220/dev/token/alice  (Development only)
//
// Try it: see README.md, or https://traxsharp.net/docs/samples/auth
//
// NO WARRANTY. Trax auth is plumbing, not a security product. You are solely responsible for
// securing systems that use it.
// ─────────────────────────────────────────────────────────────────────────────

using Microsoft.EntityFrameworkCore;
using Trax.Api.Auth;
using Trax.Api.Auth.ApiKey;
using Trax.Api.Auth.Jwt;
using Trax.Api.Extensions;
using Trax.Api.GraphQL.Audit;
using Trax.Api.GraphQL.Extensions;
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Data.Services.DomainContext;
using Trax.Effect.Extensions;
using Trax.Effect.Provider.Json.Extensions;
using Trax.Effect.Provider.Parameter.Extensions;
using Trax.Mediator.Extensions;
using Trax.Samples.Auth.Audit;
using Trax.Samples.Auth.Auth;
using Trax.Samples.Auth.Data;
using Trax.Scheduler.Extensions;

var builder = WebApplication.CreateBuilder(args);

var connectionString =
    builder.Configuration.GetConnectionString("TraxDatabase")
    ?? throw new InvalidOperationException("Connection string 'TraxDatabase' not found.");

// ── Authentication ───────────────────────────────────────────────────────────
// Each AddTrax*Auth call registers its scheme and adds it to the combined Trax policy. Neither
// becomes the default scheme: Trax authenticates a GraphQL request against every registered
// scheme and keeps the first that succeeds, so a request may carry either credential.
if (builder.Environment.IsDevelopment())
{
    // Demo API keys. Each carries "do-not-use-in-production": Trax.Api refuses to start a host
    // outside Development with such a key registered, so a copied demo key cannot go live.
    builder.Services.AddTraxApiKeyAuth(keys =>
        keys.Add(DemoCredentials.AliceKey, DemoCredentials.Alice.ToApiKeyPrincipal)
            .Add(DemoCredentials.ErinKey, DemoCredentials.Erin.ToApiKeyPrincipal)
            .Add(DemoCredentials.BobKey, DemoCredentials.Bob.ToApiKeyPrincipal)
            .Add(DemoCredentials.OscarKey, DemoCredentials.Oscar.ToApiKeyPrincipal)
    );

    // Demo JWT scheme: HS256 with a key published in this repository, so it too exists only
    // here. Tokens come from GET /dev/token/{user} below.
    builder.Services.AddTraxJwtAuth(jwt =>
        jwt.UseSymmetricKey(
            DemoCredentials.JwtIssuer,
            DemoCredentials.JwtAudience,
            DemoCredentials.JwtSigningKey
        )
    );
}
else
{
    // Real credentials come from configuration backed by a secret store. API keys arrive as a
    // salt and SHA-256(salt || key), so the cleartext never enters this process.
    var apiKeys = builder.Configuration.GetSection("Auth:ApiKeys").Get<ConfiguredApiKey[]>() ?? [];
    if (apiKeys.Length > 0)
        builder.Services.AddTraxApiKeyAuth(keys =>
        {
            foreach (var key in apiKeys)
                keys.AddHashed(
                    Convert.FromBase64String(key.Salt),
                    Convert.FromBase64String(key.Hash),
                    key.Id,
                    key.Roles
                );
        });

    // Tokens from your identity provider, validated against its published signing keys.
    var jwt = builder.Configuration.GetSection("Auth:Jwt");
    if (jwt["Authority"] is { Length: > 0 } authority)
        builder.Services.AddTraxJwtAuth(
            authority,
            jwt["Audience"] ?? throw new InvalidOperationException("Auth:Jwt:Audience is required.")
        );

    // With neither configured no scheme exists: every gated train, model and the operations
    // namespace refuse every caller, and only the [TraxAllowAnonymous] surfaces answer.
}

// The calls above are conditional, so register what they would otherwise bring and a host with no
// scheme configured still starts. UseAuthentication() needs the authentication services, and the
// mediator's startup check refuses a host whose junctions inject TraxPrincipal (these do) when
// nothing registers it.
builder.Services.AddAuthentication();
builder.Services.AddTraxPrincipalAccessor();

// ── Authorization policies ───────────────────────────────────────────────────
// Roles need no registration. A policy does. A query model naming an unregistered policy stops
// the host at startup; a train or GateOperations naming one fails every call at request time, so
// register every policy you name.
builder.Services.AddAuthorization(options =>
    options.AddPolicy(
        AuthPolicies.VerifiedEmail,
        policy => policy.RequireClaim(AuthPolicies.EmailVerifiedClaim, "true")
    )
);

// ── Trax ─────────────────────────────────────────────────────────────────────
// AddScheduler provides what the operations namespace runs on (IOperationsService,
// ITraxScheduler and a job submitter); without them, exposing it stops the host at startup.
//
// No SaveTrainParameters(): a saved input is readable by whoever passes the operations gate,
// through operations.executionDetail { input }. PublishArticle's input carries the editor note,
// which only editors and auditors may read, so saving it would hand the note to every operator.
// [TraxSensitive] does not help here: it masks copies, not the saved input itself.
builder.Services.AddTrax(trax =>
    trax.AddEffects(effects => effects.UsePostgres(connectionString).AddJson())
        .AddMediator(typeof(Program).Assembly)
        .AddScheduler(scheduler => scheduler)
);

builder.Services.AddDomainDataContext<INewsroomDbContext, NewsroomDbContext>(options =>
    options.UseNpgsql(connectionString)
);

// ── GraphQL ──────────────────────────────────────────────────────────────────
builder.Services.AddTraxGraphQL(graphql =>
    graphql
        .AddDbContext<NewsroomDbContext>()
        .AddTypeExtension<ArticleExtensions>()
        // The operations namespace reads execution inputs and history and queues, cancels and
        // reconfigures work. Exposing it without a gate stops the host at startup.
        .ExposeOperationQueries()
        .ExposeOperationMutations()
        // Gate that namespace alone. RequireAuthorization() would gate the whole endpoint, and
        // take the anonymous echo train and the public articles down with it.
        .GateOperations(roles: AuthRoles.Operator)
        // Record every request: who, what, allowed or not. Literals in the document are blanked
        // and variables are not recorded, so the trail holds no input values.
        .AddAudit<DatabaseAuditSink>(audit =>
        {
            audit.BatchSize = 20;
            audit.FlushInterval = TimeSpan.FromMilliseconds(250);
        })
);

builder.Services.AddHealthChecks().AddTraxHealthCheck();

var app = builder.Build();

// Create the newsroom schema and seed it. EnsureSchemaCreatedAsync is for demos and tests; use
// migrations in production.
await app.Services.EnsureSchemaCreatedAsync<NewsroomDbContext>();
await SeedAsync(app.Services);

app.UseAuthentication();
app.UseAuthorization();
app.UseTraxGraphQL();
app.MapHealthChecks("/trax/health");

if (app.Environment.IsDevelopment())
{
    // Mints a demo token for a demo user. Development only, like the key that signs it.
    app.MapGet(
            "/dev/token/{user}",
            (string user) =>
                DemoCredentials.Users.TryGetValue(user, out var demoUser)
                    ? Results.Ok(new { token = DemoCredentials.MintToken(demoUser) })
                    : Results.NotFound()
        )
        .AllowAnonymous();
}

app.Run();

static async Task SeedAsync(IServiceProvider services)
{
    using var scope = services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<INewsroomDbContext>();

    if (await db.Articles.AnyAsync())
        return;

    db.Articles.AddRange(
        new Article
        {
            Title = "Trax ships an auth sample",
            Body = "Two schemes, one principal, and an audit trail that says who did what.",
            AuthorId = "TraxApiKey:alice",
            PublishedAt = DateTime.UtcNow.AddHours(-2),
            EditorNote = new EditorNote { Text = "Check the quote with legal before Friday." },
        },
        new Article
        {
            Title = "Reading the audit trail",
            Body = "Auditors query it over GraphQL like any other model.",
            AuthorId = "TraxJwt:alice",
            PublishedAt = DateTime.UtcNow.AddHours(-1),
        }
    );
    await db.SaveChangesAsync();
}

/// <summary>A production API key as configuration holds it: never the key itself.</summary>
internal sealed record ConfiguredApiKey(string Id, string Salt, string Hash, string[] Roles);

namespace Trax.Samples.Auth
{
    public partial class Program;
}
