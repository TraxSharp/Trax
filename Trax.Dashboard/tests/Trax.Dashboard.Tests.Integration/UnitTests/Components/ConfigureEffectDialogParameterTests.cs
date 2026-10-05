using AwesomeAssertions;
using Bunit;
using Radzen;
using Radzen.Blazor;
using Trax.Dashboard.Components.Dialogs;
using Trax.Effect.Provider.Parameter.Configuration;

namespace Trax.Dashboard.Tests.Integration.UnitTests.Components;

/// <summary>
/// The Configure Effect dialog over the real <see cref="ParameterEffectConfiguration"/>, whose
/// fields are what the dialog has to get right: a nullable byte cap where <see langword="null"/>
/// means no cap, and predicate delegates the host sets in code. A save writes only what the
/// operator changed, keeps <see langword="null"/> as <see langword="null"/>, never round-trips a
/// value it cannot edit through its text form, and reads numbers in the invariant culture.
/// </summary>
[TestFixture]
public class ConfigureEffectDialogParameterTests
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
    public void An_unchanged_save_leaves_a_null_byte_cap_null()
    {
        var configuration = new ParameterEffectConfiguration { MaxParameterBytes = null };
        var dialog = Render(configuration);

        Save(dialog);

        dialog.FindAll(".rz-alert").Should().BeEmpty();
        configuration
            .MaxParameterBytes.Should()
            .BeNull("null means no cap; a cap of 0 would truncate every saved parameter");
    }

    [Test]
    public void An_unchanged_save_leaves_the_default_byte_cap_as_it_was()
    {
        var configuration = new ParameterEffectConfiguration { MaxParameterBytes = 1_048_576 };
        var dialog = Render(configuration);

        Save(dialog);

        configuration.MaxParameterBytes.Should().Be(1_048_576);
    }

    [Test]
    public void Clearing_the_byte_cap_saves_no_cap()
    {
        var configuration = new ParameterEffectConfiguration { MaxParameterBytes = 1_048_576 };
        var dialog = Render(configuration);

        dialog.Find("input.rz-textbox").Change("");
        Save(dialog);

        configuration.MaxParameterBytes.Should().BeNull();
    }

    [Test]
    public void With_a_predicate_set_toggling_a_bool_saves_and_keeps_the_predicate()
    {
        Func<string, bool> predicate = name => name.Contains("Keep");
        var configuration = new ParameterEffectConfiguration { ShouldSaveInputs = predicate };
        var dialog = Render(configuration);

        dialog
            .FindAll("input.rz-textbox")
            .Should()
            .ContainSingle("only the byte cap is a text field; a predicate is not editable here");
        dialog
            .Find("[data-testid='set-in-code-ShouldSaveInputs']")
            .TextContent.Should()
            .Contain("Set in code");

        var saveInputs = dialog.FindComponents<RadzenSwitch>().First();
        dialog.InvokeAsync(() => saveInputs.Instance.ValueChanged.InvokeAsync(false));
        Save(dialog);

        dialog.FindAll(".rz-alert").Should().BeEmpty();
        configuration.SaveInputs.Should().BeFalse();
        configuration.ShouldSaveInputs.Should().BeSameAs(predicate);
    }

    [Test]
    [SetCulture("de-DE")]
    public void A_decimal_is_read_in_the_invariant_culture()
    {
        var configuration = new BoundedConfiguration { Ratio = 1, Limit = 5 };
        var dialog = _ctx.RenderComponent<ConfigureEffectDialog>(p =>
            p.Add(x => x.ConfigurationType, typeof(BoundedConfiguration))
                .Add(x => x.Configuration, configuration)
        );

        dialog.FindAll("input.rz-textbox").First().Change("1.5");
        Save(dialog);

        configuration
            .Ratio.Should()
            .Be(1.5, "'.' is the decimal point whatever the server culture");
    }

    [Test]
    public void A_value_its_property_declares_out_of_range_is_refused()
    {
        var configuration = new BoundedConfiguration { Ratio = 1, Limit = 5 };
        var dialog = _ctx.RenderComponent<ConfigureEffectDialog>(p =>
            p.Add(x => x.ConfigurationType, typeof(BoundedConfiguration))
                .Add(x => x.Configuration, configuration)
        );

        dialog.FindAll("input.rz-textbox").Last().Change("-1");
        Save(dialog);

        dialog.Markup.Should().Contain("Failed to save configuration");
        configuration.Limit.Should().Be(5);
    }

    private IRenderedComponent<ConfigureEffectDialog> Render(
        ParameterEffectConfiguration configuration
    ) =>
        _ctx.RenderComponent<ConfigureEffectDialog>(p =>
            p.Add(x => x.ConfigurationType, typeof(ParameterEffectConfiguration))
                .Add(x => x.Configuration, configuration)
        );

    private static void Save(IRenderedComponent<ConfigureEffectDialog> dialog) =>
        dialog.Find("button:contains('Save')").Click();

    public sealed class BoundedConfiguration
    {
        public double Ratio { get; set; }

        [System.ComponentModel.DataAnnotations.Range(1, int.MaxValue)]
        public int? Limit { get; set; }
    }
}
