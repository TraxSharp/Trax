# Trax Bookworm Sample

A library API over two domains, each with its own project, PostgreSQL schema and EF Core context:
`catalog` (books, authors) and `lending` (members, loans). A loan lives in `lending` and its book in
`catalog`, and GraphQL joins them with a batched cross-schema edge.

## What it proves

- **Cross-schema GraphQL.** `loan.book` resolves a catalog `Book` from a lending `Loan` through
  `CrossSchemaLoader<CatalogDbContext, Book>`, so every `loan.book` in a request is one
  `WHERE id IN (...)` against the catalog.
- **Owner-scoped rows.** `Member` and `Loan` are bare `[TraxAuthorize]` query models, and
  `LendingDbContext` filters their rows by the caller: a member reads their own member row and
  loans, a librarian reads all of them, an anonymous caller reads none. The borrow and return
  trains go through the same filters.
- **Lending integrity.** A book must exist in the catalog and be on the shelf to be borrowed; a
  partial unique index on open loans stops two concurrent borrows of one book.
- **The architecture guards, adopted as a consumer would** (Samples ADR 0002):
  `tests/Trax.Samples.Tests.Reflection/BookwormArchitectureGuards.cs` subclasses the guard
  fixtures from `Trax.Effect.Data.Testing`, `Trax.Api.GraphQL.Testing` and `Trax.Mediator.Testing`,
  including the owner-scope census.

## Run

```bash
# From the Trax.Samples root: Postgres on localhost:5432 (database trax_bookworm)
docker compose up -d

# The API, in Development, on http://localhost:5250
dotnet run --project samples/Bookworm/Trax.Samples.Bookworm.Api
```

The host creates the `catalog` and `lending` schemas and seeds two books, two members and one loan.
It bootstraps with `EnsureSchemaCreatedAsync`, which never alters a table that exists: if you ran an
older Bookworm against the same database, drop the two schemas first
(`DROP SCHEMA lending CASCADE; DROP SCHEMA catalog CASCADE;`).

## Try it

The demo keys exist only in Development:

| Key | Caller |
|---|---|
| `member-key-do-not-use-in-production` | member Ada Reader |
| `other-member-key-do-not-use-in-production` | member Grace Hopper |
| `librarian-key-do-not-use-in-production` | a librarian (not a member) |

```bash
G=http://localhost:5250/trax/graphql
gql() { curl -s $G -H 'Content-Type: application/json' -H "X-Api-Key: $1" -d "$2"; echo; }
ADA=member-key-do-not-use-in-production
GRACE=other-member-key-do-not-use-in-production
LIB=librarian-key-do-not-use-in-production

# The cross-schema edge: Ada's loans, each with its catalog book
gql $ADA '{"query":"{ discover { lending { loans { nodes { id bookId book { title isbn } } } } } }"}'
# {"data":{"discover":{"lending":{"loans":{"nodes":[{"id":1,"bookId":1,"book":{"title":"The Hobbit","isbn":"978-0345339683"}}]}}}}}

# Ada reads only her own member row; the librarian reads both
gql $ADA '{"query":"{ discover { lending { members { nodes { name email } } } } }"}'
gql $LIB '{"query":"{ discover { lending { members { nodes { name email } } } } }"}'

# Grace borrows book 2; Ada cannot borrow it while it is out, nor a book that does not exist
gql $GRACE '{"query":"mutation { dispatch { lending { borrowBook(input: { bookId: 2 }) { output { loanId dueAt } } } } }"}'
gql $ADA '{"query":"mutation { dispatch { lending { borrowBook(input: { bookId: 2 }) { output { loanId } } } } }"}'
# "Book 2 is already on loan."
gql $ADA '{"query":"mutation { dispatch { lending { borrowBook(input: { bookId: 999 }) { output { loanId } } } } }"}'
# "Book 999 is not in the catalog."

# Ada cannot return Grace's loan (to her it does not exist); Grace can
gql $ADA '{"query":"mutation { dispatch { lending { returnBook(input: { loanId: 2 }) { output { loanId } } } } }"}'
# "Loan 2 not found."
gql $GRACE '{"query":"mutation { dispatch { lending { returnBook(input: { loanId: 2 }) { output { loanId returnedAt } } } } }"}'
```

Without a key, the catalog is public and lending is refused:

```bash
curl -s $G -H 'Content-Type: application/json' \
  -d '{"query":"{ discover { catalog { searchCatalog(input: { query: \"earth\" }) { books { id title } } } } }"}'
curl -s $G -H 'Content-Type: application/json' \
  -d '{"query":"{ discover { lending { members { nodes { email } } } } }"}'
# "Not authorized."
```

## Tests

```bash
dotnet test tests/Trax.Samples.Bookworm.E2E          # the real host against Postgres
dotnet test tests/Trax.Samples.Tests.Reflection      # the architecture guards
```

The E2E suite runs against the `bookworm_e2e_tests` database on port 5432 (`TRAX_TEST_PG_PORT`
moves the port, `BOOKWORM_TEST_DB` replaces the connection string). It drops and recreates the two
schemas at the start of each run, and fails, rather than skips, when the database is missing.

## Docs

[Bookworm sample](https://traxsharp.net/docs/samples/bookworm),
[Domain Data Contexts](https://traxsharp.net/docs/effect/effect-providers/domain-data-contexts) and
[Architecture Guards](https://traxsharp.net/docs/reference/architecture-guards).
