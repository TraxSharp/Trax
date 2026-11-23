namespace Trax.Api.DTOs;

/// <summary>One id in a batch trigger, and what happened to it that the caller should know.</summary>
/// <param name="Id">The id as given: a manifest id, or a group id.</param>
/// <param name="Message">What happened to it.</param>
public record BatchTriggerNote(long Id, string Message);
