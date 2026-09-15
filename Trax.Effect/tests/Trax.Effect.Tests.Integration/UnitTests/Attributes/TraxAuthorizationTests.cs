using FluentAssertions;
using NUnit.Framework;
using Trax.Effect.Attributes;

namespace Trax.Effect.Tests.Integration.UnitTests.Attributes;

/// <summary>
/// The Trax authorization vocabulary: what a class or a method declares, and what Trax refuses
/// to read in its place.
///
/// <para>Enforces <c>docs/adr/0004-trax-owns-the-authorization-vocabulary.md</c>.</para>
/// </summary>
[Property("adr", "docs/adr/0004-trax-owns-the-authorization-vocabulary.md")]
[TestFixture]
public class TraxAuthorizationTests
{
    private const string Adr = "docs/adr/0004-trax-owns-the-authorization-vocabulary.md";

    private static System.Reflection.MethodInfo Method(string name) =>
        typeof(Subject).GetMethod(name)!;

    // ── Methods are a valid target ───────────────────────────────────────

    /// <summary>
    /// The change that makes the rest possible. Before this, [TraxAuthorize] on a resolver was
    /// CS0592, so a consumer who needed to gate one had no Trax attribute to reach for.
    /// </summary>
    [Test]
    public void BothAttributes_ApplyToAMethod()
    {
        Attribute
            .GetCustomAttribute(typeof(TraxAuthorizeAttribute), typeof(AttributeUsageAttribute))
            .Should()
            .BeOfType<AttributeUsageAttribute>()
            .Which.ValidOn.Should()
            .HaveFlag(AttributeTargets.Method, Adr);

        Attribute
            .GetCustomAttribute(
                typeof(TraxAllowAnonymousAttribute),
                typeof(AttributeUsageAttribute)
            )
            .Should()
            .BeOfType<AttributeUsageAttribute>()
            .Which.ValidOn.Should()
            .HaveFlag(AttributeTargets.Method);
    }

    [Test]
    public void ClassAndInterfaceTargets_AreStillValid()
    {
        var usage = (AttributeUsageAttribute)
            Attribute.GetCustomAttribute(
                typeof(TraxAuthorizeAttribute),
                typeof(AttributeUsageAttribute)
            )!;

        usage.ValidOn.Should().HaveFlag(AttributeTargets.Class);
        usage.ValidOn.Should().HaveFlag(AttributeTargets.Interface);
    }

    // ── Reading a method ─────────────────────────────────────────────────

    [Test]
    public void Method_WithNoAttributes_DeclaresNothing()
    {
        var declaration = TraxAuthorization.Read(Method(nameof(Subject.Undeclared)));

        declaration.Declares.Should().BeFalse();
        declaration.HasAuthorize.Should().BeFalse();
        declaration.AllowAnonymous.Should().BeFalse();
        declaration.Conflicts.Should().BeFalse();
        declaration.HasForeign.Should().BeFalse();
    }

    [Test]
    public void Method_WithAuthorize_IsGated()
    {
        var declaration = TraxAuthorization.Read(Method(nameof(Subject.Gated)));

        declaration.HasAuthorize.Should().BeTrue();
        declaration.Declares.Should().BeTrue();
        declaration.Authorize.Should().ContainSingle().Which.Roles.Should().Be("subscriber");
    }

    [Test]
    public void Method_WithAllowAnonymous_IsPublic()
    {
        var declaration = TraxAuthorization.Read(Method(nameof(Subject.Public)));

        declaration.AllowAnonymous.Should().BeTrue();
        declaration.HasAuthorize.Should().BeFalse();
        declaration.Declares.Should().BeTrue();
    }

    [Test]
    public void Method_WithBoth_Conflicts()
    {
        TraxAuthorization.Read(Method(nameof(Subject.Conflicted))).Conflicts.Should().BeTrue();
    }

    /// <summary>
    /// AllowMultiple is true, and the combinator semantics depend on every instance being seen.
    /// </summary>
    [Test]
    public void Method_WithRepeatedAuthorize_KeepsEveryInstance()
    {
        var declaration = TraxAuthorization.Read(Method(nameof(Subject.DoublyGated)));

        declaration.Authorize.Should().HaveCount(2);
        declaration.Authorize.Select(a => a.Policy).Should().Contain("AdminOnly");
        declaration.Authorize.Select(a => a.Roles).Should().Contain("ops");
    }

    [Test]
    public void Method_Override_InheritsTheBaseDeclaration()
    {
        var declaration = TraxAuthorization.Read(
            typeof(DerivedSubject).GetMethod(nameof(DerivedSubject.Inherited))!
        );

        declaration.HasAuthorize.Should().BeTrue("the attribute is Inherited = true");
    }

    // ── Reading a type ───────────────────────────────────────────────────

    [Test]
    public void Type_ReadsItsOwnAndItsInterfacesAttributes()
    {
        var declaration = TraxAuthorization.Read(typeof(GatedByInterface));

        declaration.HasAuthorize.Should().BeTrue();
        declaration.Authorize.Should().ContainSingle();
    }

    [Test]
    public void Type_DeDuplicatesTheSameAttributeInstance()
    {
        var declaration = TraxAuthorization.Read(typeof(DoubleInterfaced));

        declaration
            .Authorize.Should()
            .ContainSingle("one attribute reachable through two interfaces is one attribute");
    }

    [Test]
    public void Type_WithAllowAnonymous_IsPublic()
    {
        TraxAuthorization.Read(typeof(PublicType)).AllowAnonymous.Should().BeTrue();
    }

    // ── Foreign attributes are refused ───────────────────────────────────

    /// <summary>
    /// The ban. A resolver carrying HotChocolate's attribute has not declared a posture Trax can
    /// enforce, and Trax says so rather than quietly reading somebody else's vocabulary.
    /// </summary>
    [Test]
    public void Method_WithAForeignAttribute_IsReported()
    {
        var declaration = TraxAuthorization.Read(Method(nameof(Subject.ForeignGated)));

        declaration
            .HasForeign.Should()
            .BeTrue(
                "Trax owns its authorization vocabulary, per "
                    + "docs/adr/0004-trax-owns-the-authorization-vocabulary.md"
            );
        declaration
            .ForeignAttributes.Should()
            .ContainSingle()
            .Which.Should()
            .Be("HotChocolate.Authorization.AuthorizeAttribute");
    }

    [Test]
    public void Method_WithAForeignAnonymousAttribute_IsReported()
    {
        TraxAuthorization
            .Read(Method(nameof(Subject.ForeignPublic)))
            .ForeignAttributes.Should()
            .ContainSingle()
            .Which.Should()
            .Be("HotChocolate.Authorization.AllowAnonymousAttribute");
    }

    /// <summary>
    /// A foreign attribute is reported even when a Trax attribute sits beside it: the point is
    /// that one member is being described in two vocabularies, not that nothing was declared.
    /// </summary>
    [Test]
    public void Method_WithBothVocabularies_IsStillReported()
    {
        var declaration = TraxAuthorization.Read(Method(nameof(Subject.MixedVocabulary)));

        declaration.HasAuthorize.Should().BeTrue();
        declaration.HasForeign.Should().BeTrue();
    }

    [Test]
    public void ForeignAttributes_AreMatchedByFullName_NotByShortName()
    {
        TraxAuthorization
            .Read(Method(nameof(Subject.LookalikeAttribute)))
            .HasForeign.Should()
            .BeFalse(
                "an unrelated attribute that happens to be called Authorize is not HotChocolate's"
            );
    }

    /// <summary>
    /// ASP.NET Core's attribute governs endpoints and MVC actions, a surface Trax does not own.
    /// Banning it there would be wrong, so it is deliberately not in the list.
    /// </summary>
    [Test]
    public void AspNetCoreAuthorize_IsNotBanned()
    {
        TraxAuthorization
            .ForeignAuthorizationAttributes.Should()
            .NotContain("Microsoft.AspNetCore.Authorization.AuthorizeAttribute");
    }

    [Test]
    public void ForeignList_CoversBothHotChocolateNamespaces()
    {
        TraxAuthorization
            .ForeignAuthorizationAttributes.Should()
            .Contain("HotChocolate.Authorization.AuthorizeAttribute")
            .And.Contain("HotChocolate.Authorization.AllowAnonymousAttribute")
            .And.Contain("HotChocolate.AspNetCore.Authorization.AuthorizeAttribute")
            .And.Contain("HotChocolate.AspNetCore.Authorization.AllowAnonymousAttribute");
    }

    // ── Messages and validation ──────────────────────────────────────────

    [Test]
    public void ForeignAttributeMessage_NamesTheSubjectAndTheTraxReplacement()
    {
        var message = TraxAuthorization.ForeignAttributeMessage(
            "GraphQL field 'Issue.content'",
            ["HotChocolate.Authorization.AuthorizeAttribute"]
        );

        message.Should().Contain("Issue.content");
        message.Should().Contain("[Authorize]");
        message.Should().Contain("[TraxAuthorize]");
        message.Should().Contain("[TraxAllowAnonymous]");
    }

    [Test]
    public void Validate_WithAForeignAttribute_Throws()
    {
        var declaration = TraxAuthorization.Read(Method(nameof(Subject.ForeignGated)));

        var act = () => TraxAuthorization.Validate(declaration, "the subject");

        act.Should().Throw<InvalidOperationException>().WithMessage("*[TraxAuthorize]*");
    }

    [Test]
    public void Validate_WithACleanDeclaration_DoesNotThrow()
    {
        var declaration = TraxAuthorization.Read(Method(nameof(Subject.Gated)));

        var act = () => TraxAuthorization.Validate(declaration, "the subject");

        act.Should().NotThrow();
    }

    [Test]
    public void Read_NullArguments_Throw()
    {
        var method = () => TraxAuthorization.Read((System.Reflection.MethodInfo)null!);
        var type = () => TraxAuthorization.Read((Type)null!);

        method.Should().Throw<ArgumentNullException>();
        type.Should().Throw<ArgumentNullException>();
    }

    // ── Fixtures ─────────────────────────────────────────────────────────

    /// <summary>
    /// Stands in for HotChocolate's attribute. Naming it exactly is what the ban matches on, and
    /// Trax.Effect does not reference HotChocolate, so the real one cannot be used here.
    /// </summary>
    private sealed class Subject
    {
        public string Undeclared() => "x";

        [TraxAuthorize(Roles = "subscriber")]
        public string Gated() => "x";

        [TraxAllowAnonymous]
        public string Public() => "x";

        [TraxAuthorize]
        [TraxAllowAnonymous]
        public string Conflicted() => "x";

        [TraxAuthorize(Policy = "AdminOnly")]
        [TraxAuthorize(Roles = "ops")]
        public string DoublyGated() => "x";

        [HotChocolate.Authorization.Authorize]
        public string ForeignGated() => "x";

        [HotChocolate.Authorization.AllowAnonymous]
        public string ForeignPublic() => "x";

        [TraxAuthorize(Roles = "subscriber")]
        [HotChocolate.Authorization.Authorize]
        public string MixedVocabulary() => "x";

        [Lookalike.Authorize]
        public string LookalikeAttribute() => "x";
    }

    private class BaseSubject
    {
        [TraxAuthorize(Roles = "admin")]
        public virtual string Inherited() => "x";
    }

    private sealed class DerivedSubject : BaseSubject
    {
        public override string Inherited() => "y";
    }

    [TraxAuthorize(Roles = "admin")]
    private interface IGated;

    private sealed class GatedByInterface : IGated;

    private interface IAlsoGated : IGated;

    private sealed class DoubleInterfaced : IGated, IAlsoGated;

    [TraxAllowAnonymous]
    private sealed class PublicType;
}
