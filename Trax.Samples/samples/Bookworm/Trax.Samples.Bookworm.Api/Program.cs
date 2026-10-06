// ─────────────────────────────────────────────────────────────────────────────
// Trax Bookworm: cross-schema GraphQL over two domain contexts
//
//   - Two domains, each its own project + PostgreSQL schema + DbContext (1:1:1):
//       catalog  (books, authors)   lending (members, loans)
//   - A cross-schema GraphQL edge: loan.book resolves a catalog Book from a lending
//     Loan via a batched DataLoader living in the separate .CrossSchema project.
//   - Owner-scoped lending data: a member reads only their own member row and
//     loans, a librarian reads all of them, an anonymous caller reads none.
//   - The architecture guards adopted as a consumer would
//     (tests/Trax.Samples.Tests.Reflection/BookwormArchitectureGuards.cs).
//
// Auth (NO WARRANTY, demo keys, registered only in Development):
//   member-key-do-not-use-in-production        member Ada Reader  (role Member)
//   other-member-key-do-not-use-in-production  member Grace Hopper (role Member)
//   librarian-key-do-not-use-in-production     a librarian         (role Librarian)
//   Send as header  X-Api-Key: <key>
//
// Run:
//   1. Postgres:  docker compose up -d        (from the Trax.Samples root)
//   2. API:       dotnet run --project samples/Bookworm/Trax.Samples.Bookworm.Api
//                 (Development, http://localhost:5250)
//
// Try the cross-schema edge (one batched catalog query resolves every loan.book):
//   curl -s http://localhost:5250/trax/graphql -H "Content-Type: application/json" \
//        -H "X-Api-Key: member-key-do-not-use-in-production" \
//        -d '{"query":"{ discover { lending { loans { nodes { id bookId book { title isbn } } } } } }"}'
// More in samples/Bookworm/README.md.
// ─────────────────────────────────────────────────────────────────────────────

using Microsoft.EntityFrameworkCore;
using Trax.Api.Auth;
using Trax.Api.Auth.ApiKey;
using Trax.Api.Extensions;
using Trax.Api.GraphQL.Extensions;
using Trax.Effect.Data.Extensions;
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Data.Services.DomainContext;
using Trax.Effect.Extensions;
using Trax.Effect.Provider.Json.Extensions;
using Trax.Mediator.Extensions;
using Trax.Samples.Bookworm;
using Trax.Samples.Bookworm.Auth;
using Trax.Samples.Bookworm.Catalog.Context;
using Trax.Samples.Bookworm.Catalog.Extensions;
using Trax.Samples.Bookworm.Catalog.Models.Authors;
using Trax.Samples.Bookworm.Catalog.Models.Books;
using Trax.Samples.Bookworm.CrossSchema.Extensions;
using Trax.Samples.Bookworm.Lending.Context;
using Trax.Samples.Bookworm.Lending.Extensions;
using Trax.Samples.Bookworm.Lending.Models.Loans;
using Trax.Samples.Bookworm.Lending.Models.Members;
using Trax.Samples.Bookworm.Services;
using CrossSchemaMarker = Trax.Samples.Bookworm.CrossSchema.Extensions.BookwormCrossSchemaServiceCollectionExtensions;
using SampleKeys = Trax.Samples.Bookworm.Auth.ApiKeyDefaults;
using TraxApiKey = Trax.Api.Auth.ApiKey.ApiKeyDefaults;

var builder = WebApplication.CreateBuilder(args);

var connectionString =
    builder.Configuration.GetConnectionString("TraxDatabase")
    ?? throw new InvalidOperationException("Connection string 'TraxDatabase' not found.");

builder.Services.AddLogging(logging => logging.AddConsole());

// ── Auth: API-key wiring (NO WARRANTY, demo keys) ────────────────────────
// The demo keys are published in this repository, so they are registered only in Development
// (Properties/launchSettings.json sets it for `dotnet run`). Anywhere else no credential exists
// until you register real ones, and every [TraxAuthorize] operation is refused.
if (builder.Environment.IsDevelopment())
    builder.Services.AddTraxApiKeyAuth(keys =>
        keys.Add(SampleKeys.MemberKey, id: "member", BookwormRoles.Member)
            .Add(SampleKeys.OtherMemberKey, id: "other-member", BookwormRoles.Member)
            .Add(SampleKeys.LibrarianKey, id: "librarian", BookwormRoles.Librarian)
    );

// TraxCaller, which the lending filters read through TraxLendingCaller, is registered by every
// Trax auth scheme. Outside Development no scheme is registered above, so register it directly.
builder.Services.AddTraxPrincipalAccessor();
builder.Services.AddAuthentication();
builder.Services.AddAuthorization();

// ── Trax effect + mediator (trains, bus, execution) ──────────────────────
builder.Services.AddTrax(trax =>
    trax.AddEffects(effects =>
            effects.UsePostgres(connectionString).AddDataContextLogging().AddJson()
        )
        .AddMediator(typeof(AssemblyMarker).Assembly)
);

// ── Domain data contexts (one per schema, via the shared registration) ───
builder.Services.AddCatalogDataContext(connectionString);
builder.Services.AddLendingDataContext<TraxLendingCaller>(connectionString);

// ── Lending services ─────────────────────────────────────────────────────
builder.Services.AddSingleton<ILoanPolicy, LoanPolicy>();

// ── GraphQL: expose both contexts' query models + the cross-schema edge ──
builder.Services.AddTraxGraphQL(graphql =>
    graphql
        .MaxExecutionDepth(12)
        .AddDbContext<CatalogDbContext>()
        .AddDbContext<LendingDbContext>()
        // The loan.book edge resolvers ([ExtendObjectType]) live in the CrossSchema assembly.
        .AddTypeExtensions(typeof(CrossSchemaMarker).Assembly)
);

// Batched loaders behind the cross-schema edges (one per target context/entity).
builder.Services.AddBookwormCrossSchema();

builder.Services.AddHealthChecks().AddTraxHealthCheck();

var app = builder.Build();

// ── Create each domain's schema + tables, then seed demo data ────────────
await app.Services.EnsureSchemaCreatedAsync<CatalogDbContext>();
await app.Services.EnsureSchemaCreatedAsync<LendingDbContext>();
await SeedAsync(app.Services);

app.UseAuthentication();
app.UseAuthorization();
app.UseTraxGraphQL();
app.MapHealthChecks("/trax/health");

app.Run();

static async Task SeedAsync(IServiceProvider services)
{
    using var scope = services.CreateScope();
    var catalog = scope.ServiceProvider.GetRequiredService<ICatalogDbContext>();
    var lending = scope.ServiceProvider.GetRequiredService<ILendingDbContext>();

    if (!await catalog.Books.AnyAsync())
    {
        var tolkien = new Author { Name = "J.R.R. Tolkien" };
        var leguin = new Author { Name = "Ursula K. Le Guin" };
        catalog.Authors.AddRange(tolkien, leguin);
        await catalog.SaveChangesAsync();

        catalog.Books.AddRange(
            new Book
            {
                Title = "The Hobbit",
                Isbn = "978-0345339683",
                AuthorId = tolkien.Id,
            },
            new Book
            {
                Title = "A Wizard of Earthsea",
                Isbn = "978-0553383041",
                AuthorId = leguin.Id,
            }
        );
        await catalog.SaveChangesAsync();
    }

    // Startup has no caller, so the owner filter would hide every member: seeding reads past it.
    if (!await lending.Members.IgnoreQueryFilters().AnyAsync())
    {
        var member = new Member
        {
            Name = "Ada Reader",
            Email = "ada@example.com",
            PrincipalId = TraxPrincipalId.Qualify(TraxApiKey.SchemeName, "member"),
        };
        var otherMember = new Member
        {
            Name = "Grace Hopper",
            Email = "grace@example.com",
            PrincipalId = TraxPrincipalId.Qualify(TraxApiKey.SchemeName, "other-member"),
        };
        lending.Members.AddRange(member, otherMember);
        await lending.SaveChangesAsync();

        var firstBookId = await catalog.Books.OrderBy(b => b.Id).Select(b => b.Id).FirstAsync();
        lending.Loans.Add(
            new Loan
            {
                MemberId = member.Id,
                BookId = firstBookId,
                BorrowedAt = DateTime.UtcNow,
            }
        );
        await lending.SaveChangesAsync();
    }
}

namespace Trax.Samples.Bookworm.Api
{
    public partial class Program;
}
