namespace Trax.Scheduler.Services.Sandbox;

/// <summary>What a sandboxed run gave back.</summary>
/// <typeparam name="TOutput">The train's output type.</typeparam>
/// <param name="Output">The train's output.</param>
/// <param name="Files">The files the train left in <see cref="SandboxWorkspace.FilesDirectory"/>, checked twice: by the launcher and by the hub.</param>
/// <seealso href="https://traxsharp.net/docs/sdk-reference/scheduler-api/add-trax-sandbox">SDK reference</seealso>
public sealed record SandboxRun<TOutput>(TOutput? Output, IReadOnlyList<SandboxFile> Files);
