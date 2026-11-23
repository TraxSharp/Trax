namespace Trax.Scheduler.Services.Operations;

/// <summary>An arrow on the board, from one sticky's id to another's.</summary>
public sealed record MachineBoardLinkDescription(string From, string To);
