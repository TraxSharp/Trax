using Microsoft.Extensions.DependencyInjection;
using Trax.Samples.Bookworm.Catalog.Context;
using Trax.Samples.Bookworm.Catalog.Models.Books;
using Trax.Samples.Bookworm.E2E.Utilities;

namespace Trax.Samples.Bookworm.E2E.Fixtures;

/// <summary>Base for HTTP-level Bookworm tests: a fresh GraphQL client over the shared host.</summary>
[TestFixture]
public abstract class ApiTestFixture
{
    protected GraphQLClient GraphQL { get; private set; } = null!;

    [SetUp]
    public void SetUp() => GraphQL = new GraphQLClient(SharedBookwormSetup.Factory.CreateClient());

    /// <summary>
    /// Adds a book to the catalog and returns its id, so a test that borrows owns a book no other
    /// test has on loan. The seeded author 1 writes it.
    /// </summary>
    protected static async Task<int> NewBookAsync()
    {
        using var scope = SharedBookwormSetup.Factory.Services.CreateScope();
        var catalog = scope.ServiceProvider.GetRequiredService<ICatalogDbContext>();
        var book = new Book
        {
            Title = $"Test Book {Guid.NewGuid():N}",
            Isbn = Guid.NewGuid().ToString("N"),
            AuthorId = 1,
        };
        catalog.Books.Add(book);
        await catalog.SaveChangesAsync();
        return book.Id;
    }

    protected static string Borrow(int bookId) =>
        $"mutation {{ dispatch {{ lending {{ borrowBook(input: {{ bookId: {bookId} }}) "
        + "{ output { loanId } } } } }";

    /// <summary>Borrows <paramref name="bookId"/> as <paramref name="apiKey"/> and returns the loan id.</summary>
    protected async Task<int> BorrowAsync(int bookId, string apiKey)
    {
        var doc = await GraphQL.PostAsync(Borrow(bookId), apiKey);
        GraphQLClient.HasErrors(doc).Should().BeFalse(doc.RootElement.GetRawText());
        return doc
            .RootElement.GetProperty("data")
            .GetProperty("dispatch")
            .GetProperty("lending")
            .GetProperty("borrowBook")
            .GetProperty("output")
            .GetProperty("loanId")
            .GetInt32();
    }
}
