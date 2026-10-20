using AwesomeAssertions;
using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Core.Train;

namespace Trax.Core.Tests.Integration.Examples.Memory;

// Shown on https://traxsharp.net/docs/core/memory
//
// Memory keeps one value per type. Both junctions return a string, so the second one replaces
// the first, and the train returns the last name only.

public sealed record Person(string FirstName, string LastName);

public class GetFirstName : Junction<Person, string>
{
    public override Task<string> Run(Person person) => Task.FromResult(person.FirstName);
}

public class GetLastName : Junction<Person, string>
{
    public override Task<string> Run(Person person) => Task.FromResult(person.LastName);
}

public class NameTrain : Train<Person, string>
{
    protected override Task<Either<Exception, string>> Junctions() =>
        Chain<GetFirstName>().Chain<GetLastName>().Resolve();
}

public class OneValuePerTypeTests
{
    [Test]
    public async Task Run_TwoJunctionsReturnAString_ReturnsTheSecond()
    {
        var name = await new NameTrain().Run(new Person("Ada", "Lovelace"));

        name.Should().Be("Lovelace");
    }
}
