using System.Text.Json;
using FluentAssertions;
using Trax.Core.Exceptions;
using Trax.Effect.Models.JunctionMetadata;
using Trax.Effect.Models.JunctionMetadata.DTOs;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Models.Manifest.DTOs;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Models.Metadata.DTOs;

namespace Trax.Effect.Tests.Integration.UnitTests.Models;

[TestFixture]
public class ModelCoverageGapTests
{
    #region Manifest

    [Test]
    public void Manifest_Create_NameWithNullFullName_Throws()
    {
        // Generic type parameters expose null FullName.
        var genericParam = typeof(GenericMarker<>).GetGenericArguments()[0];
        genericParam.FullName.Should().BeNull();

        var act = () => Manifest.Create(new CreateManifest { Name = genericParam });

        act.Should().Throw<Exception>().WithMessage("*full name*");
    }

    private class GenericMarker<T> { }

    [Test]
    public void Manifest_NameType_ResolvesTypeFromOtherAssembly()
    {
        // Type.GetType only finds types in mscorlib + executing assembly. Forcing
        // resolution via FullName routes through the loaded-assembly fallback path.
        var m = new Manifest { Name = typeof(Manifest).FullName! };

        m.NameType.Should().Be(typeof(Manifest));
    }

    [Test]
    public void Manifest_NameType_UnresolvableFullName_Throws()
    {
        var m = new Manifest { Name = "Definitely.Not.A.Real.Type, Ghost" };

        Action act = () => _ = m.NameType;

        act.Should().Throw<TypeLoadException>();
    }

    #endregion

    #region Metadata.AddException

    [Test]
    public void Metadata_AddException_MessageDeserializableAsTrainExceptionData_PopulatesFromJson()
    {
        var meta = NewMetadata();
        var data = new TrainExceptionData
        {
            TrainName = "TName",
            TrainExternalId = "ext",
            Type = "InvalidOperationException",
            Junction = "MyJunction",
            Message = "boom",
            StackTrace = "stack",
        };
        var json = JsonSerializer.Serialize(data);

        meta.AddException(new Exception(json));

        meta.FailureException.Should().Be("InvalidOperationException");
        meta.FailureReason.Should().Be("boom");
        meta.FailureJunction.Should().Be("MyJunction");
        meta.StackTrace.Should().Be("stack");
    }

    [Test]
    public void Metadata_AddException_DeserializedNoStackTrace_FallsBackToExceptionStackTrace()
    {
        var meta = NewMetadata();
        // Serialize with StackTrace omitted (null) so the deserialized object has null StackTrace.
        var data = new TrainExceptionData
        {
            TrainName = "TName",
            TrainExternalId = "ext",
            Type = "Boom",
            Junction = "Jx",
            Message = "msg",
            StackTrace = null,
        };
        var json = JsonSerializer.Serialize(data);
        Exception ex;
        try
        {
            throw new Exception(json);
        }
        catch (Exception e)
        {
            ex = e;
        }

        meta.AddException(ex);

        meta.FailureException.Should().Be("Boom");
        meta.StackTrace.Should().NotBeNullOrEmpty();
    }

    private static Metadata NewMetadata() =>
        Metadata.Create(
            new CreateMetadata
            {
                Name = "X",
                ExternalId = Guid.NewGuid().ToString("N"),
                Input = null,
            }
        );

    #endregion

    #region JunctionMetadata

    [Test]
    public void JunctionMetadata_Create_AssignsTrainExternalIdAndName()
    {
        var meta = NewMetadata();
        var jm = JunctionMetadata.Create(
            new CreateJunctionMetadata
            {
                Name = "Jx",
                ExternalId = Guid.NewGuid().ToString("N"),
                StartTimeUtc = DateTime.UtcNow,
                InputType = typeof(int),
                OutputType = typeof(string),
                State = LanguageExt.EitherStatus.IsRight,
            },
            meta
        );

        jm.Id.Should().Be(0);
        jm.TrainName.Should().Be(meta.Name);
        jm.TrainExternalId.Should().Be(meta.ExternalId);
        jm.HasRan.Should().BeFalse();
    }

    [Test]
    public void JunctionMetadata_ToString_ReturnsJson()
    {
        var meta = NewMetadata();
        var jm = JunctionMetadata.Create(
            new CreateJunctionMetadata
            {
                Name = "Jx",
                ExternalId = Guid.NewGuid().ToString("N"),
                StartTimeUtc = DateTime.UtcNow,
                InputType = typeof(int),
                OutputType = typeof(string),
                State = LanguageExt.EitherStatus.IsRight,
            },
            meta
        );

        var s = jm.ToString();

        s.Should().Contain("Jx");
    }

    #endregion
}
