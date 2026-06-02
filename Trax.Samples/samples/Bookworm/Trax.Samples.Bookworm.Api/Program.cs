// ─────────────────────────────────────────────────────────────────────────────
// Trax Bookworm — GraphQL API (flagship multi-schema sample)
//
// Demonstrates the opinionated Trax data/service/train/auth stack:
//   - Two domains, each its own project + PostgreSQL schema + DbContext (1:1:1):
//       catalog  (books, authors)   lending (members, loans)
//   - A cross-schema GraphQL edge: loan.book resolves a catalog Book from a lending
//     Loan via a batched DataLoader living in the separate .CrossSchema project.
//   - Shared base data context, shared API-key auth, folder-per-feature trains.
//
// Auth (NO WARRANTY, demo keys only):
//   Member key:    member-key-do-not-use-in-production    (role: Member)
//   Librarian key: librarian-key-do-not-use-in-production (roles: Librarian, Member)
//   Send as header  X-Api-Key: <key>
//
// Prerequisites:
//   1. Start Postgres:  cd Trax.Samples && docker compose up -d
//   2. Pack local:      ./pack-local.sh
//   3. Run:             dotnet run --project samples/Bookworm/Trax.Samples.Bookworm.Api
//
// Try the cross-schema edge (one batched catalog query resolves every loan.book):
//   curl -H "X-Api-Key: member-key-do-not-use-in-production" \
//        -X POST http://localhost:5210/trax/graphql -H "Content-Type: application/json" \
//        -d '{"query":"{ lending { loans { nodes { id bookId book { title isbn } } } } }"}'
// ─────────────────────────────────────────────────────────────────────────────

using Microsoft.EntityFrameworkCore;
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

var builder = WebApplication.CreateBuilder(args);

var connectionString =
    builder.Configuration.GetConnectionString("TraxDatabase")
    ?? throw new InvalidOperationException("Connection string 'TraxDatabase' not found.");

builder.Services.AddLogging(logging => logging.AddConsole());

// ── Auth: API-key wiring (NO WARRANTY, demo keys) ────────────────────────
builder.Services.AddTraxApiKeyAuth(keys =>
    keys.Add(SampleKeys.MemberKey, id: "member", BookwormRoles.Member)
        .Add(
            SampleKeys.LibrarianKey,
            id: "librarian",
            BookwormRoles.Librarian,
            BookwormRoles.Member
        )
);
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
builder.Services.AddLendingDataContext(connectionString);

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

    if (!await lending.Members.AnyAsync())
    {
        var member = new Member { Name = "Ada Reader", Email = "ada@example.com" };
        lending.Members.Add(member);
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
