namespace Trax.Scheduler.Services.Sandbox;

/// <summary>
/// The directories a sandboxed train works in. Inject it into a junction to find where to write
/// the files the hub should get back.
/// </summary>
/// <remarks>
/// Registered by <c>AddTraxSandboxRunner</c>. Only <see cref="FilesDirectory"/> comes back to the
/// hub; the scratch directory and everything else is gone when the sandbox exits.
/// </remarks>
/// <param name="root">The sandbox root; <see cref="SandboxPaths.Root"/> in a real sandbox.</param>
/// <param name="scratch">The scratch directory; <see cref="SandboxPaths.Scratch"/> in a real sandbox.</param>
/// <seealso href="https://traxsharp.net/docs/sdk-reference/scheduler-api/add-trax-sandbox#inside-the-sandbox">SDK reference</seealso>
public sealed class SandboxWorkspace(string root, string scratch)
{
    /// <summary>The input directory, read-only inside a sandbox.</summary>
    public string InputDirectory { get; } = Path.Combine(root, SandboxPaths.Input);

    /// <summary>The output volume. The entry point writes the result here.</summary>
    public string OutputDirectory { get; } = Path.Combine(root, SandboxPaths.Output);

    /// <summary>Where the train writes the files the hub gets back.</summary>
    public string FilesDirectory { get; } =
        Path.Combine(root, SandboxPaths.Output, SandboxPaths.Files);

    /// <summary>A writable directory for work in progress, not handed back.</summary>
    public string ScratchDirectory { get; } = scratch;
}
