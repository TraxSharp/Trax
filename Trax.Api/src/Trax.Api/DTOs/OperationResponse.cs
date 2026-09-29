namespace Trax.Api.DTOs;

/// <summary>
/// The result of an operations mutation. A mutation that could not do what was asked (the target
/// does not exist, or is in the wrong state) returns <c>Success = false</c> with a
/// <see cref="Message"/> rather than a GraphQL error.
/// </summary>
/// <param name="Success">Whether the operation did what was asked.</param>
/// <param name="Count">How many items the operation affected, for operations that act on a set; <c>null</c> otherwise.</param>
/// <param name="Message">A human-readable description of the outcome.</param>
public record OperationResponse(bool Success, int? Count = null, string? Message = null);
