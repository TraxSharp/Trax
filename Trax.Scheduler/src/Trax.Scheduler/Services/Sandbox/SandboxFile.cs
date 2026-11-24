namespace Trax.Scheduler.Services.Sandbox;

/// <summary>A file a sandboxed train left in its output directory.</summary>
/// <param name="Path">Its path under the output directory, with <c>/</c> separators; never absolute, never with a <c>..</c> segment.</param>
/// <param name="Content">Its bytes.</param>
/// <seealso href="https://traxsharp.net/docs/sdk-reference/scheduler-api/add-trax-sandbox">SDK reference</seealso>
public sealed record SandboxFile(string Path, ReadOnlyMemory<byte> Content);
