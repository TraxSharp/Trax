using FluentAssertions;
using Trax.Api.GraphQL.PersistedOperations.Broadcasting;

namespace Trax.Api.Tests.PersistedOperations.UnitTests;

[TestFixture]
public class PersistedOperationChangedMessageTests
{
    [Test]
    public void RecordEquality_SameValues_AreEqual()
    {
        var ts = DateTime.UtcNow;
        var a = new PersistedOperationChangedMessage(
            "tenant",
            "id",
            PersistedOperationChangeType.Upsert,
            ts
        );
        var b = new PersistedOperationChangedMessage(
            "tenant",
            "id",
            PersistedOperationChangeType.Upsert,
            ts
        );
        a.Should().Be(b);
        a.GetHashCode().Should().Be(b.GetHashCode());
    }

    [Test]
    public void RecordEquality_DifferentChangeType_AreNotEqual()
    {
        var ts = DateTime.UtcNow;
        var a = new PersistedOperationChangedMessage(
            "tenant",
            "id",
            PersistedOperationChangeType.Upsert,
            ts
        );
        var b = new PersistedOperationChangedMessage(
            "tenant",
            "id",
            PersistedOperationChangeType.Deactivate,
            ts
        );
        a.Should().NotBe(b);
    }

    [Test]
    public void RecordEquality_DifferentTenantKey_AreNotEqual()
    {
        var ts = DateTime.UtcNow;
        var a = new PersistedOperationChangedMessage(
            "tenant-a",
            "id",
            PersistedOperationChangeType.Upsert,
            ts
        );
        var b = new PersistedOperationChangedMessage(
            "tenant-b",
            "id",
            PersistedOperationChangeType.Upsert,
            ts
        );
        a.Should().NotBe(b);
    }

    [Test]
    public void With_RecordCopy_OverridesFieldAndPreservesOthers()
    {
        var ts = DateTime.UtcNow;
        var a = new PersistedOperationChangedMessage(
            "t",
            "id",
            PersistedOperationChangeType.Upsert,
            ts
        );
        var b = a with { ChangeType = PersistedOperationChangeType.Restore };
        b.Id.Should().Be("id");
        b.ChangeType.Should().Be(PersistedOperationChangeType.Restore);
        b.TenantKey.Should().Be("t");
        b.Timestamp.Should().Be(ts);
        a.ChangeType.Should().Be(PersistedOperationChangeType.Upsert);
    }

    [Test]
    public void ToString_IncludesAllFields()
    {
        var msg = new PersistedOperationChangedMessage(
            "tenant",
            "userProfile_v1",
            PersistedOperationChangeType.Upsert,
            new DateTime(2026, 5, 8, 12, 0, 0, DateTimeKind.Utc)
        );
        var s = msg.ToString();
        s.Should().Contain("tenant");
        s.Should().Contain("userProfile_v1");
        s.Should().Contain("Upsert");
    }

    [Test]
    public void NullableTenantKey_RoundTripsThroughRecord()
    {
        var msg = new PersistedOperationChangedMessage(
            null,
            "id",
            PersistedOperationChangeType.Upsert,
            DateTime.UtcNow
        );
        msg.TenantKey.Should().BeNull();

        var copy = msg with { TenantKey = "now-set" };
        copy.TenantKey.Should().Be("now-set");
        msg.TenantKey.Should().BeNull();
    }
}
