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
    private static Metadata NewMetadata(
        object? input = null,
        object? output = null,
        string name = "Trax.X.Train"
    )
    {
        var meta = Metadata.Create(
            new CreateMetadata
            {
                Name = name,
                ExternalId = Guid.NewGuid().ToString("N"),
                Input = input,
            }
        );
        if (output is not null)
            meta.SetOutputObject(output);
        return meta;
    }

    private static ParameterEffect NewEffect(
        bool saveInputs = true,
        bool saveOutputs = true,
        int? maxParameterBytes = null
    )
    {
        var config = new ParameterEffectConfiguration
        {
            SaveInputs = saveInputs,
            SaveOutputs = saveOutputs,
            MaxParameterBytes = maxParameterBytes,
        };
        return new ParameterEffect(new JsonSerializerOptions(), config);
    }

    private static ParameterEffect NewEffect(ParameterEffectConfiguration config) =>
        new(new JsonSerializerOptions(), config);

    // A train whose FullName we can match against for type-based exclusion tests.
    private sealed class FakeQuery { }

    // An output whose serialization never terminates. Serializing this without a byte ceiling
    // exhausts memory; with a ceiling it must abort in milliseconds. This is the deterministic
    // stand-in for "would have OOMed the host".
    private sealed class UnboundedOutput
    {
        public IEnumerable<int> Items { get; } = Count();

        private static IEnumerable<int> Count()
        {
            for (var i = 0; ; i++)
                yield return i;
        }
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
    public async Task Track_InputContainsDisposedJsonDocument_FallsBackToPlaceholderJson()
    {
        var effect = NewEffect();
        var doc = JsonDocument.Parse("{\"x\":1}");
        doc.Dispose();
        var meta = NewMetadata();
        meta.Input = null;
        meta.SetInputObject(doc);

        await effect.Track(meta);

        meta.Input.Should().Contain("_disposed");
    }

    [Test]
    public async Task Track_OutputContainsDisposedJsonDocument_FallsBackToPlaceholderJson()
    {
        var effect = NewEffect();
        var doc = JsonDocument.Parse("{\"x\":1}");
        doc.Dispose();
        var meta = NewMetadata();
        meta.Output = null;
        meta.SetOutputObject(doc);

        await effect.Track(meta);

        meta.Output.Should().Contain("_disposed");
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

    #region Per-train output opt-out (Feature A)

    [Test]
    public async Task Track_ShouldSaveOutputsFalse_SkipsOutput_KeepsInput()
    {
        var config = new ParameterEffectConfiguration { ShouldSaveOutputs = _ => false };
        var effect = NewEffect(config);
        var meta = NewMetadata(input: new { Keep = 1 }, output: new { Drop = 2 });

        await effect.Track(meta);

        meta.Output.Should().BeNull("output serialization was opted out");
        meta.Input.Should().Contain("Keep", "inputs are unaffected by the output opt-out");
    }

    [Test]
    public async Task Track_ShouldSaveOutputs_ReceivesCanonicalName()
    {
        string? seen = null;
        var config = new ParameterEffectConfiguration
        {
            ShouldSaveOutputs = name =>
            {
                seen = name;
                return true;
            },
        };
        var effect = NewEffect(config);
        var meta = NewMetadata(output: new { X = 1 }, name: "My.Train.Name");

        await effect.Track(meta);

        seen.Should().Be("My.Train.Name");
        meta.Output.Should().Contain("X", "returning true still serializes the output");
    }

    [Test]
    public async Task Track_ExcludeOutputByType_SkipsMatchingOutput_KeepsInput()
    {
        // Mirrors how a generically-dispatched train is named: the type FullName embedded in an
        // assembly-qualified canonical name.
        var name =
            $"{typeof(FakeQuery).FullName}, Trax.Effect.Tests, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null";
        var config = new ParameterEffectConfiguration().ExcludeOutput<FakeQuery>();
        var effect = NewEffect(config);
        var meta = NewMetadata(input: new { Keep = 1 }, output: new { Huge = "x" }, name: name);

        await effect.Track(meta);

        meta.Output.Should().BeNull("output for the excluded train type must be skipped");
        meta.Input.Should().Contain("Keep");
    }

    [Test]
    public async Task Track_ExcludeOutputByTypeInstance_SkipsMatchingOutput()
    {
        var name = $"{typeof(FakeQuery).FullName}, Trax.Effect.Tests";
        var config = new ParameterEffectConfiguration().ExcludeOutput(typeof(FakeQuery));
        var effect = NewEffect(config);
        var meta = NewMetadata(output: new { Huge = "x" }, name: name);

        await effect.Track(meta);

        meta.Output.Should().BeNull();
    }

    [Test]
    public async Task Track_ExcludeOutputByString_SkipsMatchingOutput()
    {
        var config = new ParameterEffectConfiguration().ExcludeOutput("GetEntitiesQuery");
        var effect = NewEffect(config);
        var meta = NewMetadata(
            output: new { Huge = "x" },
            name: "NSync.Handlers.GetEntitiesQueryHandler+GetEntitiesQuery, NSync"
        );

        await effect.Track(meta);

        meta.Output.Should().BeNull();
    }

    [Test]
    public async Task Track_ExcludeOutput_NonMatchingTrain_StillSavesOutput()
    {
        var config = new ParameterEffectConfiguration().ExcludeOutput("SomeOtherTrain");
        var effect = NewEffect(config);
        var meta = NewMetadata(output: new { Result = "ok" }, name: "Trax.X.Train");

        await effect.Track(meta);

        meta.Output.Should().Contain("Result", "a non-matching train keeps its output");
    }

    [Test]
    public async Task Track_NoExclusions_SavesOutput()
    {
        var effect = NewEffect();
        var meta = NewMetadata(output: new { Result = "ok" });

        await effect.Track(meta);

        meta.Output.Should().Contain("Result", "default config preserves current behavior");
    }

    [Test]
    public void ExcludeOutput_NullOrEmptyString_Throws()
    {
        var config = new ParameterEffectConfiguration();

        config.Invoking(c => c.ExcludeOutput((string)null!)).Should().Throw<ArgumentException>();
        config.Invoking(c => c.ExcludeOutput("")).Should().Throw<ArgumentException>();
    }

    [Test]
    public void ExcludeOutput_TypeWithoutFullName_Throws()
    {
        // Open generic parameters have no FullName; excluding one is a usage error, and the
        // guard turns it into a clear ArgumentException instead of a later null store.
        var noFullName = typeof(List<>).GetGenericArguments()[0];
        noFullName.FullName.Should().BeNull();

        var config = new ParameterEffectConfiguration();

        config.Invoking(c => c.ExcludeOutput(noFullName)).Should().Throw<ArgumentException>();
    }

    [Test]
    public async Task Track_NullName_WithPredicate_PassesEmptyStringAndStillSaves()
    {
        // Metadata.Name is non-null in practice, but the opt-out logic guards against null so a
        // missing name never NREs: the exclusion scan is skipped and the predicate sees "".
        string? seen = "unset";
        var config = new ParameterEffectConfiguration
        {
            ShouldSaveOutputs = name =>
            {
                seen = name;
                return true;
            },
        };
        var effect = NewEffect(config);
        var meta = NewMetadata(output: new { X = 1 });
        meta.Name = null!;

        await effect.Track(meta);

        seen.Should().Be(string.Empty);
        meta.Output.Should().Contain("X");
    }

    #endregion

    #region Size ceiling (Feature B)

    [Test]
    public async Task Track_OutputUnderCap_SerializesFully()
    {
        var effect = NewEffect(maxParameterBytes: 1_048_576);
        var meta = NewMetadata(output: new { Result = "done", N = 42 });

        await effect.Track(meta);

        meta.Output.Should().Contain("Result").And.Contain("done").And.NotContain("_truncated");
    }

    [Test]
    public async Task Track_OutputOverCap_ReturnsTruncatedPlaceholder()
    {
        const int cap = 1024;
        var effect = NewEffect(maxParameterBytes: cap);
        var meta = NewMetadata(output: new { Blob = new string('x', 50_000) });

        await effect.Track(meta);

        meta.Output.Should().Contain("_truncated").And.Contain("\"_maxBytes\": 1024");
        // The stored value is the small placeholder, not the multi-KB payload.
        meta.Output!.Length.Should().BeLessThan(cap);
    }

    [Test]
    public async Task Track_InputOverCap_ReturnsTruncatedPlaceholder()
    {
        const int cap = 1024;
        var effect = NewEffect(maxParameterBytes: cap);
        var meta = NewMetadata(input: new { Blob = new string('y', 50_000) });
        meta.Input = null;

        await effect.Track(meta);

        meta.Input.Should().Contain("_truncated");
    }

    [Test]
    public async Task Track_NullCap_ByteIdenticalToStringOverload()
    {
        var options = new JsonSerializerOptions();
        var payload = new
        {
            A = 1,
            B = "hello",
            C = new[] { 1, 2, 3 },
        };
        var expected = JsonSerializer.Serialize(payload, payload.GetType(), options);
        var effect = new ParameterEffect(options, new ParameterEffectConfiguration());
        var meta = NewMetadata(output: payload);

        await effect.Track(meta);

        meta.Output.Should().Be(expected);
    }

    [Test]
    public async Task Track_ValidJsonPlaceholder_OnOverflow()
    {
        var effect = NewEffect(maxParameterBytes: 512);
        var meta = NewMetadata(output: new { Blob = new string('z', 10_000) });

        await effect.Track(meta);

        // The placeholder must be valid JSON so it can land in a jsonb column.
        using var doc = JsonDocument.Parse(meta.Output!);
        doc.RootElement.GetProperty("_truncated").GetBoolean().Should().BeTrue();
        doc.RootElement.GetProperty("_maxBytes").GetInt32().Should().Be(512);
    }

    #endregion

    #region Size ceiling stress (Feature B)

    [Test]
    public async Task Track_UnboundedOutput_AbortsFast_ReturnsPlaceholder()
    {
        // Without the ceiling this serialization never terminates and exhausts memory. The ceiling
        // must abort it quickly and store the placeholder. The work runs on a background task and
        // WaitAsync throws if it does not complete within a generous window: the deterministic
        // stand-in for "would have OOMed the host". It returns the instant the ceiling trips (~ms);
        // the timeout only fires, and fails the test, if a regression lets serialization run away.
        var effect = NewEffect(maxParameterBytes: 64 * 1024);
        var meta = NewMetadata(output: new UnboundedOutput());

        var track = Task.Run(async () =>
        {
            await effect.Track(meta);
            return meta;
        });
        var result = await track.WaitAsync(TimeSpan.FromSeconds(30));

        result.Output.Should().Contain("_truncated");
    }

    [Test]
    public async Task Track_ManyConcurrentUnboundedOutputs_AllBounded()
    {
        // Reproduces the failure shape: a fan-out of trains each producing an effectively unbounded
        // output, serialized concurrently. Each effect instance is independent (one per train scope
        // in production). With the ceiling every one aborts and completes; without it the process
        // would OOM. WaitAsync throws if the fan-out does not finish within the window, so a
        // regression fails loudly instead of hanging.
        const int parallelism = 12;
        const int cap = 64 * 1024;

        var work = Enumerable
            .Range(0, parallelism)
            .Select(_ =>
            {
                var effect = NewEffect(maxParameterBytes: cap);
                var meta = NewMetadata(output: new UnboundedOutput());
                return Task.Run(async () =>
                {
                    await effect.Track(meta);
                    return meta;
                });
            })
            .ToArray();

        var results = await Task.WhenAll(work).WaitAsync(TimeSpan.FromSeconds(45));

        results.Should().OnlyContain(m => m.Output!.Contains("_truncated"));
    }

    #endregion
}
