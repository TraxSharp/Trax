using Trax.Core.Step;
using Trax.Effect.Tests.Benchmarks.Models;

namespace Trax.Effect.Tests.Benchmarks.Steps;

public class TransformStep : Step<PersonDto, PersonEntity>
{
    public override Task<PersonEntity> Run(PersonDto input) =>
        Task.FromResult(
            new PersonEntity(
                FullName: $"{input.FirstName} {input.LastName}",
                Age: input.Age,
                ContactEmail: input.Email,
                IsAdult: input.Age >= 18
            )
        );
}
