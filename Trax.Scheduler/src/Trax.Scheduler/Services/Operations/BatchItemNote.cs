namespace Trax.Scheduler.Services.Operations;

/// <summary>One id in a batch, and what happened to it that the caller should know.</summary>
/// <param name="Id">The id as the caller gave it: a manifest id or a group id.</param>
/// <param name="Message">What happened to it.</param>
public record BatchItemNote(long Id, string Message);
