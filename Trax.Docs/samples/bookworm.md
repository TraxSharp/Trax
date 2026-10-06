---
layout: default
title: Bookworm
description: "The Bookworm sample: two domain contexts, a batched cross-schema GraphQL edge, owner-scoped rows, and the architecture guards adopted by a consumer."
parent: Samples & Deployment
nav_order: 7
---

# Bookworm

`samples/Bookworm` is a library API over two domains, each with its own project, PostgreSQL schema
and EF Core context: `catalog` (books, authors) and `lending` (members, loans). A loan lives in
`lending` and its book in `catalog`; GraphQL joins them with a batched cross-schema edge. It is also
the one place the Trax architecture-guard packages are adopted across a real `PackageReference`,
as any consumer would adopt them.

## What it proves

| Feature | Where |
|---|---|
| One project, one schema, one context, through `DomainDataContext<TSelf>` | `Trax.Samples.Bookworm.Catalog`, `Trax.Samples.Bookworm.Lending` |
| A cross-schema GraphQL edge, `loan.book`, batched by `CrossSchemaLoader` | `Trax.Samples.Bookworm.CrossSchema/Edges/LoanToBookEdge.cs` |
| Owner-scoped rows: a member reads their own member row and loans, a librarian reads all, an anonymous caller none | `LendingDbContext` query filters, `TraxLendingCaller` |
| Trains that act as the caller and refuse what they must: a missing book, a book on loan, another member's loan | `Trains/Lending` |
| A partial unique index that stops two concurrent borrows of one book | `LendingDbContext` |
| The guard fixtures, the owner-scope census included, adopted by a consumer | `tests/Trax.Samples.Tests.Reflection/BookwormArchitectureGuards.cs` |

## Layout

```
samples/Bookworm/
├── Trax.Samples.Bookworm.Catalog/      catalog schema: Book, Author, CatalogDbContext
├── Trax.Samples.Bookworm.Lending/      lending schema: Member, Loan, LendingDbContext, ILendingCaller
├── Trax.Samples.Bookworm.CrossSchema/  the only project that references both: the loan.book edge
├── Trax.Samples.Bookworm/              trains, services, TraxLendingCaller, demo key constants
└── Trax.Samples.Bookworm.Api/          the host (Program.cs)
```

## Run

From the `Trax.Samples` root:

```bash
docker compose up -d                                        # Postgres on localhost:5432, database trax_bookworm
dotnet run --project samples/Bookworm/Trax.Samples.Bookworm.Api   # Development, http://localhost:5250
```

The host creates both schemas with `EnsureSchemaCreatedAsync` and seeds two books, two members
(Ada Reader and Grace Hopper) and one loan (Ada has The Hobbit). `EnsureSchemaCreatedAsync` never
alters a table that already exists, so after running an older Bookworm against the same database,
drop the schemas first: `DROP SCHEMA lending CASCADE; DROP SCHEMA catalog CASCADE;`.

## Try it

| Key (Development only) | Caller |
|---|---|
| `member-key-do-not-use-in-production` | member Ada Reader, `TraxApiKey:member` |
| `other-member-key-do-not-use-in-production` | member Grace Hopper, `TraxApiKey:other-member` |
| `librarian-key-do-not-use-in-production` | a librarian, not a member |

```bash
G=http://localhost:5250/trax/graphql
gql() { curl -s $G -H 'Content-Type: application/json' -H "X-Api-Key: $1" -d "$2"; echo; }
ADA=member-key-do-not-use-in-production
GRACE=other-member-key-do-not-use-in-production
LIB=librarian-key-do-not-use-in-production

# The cross-schema edge: each of Ada's loans with its catalog book, in one batched catalog query
gql $ADA '{"query":"{ discover { lending { loans { nodes { id bookId book { title isbn } } } } } }"}'
# {"data":{"discover":{"lending":{"loans":{"nodes":[{"id":1,"bookId":1,"book":{"title":"The Hobbit","isbn":"978-0345339683"}}]}}}}}

# Ada reads only her own member row; the librarian reads both
gql $ADA '{"query":"{ discover { lending { members { nodes { name email } } } } }"}'
# {"data":{"discover":{"lending":{"members":{"nodes":[{"name":"Ada Reader","email":"ada@example.com"}]}}}}}
gql $LIB '{"query":"{ discover { lending { members { nodes { name email } } } } }"}'

# Grace borrows book 2. Ada cannot borrow it while it is out, nor a book the catalog does not have
gql $GRACE '{"query":"mutation { dispatch { lending { borrowBook(input: { bookId: 2 }) { output { loanId dueAt } } } } }"}'
gql $ADA '{"query":"mutation { dispatch { lending { borrowBook(input: { bookId: 2 }) { output { loanId } } } } }"}'
# "Book 2 is already on loan."
gql $ADA '{"query":"mutation { dispatch { lending { borrowBook(input: { bookId: 999 }) { output { loanId } } } } }"}'
# "Book 999 is not in the catalog."

# Ada cannot return Grace's loan: to her it does not exist. Grace can.
gql $ADA '{"query":"mutation { dispatch { lending { returnBook(input: { loanId: 2 }) { output { loanId } } } } }"}'
# "Loan 2 not found."
gql $GRACE '{"query":"mutation { dispatch { lending { returnBook(input: { loanId: 2 }) { output { loanId returnedAt } } } } }"}'

# Without a key the catalog is public and lending is refused
curl -s $G -H 'Content-Type: application/json' \
  -d '{"query":"{ discover { catalog { searchCatalog(input: { query: \"earth\" }) { books { id title } } } } }"}'
curl -s $G -H 'Content-Type: application/json' \
  -d '{"query":"{ discover { lending { members { nodes { email } } } } }"}'
# {"errors":[{"message":"Not authorized.","extensions":{"code":"TRAX_AUTHORIZATION"}}],...}
```

## How it works

### Two contexts, one database

Each domain context derives [`DomainDataContext<TSelf>`](/docs/effect/effect-providers/domain-data-contexts),
declares its one schema, and ships a companion interface that application code depends on:

```csharp
public class CatalogDbContext(DbContextOptions<CatalogDbContext> options)
    : DomainDataContext<CatalogDbContext>(options), ICatalogDbContext
{
    public DbSet<Author> Authors => Set<Author>();
    public DbSet<Book> Books => Set<Book>();

    protected override string Schema => "catalog";

    protected override void ConfigureModel(ModelBuilder modelBuilder) { /* keys, indexes */ }
}

services.AddDomainDataContext<ICatalogDbContext, CatalogDbContext>(o => o.UseNpgsql(connectionString));
```

A domain never references another one. A loan's book is a plain integer column, `book_id`, with no
EF navigation and no foreign key across schemas.

### The cross-schema edge

`loan.book` is added to the `Loan` GraphQL type by an `[ExtendObjectType]` class in the
`CrossSchema` project, the only project allowed to reference both domains. EF Core cannot join
across two contexts, so the resolver goes through a batched loader that collects every book id the
request asks for and issues one `WHERE id IN (...)` against the catalog:

```csharp
[ExtendObjectType(typeof(Loan))]
public sealed class LoanToBookEdge
{
    public async Task<Book?> GetBook(
        [Parent(requires: nameof(Loan.BookId))] Loan loan,
        CrossSchemaLoader<CatalogDbContext, Book> books,
        CancellationToken cancellationToken
    ) => await books.LoadAsync(loan.BookId, cancellationToken);
}

// Program.cs
builder.Services.AddTraxGraphQL(graphql => graphql
    .AddDbContext<CatalogDbContext>()
    .AddDbContext<LendingDbContext>()
    .AddTypeExtensions(typeof(LoanToBookEdge).Assembly));
builder.Services.AddCrossSchemaLoader<CatalogDbContext, Book>();
```

`[Parent(requires: ...)]` matters: projection selects only the columns a query asks for, so without
it `loan.BookId` arrives as `0` when the query does not select `bookId`, and the book resolves to
nothing. The field declares no posture of its own because it inherits the gate of `Loan`. Every edge
is listed in `CrossSchemaEdges.All`, which the guards read. See
[Cross-schema data loaders](/docs/sdk-reference/graphql-api/cross-schema-data-loaders).

### Owner-scoped rows

`Member` and `Loan` are `[TraxQueryModel]`s with a bare `[TraxAuthorize]`: a caller must be
authenticated to reach the type at all. Which rows they see is decided by query filters on
`LendingDbContext`, which read an `ILendingCaller` the host binds over Trax's
[`TraxCaller`](/docs/sdk-reference/api-auth/trax-caller):

```csharp
public interface ILendingCaller
{
    string? PrincipalId { get; }   // TraxApiKey:member, or null when anonymous
    bool IsLibrarian { get; }
}

public sealed class TraxLendingCaller(TraxCaller caller) : ILendingCaller
{
    public string? PrincipalId => caller.Principal?.Id;
    public bool IsLibrarian => caller.Principal?.Roles.Contains("Librarian") == true;
}
```

```csharp
modelBuilder.Entity<Member>().HasQueryFilter(m =>
    _caller.IsLibrarian || (_caller.PrincipalId != null && m.PrincipalId == _caller.PrincipalId));

modelBuilder.Entity<Loan>().HasQueryFilter(l =>
    _caller.IsLibrarian
    || Set<Member>().Any(m => m.Id == l.MemberId
        && _caller.PrincipalId != null && m.PrincipalId == _caller.PrincipalId));
```

A member row carries the principal id of the key that acts for it, seeded with
`TraxPrincipalId.Qualify(ApiKeyDefaults.SchemeName, "member")`. The filters apply to every query
through the context, so the GraphQL query models and the trains read through the same rule: the
borrow train finds the caller's own member row, and the return train looks the loan up through the
filter, so another member's loan is simply not found.

A context that takes the caller cannot come from `AddDomainDataContext`, whose pooled factory builds
contexts from their options alone. The lending context registers an unpooled, scoped factory
instead; [Owner-scoped contexts](/docs/effect/effect-providers/domain-data-contexts#owner-scoped-contexts)
has the full registration and the reasons for each piece.

Two reads step outside the filter on purpose, each with `IgnoreQueryFilters()`: the borrow train's
availability check (whether anyone has the book out, a yes or no) and the startup seed (which runs
with no caller). Both are listed, with their reasons, in the census's `FilterBypassAllowlist`.

### Lending integrity

```csharp
if (!await catalog.Books.AnyAsync(b => b.Id == input.BookId))
    throw new TrainException($"Book {input.BookId} is not in the catalog.");

if (await lending.Loans.IgnoreQueryFilters()
        .AnyAsync(l => l.BookId == input.BookId && l.ReturnedAt == null))
    throw new TrainException($"Book {input.BookId} is already on loan.");
```

The check gives a readable error; it cannot stop two borrows that both pass it before either
inserts. A partial unique index does, `entity.HasIndex(e => e.BookId).IsUnique().HasFilter("returned_at IS NULL")`,
and the train turns the resulting unique violation into the same "already on loan" message.

### The guards, adopted

```csharp
[TestFixture]
public sealed class BookwormDataLayerGuards : DomainDataLayerGuardFixture
{
    protected override ArchitectureGuardOptions Options => new() { SourceScanRoots = ["samples"] };

    protected override IReadOnlyList<Type> DomainContexts =>
        [typeof(CatalogDbContext), typeof(LendingDbContext)];

    protected override IReadOnlyList<IReadOnlyModel> OwnerScopedModels
    {
        get
        {
            var options = new DbContextOptionsBuilder<LendingDbContext>()
                .UseNpgsql("Host=localhost;Database=model_only").Options;
            using var context = new LendingDbContext(options, NoLendingCaller.Instance);
            return [context.Model];
        }
    }

    protected override OwnerScopeCensusOptions OwnerScope => new()
    {
        OwnerType = typeof(Member),
        PrincipalAccessorType = typeof(ILendingCaller),
        FilterBypassAllowlist = new Dictionary<string, string>
        {
            ["samples/Bookworm/Trax.Samples.Bookworm/Trains/Lending/BorrowBook/Junctions/BorrowBookJunction.cs"] =
                "a book's availability depends on every member's open loans; the read returns a yes or no, no row",
            ["samples/Bookworm/Trax.Samples.Bookworm.Api/Program.cs"] =
                "startup seeding runs with no caller, so it checks for existing members past the filter",
        },
    };
}
```

`BookwormCrossSchemaGuards` and `BookwormTrainGuards` subclass the GraphQL and train fixtures the
same way. The census fails if either lending filter is removed, and the bypass scan fails if an
allowlisted file stops bypassing (a stale entry would silently cover whatever the file does next).
See [Architecture Guards](/docs/reference/architecture-guards).

## Tests

```bash
dotnet test tests/Trax.Samples.Bookworm.E2E          # the real host against Postgres (28 tests)
dotnet test tests/Trax.Samples.Tests.Reflection      # the architecture guards (10 tests)
```

The E2E suite runs against the `bookworm_e2e_tests` database on port 5432; `TRAX_TEST_PG_PORT`
moves the port and `BOOKWORM_TEST_DB` replaces the connection string. It drops both schemas at the
start of a run, gives every borrowing test a book of its own, and fails rather than skips when the
database is missing.

| Class | Proves |
|---|---|
| `LendingOwnershipTests` | anonymous callers read no member or loan; a member reads only their own row and loans and cannot return another's loan; a librarian reads and returns everything |
| `LendingIntegrityTests` | no loan of a missing book or a book on loan; a returned book can be lent again; six concurrent borrows of one book record one loan; a loan belongs to its caller |
| `CrossSchemaEdgeTests` | `loan.book` resolves across schemas for every loan |
| `ProductionPostureTests` | in Production the demo keys do not exist, lending is refused, the catalog stays public |
| `AuthTests`, `ReturnBookTests`, `CatalogSearchTests` | the train gates and the return and search paths |

## SDK Reference

> [DomainDataContext](/docs/sdk-reference/configuration/domain-data-context) | [Cross-schema data loaders](/docs/sdk-reference/graphql-api/cross-schema-data-loaders) | [TraxQueryModel](/docs/sdk-reference/graphql-api/query-models) | [TraxAuthorize](/docs/sdk-reference/attributes/trax-authorize) | [TraxCaller](/docs/sdk-reference/api-auth/trax-caller) | [TraxPrincipal](/docs/sdk-reference/api-auth/trax-principal) | [AddTraxGraphQL](/docs/sdk-reference/graphql-api/add-trax-graphql) | [Architecture guards](/docs/reference/architecture-guards)
