using Trax.Core.Junction;
using Trax.Effect.Tests.Benchmarks.Models;

namespace Trax.Effect.Tests.Benchmarks.Junctions;

public class TransformJunction : Junction<PersonDto, PersonEntity>
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
