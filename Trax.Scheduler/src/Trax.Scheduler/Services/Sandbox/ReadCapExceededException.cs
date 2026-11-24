namespace Trax.Scheduler.Services.Sandbox;

/// <summary>Thrown by a <see cref="CappedReadStream"/> read that takes the total past its cap.</summary>
internal sealed class ReadCapExceededException : IOException;
