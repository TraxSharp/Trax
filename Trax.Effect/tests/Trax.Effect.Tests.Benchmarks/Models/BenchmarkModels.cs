namespace Trax.Effect.Tests.Benchmarks.Models;

public record PersonDto(string FirstName, string LastName, int Age, string Email);

public record PersonEntity(string FullName, int Age, string ContactEmail, bool IsAdult);
