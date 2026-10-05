using AwesomeAssertions;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Radzen;
using Trax.Dashboard.Components.Dialogs;

namespace Trax.Dashboard.Tests.Integration.UnitTests.Components;

/// <summary>
/// The effect configuration dialog edits the live, process-wide configuration object of an
/// effect. A save is all or nothing: every field converts before any is applied, so a bad
/// field leaves the configuration exactly as it was. Nothing is written except by Save.
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

        dialog.Markup.Should().Contain("Failed to save configuration");
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

    private IRenderedComponent<ConfigureEffectDialog> Render(SampleConfiguration configuration) =>
        _ctx.RenderComponent<ConfigureEffectDialog>(p =>
            p.Add(x => x.ConfigurationType, typeof(SampleConfiguration))
                .Add(x => x.Configuration, configuration)
        );

    private static AngleSharp.Dom.IElement Field(
        IRenderedComponent<ConfigureEffectDialog> dialog,
        int index
    ) => dialog.FindAll("input").ElementAt(index);

    public sealed class SampleConfiguration
    {
        public int First { get; set; }
        public int Second { get; set; }
    }
}
