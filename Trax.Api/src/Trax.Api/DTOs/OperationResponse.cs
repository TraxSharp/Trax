namespace Trax.Api.DTOs;

public record OperationResponse(bool Success, int? Count = null, string? Message = null);
