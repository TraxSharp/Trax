namespace Trax.Mediator.Services.Principal;

/// <summary>
/// Default <see cref="ICurrentPrincipalProvider"/> that always returns <c>null</c>.
/// Used in hosts that do not run inside an HTTP request pipeline (scheduler,
/// remote worker). Replaced by <c>AddTraxApi</c> with an HttpContext-backed
/// implementation.
/// </summary>
internal sealed class NullPrincipalProvider : ICurrentPrincipalProvider
{
    public string? GetCurrentPrincipalId() => null;
}
