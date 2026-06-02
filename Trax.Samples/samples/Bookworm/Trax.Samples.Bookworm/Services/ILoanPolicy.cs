namespace Trax.Samples.Bookworm.Services;

/// <summary>
/// Lending business rules kept out of the junctions so they can be unit-tested in isolation and
/// swapped via DI. An ancillary service like this lives in the <c>Services/</c> folder by convention.
/// </summary>
public interface ILoanPolicy
{
    /// <summary>The date a book borrowed at <paramref name="borrowedAt"/> is due back.</summary>
    DateTime DueDate(DateTime borrowedAt);
}
