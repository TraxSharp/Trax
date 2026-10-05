namespace Trax.Samples.Bookworm.Lending.Context;

/// <summary>
/// Who is reading lending rows. <see cref="LendingDbContext"/>'s query filters read it, so a member
/// sees their own member row and loans, a librarian sees every row, and anyone else sees none.
/// </summary>
/// <remarks>
/// The lending domain does not know how callers are authenticated; the host binds this over its
/// auth (Bookworm binds it over Trax's <c>TraxCaller</c>). It is read when a query runs, not when
/// the context is built, so a principal authenticated later in the request still applies.
/// </remarks>
public interface ILendingCaller
{
    /// <summary>The caller's qualified principal id (<c>TraxApiKey:member</c>), or null when anonymous.</summary>
    string? PrincipalId { get; }

    /// <summary>True when the caller may read every member and loan.</summary>
    bool IsLibrarian { get; }
}

/// <summary>
/// The caller of a context built with no caller: nobody. Every owner-scoped row is filtered out,
/// so a context created outside a request (or by a guard reading the model) fails closed.
/// </summary>
public sealed class NoLendingCaller : ILendingCaller
{
    public static readonly NoLendingCaller Instance = new();

    public string? PrincipalId => null;

    public bool IsLibrarian => false;
}
