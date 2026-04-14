using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;
using Trax.Samples.JobHunt.Data.Entities;
using Trax.Samples.JobHunt.Tests.Fixtures;

namespace Trax.Samples.JobHunt.Tests.UnitTests;

[TestFixture]
public class JobHuntDbContextTests
{
    [Test]
    public async Task User_CanBePersistedAndQueried()
    {
        await using var db = JobHuntDbContextFixture.Create();

        var user = new User
        {
            Id = Guid.NewGuid(),
            ApiKey = "alice-key",
            DisplayName = "Alice",
            CreatedAt = DateTime.UtcNow,
        };

        db.Users.Add(user);
        await db.SaveChangesAsync();

        var loaded = await db.Users.SingleAsync(u => u.ApiKey == "alice-key");
        loaded.Id.Should().Be(user.Id);
        loaded.DisplayName.Should().Be("Alice");
    }

    [Test]
    public async Task User_QueryByApiKey_ReturnsMatchingUser()
    {
        await using var db = JobHuntDbContextFixture.Create();

        db.Users.AddRange(
            new User
            {
                Id = Guid.NewGuid(),
                ApiKey = "alice-key",
                DisplayName = "Alice",
                CreatedAt = DateTime.UtcNow,
            },
            new User
            {
                Id = Guid.NewGuid(),
                ApiKey = "bob-key",
                DisplayName = "Bob",
                CreatedAt = DateTime.UtcNow,
            }
        );
        await db.SaveChangesAsync();

        var bob = await db.Users.SingleAsync(u => u.ApiKey == "bob-key");
        bob.DisplayName.Should().Be("Bob");
    }

    [Test]
    public async Task Users_EmptyDatabase_ReturnsEmptyCollection()
    {
        await using var db = JobHuntDbContextFixture.Create();

        var users = await db.Users.ToListAsync();

        users.Should().BeEmpty();
    }
}
