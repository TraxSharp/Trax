using Trax.Scheduler.Services.Operations;

namespace Trax.Api.DTOs;

/// <summary>An arrow from one sticky's id to another's.</summary>
public record MachineBoardLink(string From, string To);
