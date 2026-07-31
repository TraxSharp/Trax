namespace Trax.Effect.StateMachine.Tests.Helpers;

/// <summary>
/// Locates the shared, language-neutral fixtures that live in the sibling <c>Trax.Api.StateMachine</c>
/// repo (the source of truth both engines drive). In a full workspace checkout it walks up to the
/// workspace root and into <c>Trax.Api.StateMachine/machines</c>; in an isolated build the fixtures are
/// absent and the conformance tests skip (in CI they are supplied by the fixtures package instead).
/// </summary>
public static class FixturePaths
{
    private static readonly Lazy<string?> Root = new(Find);

    /// <summary>The shared <c>machines/</c> directory, or <c>null</c> if this is an isolated build.</summary>
    public static string? MachinesRoot => Root.Value;

    public static string? AdvanceDir(string machine) =>
        MachinesRoot is null ? null : Path.Combine(MachinesRoot, machine, "fixtures", "advance");

    public static string? RehydrateDir(string machine) =>
        MachinesRoot is null ? null : Path.Combine(MachinesRoot, machine, "fixtures", "rehydrate");

    private static string? Find()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "Trax.Api.StateMachine", "machines");
            if (Directory.Exists(Path.Combine(candidate, "turnstile", "fixtures")))
                return candidate;
            dir = dir.Parent;
        }

        return null;
    }
}
