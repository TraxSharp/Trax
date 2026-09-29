using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Radzen;
using Trax.Dashboard.Components.Pages.Data;
using Trax.Dashboard.Tests.Integration.Fakes.Data;
using Trax.Effect.Data.Services.IDataContextFactory;

namespace Trax.Dashboard.Tests.Integration.UnitTests.Components;

/// <summary>
/// The persisted-operation detail page on a host that has not enabled persisted operations.
/// Such a host registers no <c>IPersistedOperationStore</c>, and the page must say the feature
/// is off rather than fail to construct, which ends the whole circuit.
/// </summary>
[TestFixture]
public class PersistedOperationDetailPageTests
{
    private Bunit.TestContext _ctx = null!;

    [SetUp]
    public void SetUp()
    {
        _ctx = new Bunit.TestContext();
        _ctx.Services.AddRadzenComponents();
        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        _ctx.Services.AddSingleton<IDataContextProviderFactory>(new InMemoryDataContextFactory());
    }

    [TearDown]
    public void TearDown() => _ctx.Dispose();

    [Test]
    public void Without_persisted_operations_the_detail_page_says_the_feature_is_not_enabled()
    {
        var page = _ctx.RenderComponent<PersistedOperationDetailPage>(p =>
            p.Add(x => x.Id, "greet.v1")
        );

        page.Markup.Should().Contain("Persisted Operations is not enabled on this server");
    }
}
