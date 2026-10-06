using AwesomeAssertions;
using HotChocolate.Types.Descriptors.Configurations;
using Trax.Api.GraphQL.Configuration;
using Trax.Api.GraphQL.Subscriptions;
using Trax.Effect.Attributes;

namespace Trax.Api.Tests;

/// <summary>
/// The census's two guards on Trax's own code, driven on hand-built configurations because no
/// shipped type trips them: a field on Trax's subscription root that declares no authorization,
/// and a <c>[TraxAuthorize]</c> field the census accepted whose gate was never emitted. Each
/// refuses the host rather than serve the field ungated.
/// </summary>
[TestFixture]
public class TypeExtensionExposureInterceptorTests
{
    private static (TypeExtensionExposureInterceptor, TypeExtensionExposureReport) Create()
    {
        var report = new TypeExtensionExposureReport();
        return (new TypeExtensionExposureInterceptor(new([], [], [], []), report), report);
    }

    [Test]
    public void AnOwnSubscriptionFieldDeclaringNothing_IsReported()
    {
        var (interceptor, report) = Create();
        var subscription = new ObjectTypeConfiguration(
            "LifecycleSubscriptions",
            runtimeType: typeof(LifecycleSubscriptions)
        );
        // Declared by a base of the runtime type, so a natural member of it, with no attribute.
        subscription.Fields.Add(
            new ObjectFieldConfiguration("probe") { Member = typeof(object).GetMethod("ToString") }
        );

        interceptor.OnBeforeCompleteType(null!, subscription);

        report
            .Violations.Should()
            .ContainSingle()
            .Which.Message.Should()
            .Contain("LifecycleSubscriptions.probe")
            .And.Contain("declares no authorization");
    }

    [Test]
    public void AnOwnSubscriptionFieldBuiltInCode_IsLeftToItsBuilder()
    {
        var (interceptor, report) = Create();
        var subscription = new ObjectTypeConfiguration(
            "LifecycleSubscriptions",
            runtimeType: typeof(LifecycleSubscriptions)
        );
        // A field built in code has no member to carry an attribute; its builder gates it.
        subscription.Fields.Add(new ObjectFieldConfiguration("built"));

        interceptor.OnBeforeCompleteType(null!, subscription);

        report.Violations.Should().BeEmpty();
    }

    /// <summary>
    /// The census accepts a <c>[TraxAuthorize]</c> field because the emission phase gates it.
    /// Run the census without emission and the field arrives ungated, which it reports as a
    /// defect rather than accept.
    /// </summary>
    [Test]
    public void AnAcceptedGatedFieldWithoutItsDirective_IsReported()
    {
        var (interceptor, report) = Create();
        var root = new ObjectTypeConfiguration(
            "RootQuery",
            runtimeType: typeof(Trax.Api.GraphQL.Queries.RootQuery)
        );
        root.Fields.Add(
            new ObjectFieldConfiguration("gated")
            {
                Member = typeof(GatedResolvers).GetMethod(nameof(GatedResolvers.Gated)),
            }
        );

        interceptor.OnBeforeCompleteType(null!, root);

        report
            .Violations.Should()
            .ContainSingle()
            .Which.Message.Should()
            .Contain("RootQuery.gated")
            .And.Contain("no @authorize directive was emitted");
    }

    public sealed class GatedResolvers
    {
        [TraxAuthorize(Roles = "Admin")]
        public string Gated() => "gated";
    }
}
