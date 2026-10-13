---
title: Bookworm
description: "The Bookworm sample: two domain contexts, a batched cross-schema GraphQL edge, owner-scoped rows, and the architecture guards adopted by a consumer."
type: explanation
area: samples
nav_order: 6
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

### Layout

```
samples/Bookworm/
├── Trax.Samples.Bookworm.Catalog/      catalog schema: Book, Author, CatalogDbContext
├── Trax.Samples.Bookworm.Lending/      lending schema: Member, Loan, LendingDbContext, ILendingCaller
├── Trax.Samples.Bookworm.CrossSchema/  the only project that references both: the loan.book edge
├── Trax.Samples.Bookworm/              trains, services, TraxLendingCaller, demo key constants
└── Trax.Samples.Bookworm.Api/          the host (Program.cs)
```

### Two contexts, one database

Each domain context derives [`DomainDataContext<TSelf>`](/docs/effect/effect-providers/domain-data-contexts),
declares its one schema, and ships a companion interface that application code depends on:

```csharp from=Trax.Samples/samples/Bookworm/Trax.Samples.Bookworm.Catalog/Context/CatalogDbContext.cs
public class CatalogDbContext(DbContextOptions<CatalogDbContext> options)
    : DomainDataContext<CatalogDbContext>(options),
        ICatalogDbContext
{
    public DbSet<Author> Authors => Set<Author>();
    public DbSet<Book> Books => Set<Book>();

    protected override string Schema => CatalogSchema.Name;

    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
// ...
    }
}
```

`CatalogSchema.Name` is `"catalog"`. The catalog project registers its context for the host:

```csharp from=Trax.Samples/samples/Bookworm/Trax.Samples.Bookworm.Catalog/Extensions/CatalogServiceCollectionExtensions.cs
public static IServiceCollection AddCatalogDataContext(
    this IServiceCollection services,
    string connectionString
) =>
    services.AddDomainDataContext<ICatalogDbContext, CatalogDbContext>(options =>
        options.UseNpgsql(connectionString)
    );
```

A domain never references another one. A loan's book is a plain integer column, `book_id`, with no
EF navigation and no foreign key across schemas.

### The cross-schema edge

`loan.book` is added to the `Loan` GraphQL type by an `[ExtendObjectType]` class in the
`CrossSchema` project, the only project allowed to reference both domains. EF Core cannot join
across two contexts, so the resolver goes through a batched loader that collects every book id the
request asks for and issues one `WHERE id IN (...)` against the catalog:

```csharp from=Trax.Samples/samples/Bookworm/Trax.Samples.Bookworm.CrossSchema/Edges/LoanToBookEdge.cs
[ExtendObjectType(typeof(Loan))]
public sealed class LoanToBookEdge
{
// ...
    public async Task<Book?> GetBook(
        [Parent(requires: nameof(Loan.BookId))] Loan loan,
        CrossSchemaLoader<CatalogDbContext, Book> books,
        CancellationToken cancellationToken
    ) => await books.LoadAsync(loan.BookId, cancellationToken);
}
```

The host adds the edge's assembly to the schema and registers the loader:

```csharp from=Trax.Samples/samples/Bookworm/Trax.Samples.Bookworm.Api/Program.cs
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
```

```csharp from=Trax.Samples/samples/Bookworm/Trax.Samples.Bookworm.CrossSchema/Extensions/BookwormCrossSchemaServiceCollectionExtensions.cs
public static IServiceCollection AddBookwormCrossSchema(this IServiceCollection services)
{
    ArgumentNullException.ThrowIfNull(services);
    services.AddCrossSchemaLoader<CatalogDbContext, Book>();
    return services;
}
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

```csharp from=Trax.Samples/samples/Bookworm/Trax.Samples.Bookworm.Lending/Context/ILendingCaller.cs
public interface ILendingCaller
{
    /// <summary>The caller's qualified principal id (<c>TraxApiKey:member</c>), or null when anonymous.</summary>
    string? PrincipalId { get; }

    /// <summary>True when the caller may read every member and loan.</summary>
    bool IsLibrarian { get; }
}
```

```csharp from=Trax.Samples/samples/Bookworm/Trax.Samples.Bookworm/Auth/TraxLendingCaller.cs
public sealed class TraxLendingCaller(TraxCaller caller) : ILendingCaller
{
    public string? PrincipalId => caller.Principal?.Id;

    public bool IsLibrarian => caller.Principal?.Roles.Contains(BookwormRoles.Librarian) == true;
}
```

```csharp from=Trax.Samples/samples/Bookworm/Trax.Samples.Bookworm.Lending/Context/LendingDbContext.cs
entity.HasQueryFilter(m =>
    _caller.IsLibrarian
    || (_caller.PrincipalId != null && m.PrincipalId == _caller.PrincipalId)
);
// ...
entity.HasQueryFilter(l =>
    _caller.IsLibrarian
    || Set<Member>()
        .Any(m =>
            m.Id == l.MemberId
            && _caller.PrincipalId != null
            && m.PrincipalId == _caller.PrincipalId
        )
);
```

A member row carries the principal id of the key that acts for it, seeded with
`TraxPrincipalId.Qualify(ApiKeyDefaults.SchemeName, "member")`. The filters apply to every query
through the context, so the GraphQL query models and the trains read through the same rule: the
borrow train finds the caller's own member row, and the return train looks the loan up through the
filter, so another member's loan is simply not found.

A context that takes the caller cannot come from `AddDomainDataContext`, whose pooled factory builds
contexts from their options alone. The lending context registers an unpooled, scoped factory
instead (`AddLendingDataContext<TraxLendingCaller>`);
[Owner-scoped contexts](/docs/effect/effect-providers/domain-data-contexts#owner-scoped-contexts)
has the full registration and the reasons for each piece.

Two reads step outside the filter on purpose, each with `IgnoreQueryFilters()`: the borrow train's
availability check (whether anyone has the book out, a yes or no) and the startup seed (which runs
with no caller). Both are listed, with their reasons, in the census's `FilterBypassAllowlist`.

### Lending integrity

```csharp from=Trax.Samples/samples/Bookworm/Trax.Samples.Bookworm/Trains/Lending/BorrowBook/Junctions/BorrowBookJunction.cs
if (!await catalog.Books.AnyAsync(b => b.Id == input.BookId))
    throw new TrainException($"Book {input.BookId} is not in the catalog.");

// Availability depends on every member's loans, not only the caller's, so this one read
// steps outside the owner filter. It returns a yes or no and no row.
var onLoan = await lending
    .Loans.IgnoreQueryFilters()
    .AnyAsync(l => l.BookId == input.BookId && l.ReturnedAt == null);
if (onLoan)
    throw new TrainException($"Book {input.BookId} is already on loan.");
// ...
try
{
    await lending.SaveChangesAsync();
}
catch (DbUpdateException ex)
    when (ex.InnerException
            is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation }
    )
{
    // Another borrow of the same book committed between the check and this insert; the
    // partial unique index on open loans refused the second one.
    throw new TrainException($"Book {input.BookId} is already on loan.");
}
```

The check gives a readable error; it cannot stop two borrows that both pass it before either
inserts. A partial unique index on open loans does, and the `catch` turns its unique violation into
the same "already on loan" message:

```csharp from=Trax.Samples/samples/Bookworm/Trax.Samples.Bookworm.Lending/Context/LendingDbContext.cs
entity.HasIndex(e => e.BookId).IsUnique().HasFilter("returned_at IS NULL");
```

### The guards, adopted

```csharp from=Trax.Samples/tests/Trax.Samples.Tests.Reflection/BookwormArchitectureGuards.cs
[TestFixture]
public sealed class BookwormDataLayerGuards : DomainDataLayerGuardFixture
{
    protected override ArchitectureGuardOptions Options => new() { SourceScanRoots = ["samples"] };

    protected override IReadOnlyList<Type> DomainContexts =>
        [typeof(CatalogDbContext), typeof(LendingDbContext)];

    // The lending model, built offline (no database, no caller), for the owner-scope census.
    protected override IReadOnlyList<IReadOnlyModel> OwnerScopedModels
    {
        get
        {
            var options = new DbContextOptionsBuilder<LendingDbContext>()
                .UseNpgsql("Host=localhost;Database=model_only")
                .Options;
            using var context = new LendingDbContext(options, NoLendingCaller.Instance);
            return [context.Model];
        }
    }

    protected override OwnerScopeCensusOptions OwnerScope =>
        new()
        {
            OwnerType = typeof(Member),
            PrincipalAccessorType = typeof(ILendingCaller),
            FilterBypassAllowlist = new Dictionary<string, string>
            {
                [
                    "samples/Bookworm/Trax.Samples.Bookworm/Trains/Lending/BorrowBook/Junctions/BorrowBookJunction.cs"
                ] =
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
dotnet test tests/Trax.Samples.Bookworm.E2E          # the real host against Postgres
dotnet test tests/Trax.Samples.Tests.Reflection      # the architecture guards
```

The E2E suite runs against the `bookworm_e2e_tests` database on port 5432; `TRAX_TEST_PG_PORT`
moves the port and `BOOKWORM_TEST_DB` replaces the connection string. It drops both schemas at the
start of a run, gives every borrowing test a book of its own, and fails rather than skips when the
database is missing.

| Class | Proves |
|---|---|
| `LendingOwnershipTests` | anonymous callers read no member or loan; a member reads only their own row and loans and cannot return another's loan; a librarian reads and returns everything |
| `LendingIntegrityTests` | no loan of a missing book or a book on loan; a returned book can be lent again; six concurrent borrows of one book record one loan; a loan belongs to its caller |
| `LendingMutationModeTests` | `borrowBook` and `returnBook` offer no `mode` argument, since a queued run would have no caller |
| `CrossSchemaEdgeTests` | `loan.book` resolves across schemas for every loan |
| `CrossSchemaModelTests` | a context that reads `Book` cross-schema pins it to the catalog schema and ignores its navigations (no database) |
| `ProductionPostureTests` | in Production the demo keys do not exist, lending is refused, the catalog stays public |
| `AuthTests`, `ReturnBookTests`, `CatalogSearchTests` | the train gates and the return and search paths |

## SDK Reference

> [DomainDataContext](/docs/sdk-reference/configuration/domain-data-context) | [Cross-schema data loaders](/docs/sdk-reference/graphql-api/cross-schema-data-loaders) | [TraxQueryModel](/docs/sdk-reference/graphql-api/query-models) | [TraxAuthorize](/docs/sdk-reference/attributes/trax-authorize) | [TraxCaller](/docs/sdk-reference/api-auth/trax-caller) | [TraxPrincipal](/docs/sdk-reference/api-auth/trax-principal) | [AddTraxGraphQL](/docs/sdk-reference/graphql-api/add-trax-graphql) | [Architecture guards](/docs/reference/architecture-guards)
