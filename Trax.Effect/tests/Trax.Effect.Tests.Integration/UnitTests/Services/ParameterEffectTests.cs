using System.Text.Json;
using FluentAssertions;
using NUnit.Framework;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Models.Metadata.DTOs;
using Trax.Effect.Provider.Parameter.Configuration;
using Trax.Effect.Provider.Parameter.Services.ParameterEffectProviderFactory;

namespace Trax.Effect.Tests.Integration.UnitTests.Services;

[TestFixture]
public class ParameterEffectTests
{
    private static Metadata NewMetadata(object? input = null, object? output = null)
    {
        var meta = Metadata.Create(
            new CreateMetadata
            {
                Name = "Trax.X.Train",
                ExternalId = Guid.NewGuid().ToString("N"),
                Input = input,
            }
        );
        if (output is not null)
            meta.SetOutputObject(output);
        return meta;
    }

    private static ParameterEffect NewEffect(bool saveInputs = true, bool saveOutputs = true)
    {
        var config = new ParameterEffectConfiguration
        {
            SaveInputs = saveInputs,
            SaveOutputs = saveOutputs,
        };
        return new ParameterEffect(new JsonSerializerOptions(), config);
    }

    [Test]
    public async Task Track_MetadataModel_AddsToTrackedAndSerializesInput()
    {
        var effect = NewEffect();
        var meta = NewMetadata(input: new { Foo = 1, Bar = "x" });

        await effect.Track(meta);

        meta.Input.Should().Contain("Foo").And.Contain("Bar");
    }

    [Test]
    public async Task Track_NonMetadataModel_NoOp()
    {
        var effect = NewEffect();
        var manifest = Trax.Effect.Models.Manifest.Manifest.Create(
            new Trax.Effect.Models.Manifest.DTOs.CreateManifest { Name = typeof(string) }
        );

        Func<Task> act = () => effect.Track(manifest);

        await act.Should().NotThrowAsync();
    }

    [Test]
    public async Task Update_TrackedMetadata_ReSerializes()
    {
        var effect = NewEffect();
        var meta = NewMetadata(input: new { V = 1 });
        await effect.Track(meta);

        meta.SetInputObject(new { V = 99 });
        await effect.Update(meta);

        meta.Input.Should().Contain("99");
    }

    [Test]
    public async Task Update_UntrackedMetadata_DoesNotSerialize()
    {
        var effect = NewEffect();
        var meta = NewMetadata(input: new { V = 1 });
        // First serialization happens during Create via SetInputObject (not via Track)
        var beforeInput = meta.Input;

        await effect.Update(meta);

        meta.Input.Should().Be(beforeInput);
    }

    [Test]
    public async Task SaveChanges_ReSerializesAllTracked()
    {
        var effect = NewEffect();
        var m1 = NewMetadata(input: new { A = 1 });
        var m2 = NewMetadata(input: new { B = 2 });
        await effect.Track(m1);
        await effect.Track(m2);

        m1.SetInputObject(new { A = 100 });
        m2.SetInputObject(new { B = 200 });
        await effect.SaveChanges(default);

        m1.Input.Should().Contain("100");
        m2.Input.Should().Contain("200");
    }

    [Test]
    public async Task Track_OutputObject_SerializesIntoOutput()
    {
        var effect = NewEffect();
        var meta = NewMetadata(input: new { A = 1 }, output: new { Result = "done" });

        await effect.Track(meta);

        meta.Output.Should().Contain("Result").And.Contain("done");
    }

    [Test]
    public async Task Track_SaveInputsDisabled_DoesNotSerializeInput()
    {
        var effect = NewEffect(saveInputs: false, saveOutputs: true);
        var meta = NewMetadata(input: new { Hidden = "secret" });
        meta.Input = null; // wipe the auto-serialization from CreateMetadata

        await effect.Track(meta);

        meta.Input.Should().BeNull();
    }

    [Test]
    public async Task Track_SaveOutputsDisabled_DoesNotSerializeOutput()
    {
        var effect = NewEffect(saveInputs: true, saveOutputs: false);
        var meta = NewMetadata(input: null, output: new { Hidden = "secret" });

        await effect.Track(meta);

        meta.Output.Should().BeNull();
    }

    [Test]
    public void Dispose_ClearsTrackedAndDetachesObjects()
    {
        var effect = NewEffect();
        var meta = NewMetadata(input: new { V = 1 });
        effect.Track(meta).GetAwaiter().GetResult();

        effect.Dispose();

        // After dispose, calling SaveChanges should be a no-op (no tracked items)
        Func<Task> act = () => effect.SaveChanges(default);
        act.Should().NotThrowAsync();
    }
}
