using System.Text.Json;
using FluentAssertions;
using Trax.Effect.Attributes;
using Trax.Effect.Models.BackgroundJob;
using Trax.Effect.Models.BackgroundJob.DTOs;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Models.Manifest.DTOs;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Models.Metadata.DTOs;
using Trax.Effect.Models.WorkQueue;
using Trax.Effect.Models.WorkQueue.DTOs;
using Trax.Effect.Utils;

namespace Trax.Effect.Tests.Integration.UnitTests.Utils;

/// <summary>
/// A model written to a log carries none of the unmasked copies of an input Trax keeps to run
/// from, and a metadata row's log line leaves out the graph loaded around it.
/// </summary>
[TestFixture]
public class TraxLogSerializationTests
{
    private const string Secret = "sk-live-hunter2";

    [Test]
    public void Manifest_ToString_writes_its_properties_as_omitted()
    {
        var manifest = Manifest.Create(
            new CreateManifest
            {
                Name = typeof(TraxLogSerializationTests),
                Properties = new Charge { ApiKey = Secret },
            }
        );

        var text = manifest.ToString();

        text.Should().NotContain(Secret).And.Contain("\"_omitted\": true");
        manifest.Properties.Should().Contain(Secret, "the column is what the manifest runs with");
    }

    [Test]
    public void WorkQueue_ToString_writes_its_input_as_omitted()
    {
        var entry = WorkQueue.Create(
            new CreateWorkQueue { TrainName = "T", Input = $$"""{"apiKey":"{{Secret}}"}""" }
        );

        entry
            .ToString()
            .Should()
            .NotContain(Secret)
            .And.Contain(TraxLogSerialization.OmittedProperty);
    }

    [Test]
    public void BackgroundJob_ToString_writes_its_input_as_omitted()
    {
        var job = BackgroundJob.Create(
            new CreateBackgroundJob { MetadataId = 1, Input = $$"""{"apiKey":"{{Secret}}"}""" }
        );

        job.ToString()
            .Should()
            .NotContain(Secret)
            .And.Contain(TraxLogSerialization.OmittedProperty);
    }

    [Test]
    public void Metadata_ToString_leaves_out_its_navigations()
    {
        var metadata = Metadata.Create(
            new CreateMetadata
            {
                Name = "Train",
                ExternalId = "ext",
                Input = null,
            }
        );
        var manifest = Manifest.Create(
            new CreateManifest
            {
                Name = typeof(TraxLogSerializationTests),
                Properties = new Charge { ApiKey = Secret },
            }
        );
        typeof(Metadata).GetProperty(nameof(Metadata.Manifest))!.SetValue(metadata, manifest);

        var text = metadata.ToString();

        using var document = JsonDocument.Parse(text);
        var names = document.RootElement.EnumerateObject().Select(p => p.Name).ToList();
        names.Should().NotContain(["manifest", "parent", "children", "logs"]).And.Contain("name");
        text.Should().NotContain(Secret);
    }

    [Test]
    public void A_missing_run_from_copy_is_left_out_rather_than_marked()
    {
        var entry = WorkQueue.Create(new CreateWorkQueue { TrainName = "T", Input = null });

        entry.ToString().Should().NotContain(TraxLogSerialization.OmittedProperty);
    }

    [Test]
    public void ForLogging_masks_sensitive_members_and_leaves_the_given_options_alone()
    {
        var options = new JsonSerializerOptions();
        var logging = TraxLogSerialization.ForLogging(options);

        JsonSerializer
            .Serialize(new Charge { ApiKey = Secret }, logging)
            .Should()
            .NotContain(Secret)
            .And.Contain(TraxRedaction.MarkerProperty);
        JsonSerializer.Serialize(new Charge { ApiKey = Secret }, options).Should().Contain(Secret);
        TraxLogSerialization.ForLogging(options).Should().BeSameAs(logging);
    }

    private sealed class Charge : IManifestProperties
    {
        [TraxSensitive]
        public string ApiKey { get; set; } = "";
    }
}
