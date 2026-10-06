using AwesomeAssertions;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Radzen;
using Trax.Dashboard.Components.Dialogs;
using Trax.Dashboard.Tests.Integration.Fakes.Services;
using Trax.Effect.Attributes;

namespace Trax.Dashboard.Tests.Integration.UnitTests.Components;

/// <summary>
/// The effect configuration dialog edits an effect's process-wide settings through the
/// Scheduler's effect settings service, the one the API calls. A save is all or nothing: every
/// field converts before any is applied, so a bad field leaves the configuration exactly as it
/// was. Nothing is written except by Save, and a sensitive setting is never shown.
/// </summary>
[TestFixture]
public class ConfigureEffectDialogTests
{
    private Bunit.TestContext _ctx = null!;

    [SetUp]
    public void SetUp()
    {
        _ctx = new Bunit.TestContext();
        _ctx.Services.AddRadzenComponents();
        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [TearDown]
    public void TearDown() => _ctx.Dispose();

    [Test]
    public void A_save_with_an_invalid_later_field_leaves_the_configuration_unchanged()
    {
        var configuration = new SampleConfiguration { First = 1, Second = 2 };
        var dialog = Render(configuration);

        Field(dialog, 0).Change("5");
        Field(dialog, 1).Change("not a number");
        dialog.Find("button:contains('Save')").Click();

        dialog.Markup.Should().Contain("The configuration was not saved");
        configuration.First.Should().Be(1, "no field is applied unless every field converts");
        configuration.Second.Should().Be(2);
    }

    [Test]
    public void A_valid_save_applies_every_field()
    {
        var configuration = new SampleConfiguration { First = 1, Second = 2 };
        var dialog = Render(configuration);

        Field(dialog, 0).Change("5");
        Field(dialog, 1).Change("6");
        dialog.Find("button:contains('Save')").Click();

        configuration.First.Should().Be(5);
        configuration.Second.Should().Be(6);
    }

    [Test]
    public void A_save_sends_only_the_fields_the_operator_changed()
    {
        var configuration = new SampleConfiguration { First = 1, Second = 2 };
        var dialog = Render(configuration);

        // Another circuit saves Second while this dialog is open.
        configuration.Second = 9;
        Field(dialog, 0).Change("5");
        dialog.Find("button:contains('Save')").Click();

        configuration.First.Should().Be(5);
        configuration.Second.Should().Be(9, "the dialog did not change it, so it does not send it");
    }

    [Test]
    public void Cancel_writes_nothing_to_the_configuration()
    {
        var configuration = new SampleConfiguration { First = 1, Second = 2 };
        var dialog = Render(configuration);

        // Another circuit saves while this dialog is open. Cancelling here must not put back
        // the values this dialog opened with.
        configuration.First = 9;
        dialog.Find("button:contains('Cancel')").Click();

        configuration.First.Should().Be(9);
    }

    [Test]
    public void A_sensitive_setting_is_never_shown_and_a_blank_one_is_not_written()
    {
        var configuration = new SecretConfiguration { ApiKey = "s3cr3t-value", Region = "eu" };
        var dialog = RenderFor(configuration);

        dialog.Markup.Should().NotContain("s3cr3t-value");
        dialog
            .Find("[data-testid='sensitive-ApiKey'] input")
            .GetAttribute("value")
            .Should()
            .BeNullOrEmpty();
        dialog
            .Find("[data-testid='sensitive-ApiKey']")
            .TextContent.Should()
            .Contain("a value is set");

        dialog
            .FindAll("input.rz-textbox")
            .Single(i => i.GetAttribute("type") != "password")
            .Change("us");
        dialog.Find("button:contains('Save')").Click();

        configuration.Region.Should().Be("us");
        configuration.ApiKey.Should().Be("s3cr3t-value", "a blank sensitive field keeps its value");
    }

    [Test]
    public void A_new_value_typed_into_a_sensitive_setting_is_written()
    {
        var configuration = new SecretConfiguration { ApiKey = "old", Region = "eu" };
        var dialog = RenderFor(configuration);

        dialog.Find("[data-testid='sensitive-ApiKey'] input").Change("new-key");
        dialog.Find("button:contains('Save')").Click();

        configuration.ApiKey.Should().Be("new-key");
        configuration.Region.Should().Be("eu");
    }

    private IRenderedComponent<ConfigureEffectDialog> Render(SampleConfiguration configuration) =>
        RenderFor(configuration);

    private IRenderedComponent<ConfigureEffectDialog> RenderFor(object configuration)
    {
        ConfigurableEffectFactory.Register(_ctx.Services, configuration);
        return _ctx.RenderComponent<ConfigureEffectDialog>(p =>
            p.Add(x => x.EffectFullName, ConfigurableEffectFactory.FullName)
        );
    }

    private static AngleSharp.Dom.IElement Field(
        IRenderedComponent<ConfigureEffectDialog> dialog,
        int index
    ) => dialog.FindAll("input").ElementAt(index);

    public sealed class SampleConfiguration
    {
        public int First { get; set; }
        public int Second { get; set; }
    }

    public sealed class SecretConfiguration
    {
        [TraxSensitive]
        public string? ApiKey { get; set; }

        public string Region { get; set; } = "";
    }
}
