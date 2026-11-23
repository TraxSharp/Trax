namespace Trax.Samples.Bookworm.Lending.Context;

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
