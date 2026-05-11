using FluentAssertions;
using Trax.Api.GraphQL.PersistedOperations.Storage.Validation;

namespace Trax.Api.Tests.PersistedOperations.UnitTests.Validation;

[TestFixture]
public class NoOpPersistedOperationValidatorTests
{
    [TestCase("")]
    [TestCase("   ")]
    [TestCase("not a graphql document")]
    [TestCase("query Q { foo }")]
    [TestCase("{ malformed")]
    public async Task ValidateAsync_AnyInput_DoesNotThrow(string document)
    {
        var validator = new NoOpPersistedOperationValidator();

        Func<Task> act = () => validator.ValidateAsync(document, CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    [Test]
    public async Task ValidateAsync_PreCancelledToken_ReturnsCancelled()
    {
        var validator = new NoOpPersistedOperationValidator();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Func<Task> act = () => validator.ValidateAsync("query Q { x }", cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
