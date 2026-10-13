---
title: Unit-test a junction
description: "Set up an NUnit test project for a Trax application and test a junction by constructing it with its dependencies, or with fakes, and calling Run."
type: how-to
area: testing
nav_order: 1
---

# Unit-test a junction

A junction is a class with a `Run` method, so a unit test constructs it with its dependencies and
calls `Run`: no container, no data provider, no host. To test the train the junction sits in, see
[Test a train with in-memory effects](/docs/cross-cutting/test-a-train); to test the running
application, [Test a host end to end](/docs/cross-cutting/e2e-testing).

## Set up the test project

The test project needs NUnit, its adapter and the test SDK, plus a `ProjectReference` to the
application. Every [project template](/docs/reference/templates) ships one under `tests/`, with
versions pinned in the template's `Directory.Packages.props`:

```xml from=Trax.Samples/templates/content/Trax.Samples.Hub/tests/Trax.Samples.Hub.Tests/Trax.Samples.Hub.Tests.csproj
  <ItemGroup>
    <ProjectReference Include="..\..\Trax.Samples.Hub.csproj" />
  </ItemGroup>
<!-- ... -->
  <ItemGroup>
    <PackageReference Include="Microsoft.AspNetCore.Mvc.Testing" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="NUnit" />
    <PackageReference Include="NUnit.Analyzers" />
    <PackageReference Include="NUnit3TestAdapter" />
    <PackageReference Include="Microsoft.Testing.Extensions.VSTestBridge" />
    <PackageReference Include="Microsoft.Testing.Platform.MSBuild" />
  </ItemGroup>
```

`Microsoft.AspNetCore.Mvc.Testing` is only for the host tests. The template tests assert with
NUnit's `Assert.That`, which needs no other package, and the examples here do the same. Any
assertion library works; Trax's own test projects use AwesomeAssertions.

## Construct the junction and call Run

Pass the junction what its constructor asks for and await `Run` with an input. A junction that
takes only a logger gets `NullLogger<T>.Instance`:

```csharp from=Trax.Samples/templates/content/Trax.Samples.Hub/tests/Trax.Samples.Hub.Tests/UnitTests/FetchDataJunctionTests.cs
[TestFixture]
public class FetchDataJunctionTests
{
    [Test]
    public async Task Run_WithAnId_ReturnsTheRecordForThatId()
    {
        var junction = new FetchDataJunction(NullLogger<FetchDataJunction>.Instance);

        var output = await junction.Run(new LookupInput { Id = "42" });

        Assert.That(output.Id, Is.EqualTo("42"), "the lookup returns the record it was asked for");
    }
}
```

## Replace dependencies with fakes

When the junction depends on a repository or a client, write a fake of its interface that keeps
state in memory, so the test can seed it and read it back:

```csharp compile
public class FakeUserRepository : IUserRepository
{
    private readonly List<User> _users = [];
    private int _nextId = 1;

    public Task<User?> GetByEmailAsync(string email) =>
        Task.FromResult(_users.FirstOrDefault(u => u.Email == email));

    public Task<User> CreateAsync(User user)
    {
        user = user with { Id = _nextId++ };
        _users.Add(user);
        return Task.FromResult(user);
    }

    public void AddExisting(User user) => _users.Add(user);
}
```

Then test what the junction returns, and what it throws:

```csharp fragment
[Test]
public async Task CreateUserJunction_ReturnsTheNewUser()
{
    var junction = new CreateUserJunction(new FakeUserRepository());

    var user = await junction.Run(new CreateUserRequest { Email = "new@example.com" });

    Assert.That(user.Id, Is.EqualTo(1), "the first user gets id 1");
    Assert.That(user.Email, Is.EqualTo("new@example.com"));
}

[Test]
public void ValidateEmailJunction_RefusesAnEmailInUse()
{
    var repository = new FakeUserRepository();
    repository.AddExisting(new User { Id = 1, Email = "taken@example.com" });
    var junction = new ValidateEmailJunction(repository);

    Assert.ThrowsAsync<ValidationException>(
        () => junction.Run(new CreateUserRequest { Email = "taken@example.com" })
    );
}
```

A junction that throws is how a train switches to the failure track, so asserting the exception
is asserting the failure the train reports.

## Test cancellation

A junction reads its token from its `CancellationToken` property, which the train sets before
`Run`. To test that a junction stops when it is cancelled, see
[Make a junction cancellable](/docs/cross-cutting/make-a-junction-cancellable#5-test-it).

## SDK Reference

> [Junction](/docs/sdk-reference/trains-and-junctions/junction) | [EffectJunction](/docs/sdk-reference/trains-and-junctions/effect-junction)
