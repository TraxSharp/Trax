using FluentAssertions;
using Trax.Api.GraphQL.PersistedOperations.Storage;
using Trax.Api.GraphQL.PersistedOperations.Storage.Exceptions;

namespace Trax.Api.Tests.PersistedOperations.UnitTests.Exceptions;

[TestFixture]
public class ShapeDiffViolationExceptionTests
{
    private const string OldFp = "abcdef1234567890abcdef1234567890abcdef1234567890abcdef1234567890";
    private const string NewFp = "fedcba0987654321fedcba0987654321fedcba0987654321fedcba0987654321";

    [Test]
    public void Ctor_PreservesFingerprintsAndId()
    {
        var ex = new ShapeDiffViolationException("op_v1", OldFp, NewFp);

        ex.Id.Should().Be("op_v1");
        ex.OldFingerprint.Should().Be(OldFp);
        ex.NewFingerprint.Should().Be(NewFp);
    }

    [Test]
    public void InheritsFromPersistedOperationException()
    {
        var ex = new ShapeDiffViolationException("op_v1", OldFp, NewFp);

        ex.Should().BeAssignableTo<PersistedOperationException>();
        ex.Code.Should().Be("SHAPE_DIFF_VIOLATION");
    }

    [Test]
    public void Message_IncludesIdAndAbbreviatedFingerprints()
    {
        var ex = new ShapeDiffViolationException("op_v1", OldFp, NewFp);

        ex.Message.Should().Contain("op_v1").And.Contain(OldFp[..8]).And.Contain(NewFp[..8]);
    }
}
