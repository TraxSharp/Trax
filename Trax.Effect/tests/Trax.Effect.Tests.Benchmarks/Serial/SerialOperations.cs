using Trax.Effect.Tests.Benchmarks.Models;

namespace Trax.Effect.Tests.Benchmarks.Serial;

public static class SerialOperations
{
    public static Task<int> AddOneSerial(int input) => Task.FromResult(input + 1);

    public static Task<int> AddThreeSerial(int input) => Task.FromResult(input + 3);

    public static async Task<int> AddNSerial(int input, int steps)
    {
        var result = input;
        for (var i = 0; i < steps; i++)
            result += 1;
        return result;
    }

    public static Task<PersonEntity> TransformSerial(PersonDto input) =>
        Task.FromResult(
            new PersonEntity(
                FullName: $"{input.FirstName} {input.LastName}",
                Age: input.Age,
                ContactEmail: input.Email,
                IsAdult: input.Age >= 18
            )
        );

    public static async Task<int> SimulatedIoSerial(int input, int steps)
    {
        var result = input;
        for (var i = 0; i < steps; i++)
        {
            await Task.Yield();
            result += 1;
        }
        return result;
    }
}
