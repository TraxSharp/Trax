using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using FluentAssertions;
using Trax.Api.GraphQL.Projection;

namespace Trax.Api.Tests;

/// <summary>
/// The key resolver decides which columns Trax pins into a projection for hand-written
/// resolvers. Getting it wrong is silent — the resolver reads a default value — so every
/// convention EF Core infers a key from is covered here.
/// </summary>
[TestFixture]
public class EntityKeyResolverTests
{
    #region [Key] annotation

    [Test]
    public void GetKeyPropertyNames_KeyAttribute_ReturnsThatProperty()
    {
        EntityKeyResolver.GetKeyPropertyNames(typeof(AnnotatedKey)).Should().Equal("Code");
    }

    [Test]
    public void GetKeyPropertyNames_KeyAttribute_WinsOverIdConvention()
    {
        // Both a [Key] Code and an Id property exist. EF binds the annotation.
        EntityKeyResolver
            .GetKeyPropertyNames(typeof(AnnotatedKeyAlongsideId))
            .Should()
            .Equal("Code");
    }

    [Test]
    public void GetKeyPropertyNames_CompositeKey_OrdersByColumnOrder()
    {
        EntityKeyResolver.GetKeyPropertyNames(typeof(CompositeKey)).Should().Equal("Left", "Right");
    }

    [Test]
    public void GetKeyPropertyNames_CompositeKeyWithoutColumnOrder_IsDeterministic()
    {
        // No [Column(Order)] to order by: the result must still be stable, or the emitted
        // requirement string would churn between runs.
        EntityKeyResolver
            .GetKeyPropertyNames(typeof(CompositeKeyUnordered))
            .Should()
            .Equal("Alpha", "Beta");
    }

    [Test]
    public void GetKeyPropertyNames_KeyOnInheritedProperty_IsFound()
    {
        EntityKeyResolver
            .GetKeyPropertyNames(typeof(DerivedFromAnnotatedBase))
            .Should()
            .Equal("Code");
    }

    #endregion

    #region Conventions

    [Test]
    public void GetKeyPropertyNames_IdConvention_ReturnsId()
    {
        EntityKeyResolver.GetKeyPropertyNames(typeof(ConventionalId)).Should().Equal("Id");
    }

    [Test]
    public void GetKeyPropertyNames_TypeNameIdConvention_ReturnsIt()
    {
        EntityKeyResolver.GetKeyPropertyNames(typeof(Widget)).Should().Equal("WidgetId");
    }

    [Test]
    public void GetKeyPropertyNames_IdConvention_IsCaseInsensitive()
    {
        // EF matches the convention case-insensitively; so must this.
        EntityKeyResolver.GetKeyPropertyNames(typeof(LowerCaseId)).Should().Equal("id");
    }

    [Test]
    public void GetKeyPropertyNames_IdWins_OverTypeNameId()
    {
        EntityKeyResolver.GetKeyPropertyNames(typeof(BothConventions)).Should().Equal("Id");
    }

    #endregion

    #region No inferable key

    [Test]
    public void GetKeyPropertyNames_NoKey_ReturnsEmpty()
    {
        // A fluent-API-only key is invisible from the class. Returning nothing is what
        // makes the interceptor skip the type rather than pin the wrong column.
        EntityKeyResolver.GetKeyPropertyNames(typeof(NoKey)).Should().BeEmpty();
    }

    [Test]
    public void GetKeyPropertyNames_NoProperties_ReturnsEmpty()
    {
        EntityKeyResolver.GetKeyPropertyNames(typeof(Empty)).Should().BeEmpty();
    }

    [Test]
    public void GetKeyPropertyNames_PrivateId_IsNotAKey()
    {
        // Projection can only ever populate public properties.
        EntityKeyResolver.GetKeyPropertyNames(typeof(PrivateId)).Should().BeEmpty();
    }

    [Test]
    public void GetKeyPropertyNames_StaticId_IsNotAKey()
    {
        EntityKeyResolver.GetKeyPropertyNames(typeof(StaticId)).Should().BeEmpty();
    }

    [Test]
    public void GetKeyPropertyNames_NullType_Throws()
    {
        var act = () => EntityKeyResolver.GetKeyPropertyNames(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    #endregion

    #region Fixtures

    private sealed class AnnotatedKey
    {
        [Key]
        public string Code { get; set; } = "";
    }

    private sealed class AnnotatedKeyAlongsideId
    {
        [Key]
        public string Code { get; set; } = "";

        public int Id { get; set; }
    }

    private sealed class CompositeKey
    {
        [Key]
        [Column(Order = 0)]
        public int Left { get; set; }

        [Key]
        [Column(Order = 1)]
        public int Right { get; set; }
    }

    private sealed class CompositeKeyUnordered
    {
        [Key]
        public int Beta { get; set; }

        [Key]
        public int Alpha { get; set; }
    }

    private abstract class AnnotatedBase
    {
        [Key]
        public string Code { get; set; } = "";
    }

    private sealed class DerivedFromAnnotatedBase : AnnotatedBase;

    private sealed class ConventionalId
    {
        public int Id { get; set; }
    }

    private sealed class Widget
    {
        public int WidgetId { get; set; }
    }

    private sealed class LowerCaseId
    {
        public int id { get; set; }
    }

    private sealed class BothConventions
    {
        public int Id { get; set; }

        public int BothConventionsId { get; set; }
    }

    private sealed class NoKey
    {
        public string Slug { get; set; } = "";
    }

    private sealed class Empty;

    private sealed class PrivateId
    {
        private int Id { get; set; }

        public string Slug { get; set; } = "";
    }

    private sealed class StaticId
    {
        public static int Id { get; set; }

        public string Slug { get; set; } = "";
    }

    #endregion
}
