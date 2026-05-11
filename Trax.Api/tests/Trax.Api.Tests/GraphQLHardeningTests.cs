using FluentAssertions;
using HotChocolate;
using HotChocolate.Language;
using HotChocolate.Validation;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Trax.Api.GraphQL.Configuration;
using Trax.Api.GraphQL.Configuration.TraxGraphQLBuilder;
using Trax.Api.GraphQL.Validation;

namespace Trax.Api.Tests;

[TestFixture]
public class GraphQLHardeningTests
{
    #region Builder defaults

    [Test]
    public void MaxExecutionDepth_DefaultIs15()
    {
        var builder = new TraxGraphQLBuilder(new ServiceCollection());

        builder.MaxExecutionDepthValue.Should().Be(15);
        builder.MaxExecutionDepthWasOverridden.Should().BeFalse();
    }

    [Test]
    public void MaxExecutionDepth_Override_StoredAndMarked()
    {
        var builder = new TraxGraphQLBuilder(new ServiceCollection());

        builder.MaxExecutionDepth(12);

        builder.MaxExecutionDepthValue.Should().Be(12);
        builder.MaxExecutionDepthWasOverridden.Should().BeTrue();
    }

    [Test]
    public void MaxExecutionDepth_NonPositive_Throws()
    {
        var builder = new TraxGraphQLBuilder(new ServiceCollection());

        Action zero = () => builder.MaxExecutionDepth(0);
        Action negative = () => builder.MaxExecutionDepth(-1);

        zero.Should().Throw<ArgumentOutOfRangeException>();
        negative.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Test]
    public void MaxOperationsPerRequest_DefaultIs50()
    {
        var builder = new TraxGraphQLBuilder(new ServiceCollection());

        builder.MaxOperationsPerRequestValue.Should().Be(50);
    }

    [Test]
    public void MaxOperationsPerRequest_Override_Stored()
    {
        var builder = new TraxGraphQLBuilder(new ServiceCollection());

        builder.MaxOperationsPerRequest(100);

        builder.MaxOperationsPerRequestValue.Should().Be(100);
    }

    [Test]
    public void MaxOperationsPerRequest_NonPositive_Throws()
    {
        var builder = new TraxGraphQLBuilder(new ServiceCollection());

        Action zero = () => builder.MaxOperationsPerRequest(0);
        Action negative = () => builder.MaxOperationsPerRequest(-5);

        zero.Should().Throw<ArgumentOutOfRangeException>();
        negative.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Test]
    public void AllowIntrospection_NullPredicate_Throws()
    {
        var builder = new TraxGraphQLBuilder(new ServiceCollection());

        Action act = () => builder.AllowIntrospection(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Test]
    public void AllowIntrospection_Predicate_Stored()
    {
        var builder = new TraxGraphQLBuilder(new ServiceCollection());
        Predicate<HttpContext> predicate = _ => true;

        builder.AllowIntrospection(predicate);

        builder.IntrospectionPredicate.Should().BeSameAs(predicate);
    }

    [Test]
    public void ConfigureCost_NullCallback_Throws()
    {
        var builder = new TraxGraphQLBuilder(new ServiceCollection());

        Action act = () => builder.ConfigureCost(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    #endregion

    #region OperationCountValidatorRule

    [Test]
    public void OperationCountValidator_UnderCap_DoesNotReport()
    {
        var rule = new OperationCountValidatorRule(10);
        var doc = Utf8GraphQLParser.Parse("{ a b c }");
        var ctx = Substitute.For<IDocumentValidatorContext>();

        rule.Validate(ctx, doc);

        ctx.DidNotReceive().ReportError(Arg.Any<IError>());
    }

    [Test]
    public void OperationCountValidator_AtCap_DoesNotReport()
    {
        var rule = new OperationCountValidatorRule(3);
        var doc = Utf8GraphQLParser.Parse("{ a b c }");
        var ctx = Substitute.For<IDocumentValidatorContext>();

        rule.Validate(ctx, doc);

        ctx.DidNotReceive().ReportError(Arg.Any<IError>());
    }

    [Test]
    public void OperationCountValidator_AliasedOverCap_ReportsErrorWithCode()
    {
        // Three aliased selections against a single root exceed a cap of 2.
        var rule = new OperationCountValidatorRule(2);
        var doc = Utf8GraphQLParser.Parse("{ a: foo b: foo c: foo }");
        var ctx = Substitute.For<IDocumentValidatorContext>();

        rule.Validate(ctx, doc);

        ctx.Received(1)
            .ReportError(
                Arg.Is<IError>(e =>
                    e.Code == "TRAX_TOO_MANY_OPERATIONS" && e.Message.Contains("maximum")
                )
            );
    }

    [Test]
    public void OperationCountValidator_MultipleOperations_SumCounts()
    {
        // Two operations with 3 selections each = 6 total, cap of 4 rejects.
        var rule = new OperationCountValidatorRule(4);
        var doc = Utf8GraphQLParser.Parse("query A { a b c } query B { d e f }");
        var ctx = Substitute.For<IDocumentValidatorContext>();

        rule.Validate(ctx, doc);

        ctx.Received().ReportError(Arg.Any<IError>());
    }

    [Test]
    public void OperationCountValidator_FragmentDefinitions_NotCounted()
    {
        // Fragment definitions at the document root are not top-level operation
        // selections. They should not count against the cap.
        var rule = new OperationCountValidatorRule(2);
        var doc = Utf8GraphQLParser.Parse(
            "fragment F on Foo { x } fragment G on Foo { y } { a b }"
        );
        var ctx = Substitute.For<IDocumentValidatorContext>();

        rule.Validate(ctx, doc);

        ctx.DidNotReceive().ReportError(Arg.Any<IError>());
    }

    [Test]
    public void OperationCountValidator_StopsAfterFirstOverflow()
    {
        // Even when the overflow happens on the first operation, the rule must
        // short-circuit and not report duplicate errors.
        var rule = new OperationCountValidatorRule(1);
        var doc = Utf8GraphQLParser.Parse("query A { a b c } query B { d e f g h }");
        var ctx = Substitute.For<IDocumentValidatorContext>();

        rule.Validate(ctx, doc);

        ctx.Received(1).ReportError(Arg.Any<IError>());
    }

    #endregion

    #region GraphQLConfiguration plumbing

    [Test]
    public void Build_PropagatesHardeningValues_ToConfiguration()
    {
        var builder = new TraxGraphQLBuilder(new ServiceCollection());
        builder.MaxExecutionDepth(7);
        builder.MaxOperationsPerRequest(25);
        Predicate<HttpContext> predicate = _ => true;
        builder.AllowIntrospection(predicate);
        builder.ConfigureCost(_ => { });

        var config = builder.Build();

        config.MaxExecutionDepth.Should().Be(7);
        config.MaxOperationsPerRequest.Should().Be(25);
    }

    [Test]
    public void Build_NoOverrides_DefaultsApplied()
    {
        var builder = new TraxGraphQLBuilder(new ServiceCollection());

        var config = builder.Build();

        config.MaxExecutionDepth.Should().Be(15);
        config.MaxOperationsPerRequest.Should().Be(50);
    }

    #endregion

    #region RequireAuthorization

    [Test]
    public void RequireAuthorization_DefaultBuilderState_NotRequired()
    {
        var builder = new TraxGraphQLBuilder(new ServiceCollection());

        builder.AuthorizationRequired.Should().BeFalse();
        builder.AuthorizationPolicy.Should().BeNull();
    }

    [Test]
    public void RequireAuthorization_NoArgs_FlagsRequired_WithNullPolicy()
    {
        var builder = new TraxGraphQLBuilder(new ServiceCollection());

        builder.RequireAuthorization();

        builder.AuthorizationRequired.Should().BeTrue();
        builder.AuthorizationPolicy.Should().BeNull();
    }

    [Test]
    public void RequireAuthorization_WithExplicitPolicy_StoresPolicy()
    {
        var builder = new TraxGraphQLBuilder(new ServiceCollection());

        builder.RequireAuthorization("MyCustomPolicy");

        builder.AuthorizationRequired.Should().BeTrue();
        builder.AuthorizationPolicy.Should().Be("MyCustomPolicy");
    }

    [Test]
    public void RequireAuthorization_PropagatesToConfiguration()
    {
        var builder = new TraxGraphQLBuilder(new ServiceCollection());
        builder.RequireAuthorization("ExplicitPolicy");

        var config = builder.Build();

        config.AuthorizationRequired.Should().BeTrue();
        config.AuthorizationPolicy.Should().Be("ExplicitPolicy");
    }

    [Test]
    public void RequireAuthorization_NotCalled_ConfigurationFlagsFalse()
    {
        var builder = new TraxGraphQLBuilder(new ServiceCollection());

        var config = builder.Build();

        config.AuthorizationRequired.Should().BeFalse();
        config.AuthorizationPolicy.Should().BeNull();
    }

    #endregion
}
