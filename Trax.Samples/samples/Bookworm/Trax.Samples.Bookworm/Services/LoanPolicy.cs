namespace Trax.Samples.Bookworm.Services;

/// <summary>Default lending policy: a standard three-week loan period.</summary>
public sealed class LoanPolicy : ILoanPolicy
{
    public const int LoanDays = 21;

    public DateTime DueDate(DateTime borrowedAt) => borrowedAt.AddDays(LoanDays);
}
