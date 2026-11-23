using Trax.Core.Train;

namespace Trax.Core.Monad;

/// <summary>
/// The branch running on this async flow: the token its junctions honour and the scope its
/// services come from. Unset outside a branch.
/// </summary>
internal static class RunningBranch
{
    private static readonly AsyncLocal<(
        object Train,
        CancellationToken Token,
        IServiceProvider? Services
    )?> Current = new();

    /// <summary>
    /// The branch's token for a junction of <paramref name="train"/>, or null outside a branch of
    /// it. A train run inside a branch's junction keeps its own token.
    /// </summary>
    public static CancellationToken? TokenFor(object train) =>
        Current.Value is { } branch && ReferenceEquals(branch.Train, train) ? branch.Token : null;

    /// <summary>
    /// The branch's own scope for <paramref name="train"/>, or null outside a branch of it or
    /// for a run with no container. A scoped service is not made to be used from two threads, so
    /// what a branch's steps resolve comes from here rather than from the run's scope.
    /// </summary>
    public static IServiceProvider? ServicesFor(object train) =>
        Current.Value is { } branch && ReferenceEquals(branch.Train, train)
            ? branch.Services
            : null;

    public static void Enter(object train, CancellationToken token, IServiceProvider? services) =>
        Current.Value = (train, token, services);
}
