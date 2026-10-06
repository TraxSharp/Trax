using AwesomeAssertions;
using Trax.Api.GraphQL.Configuration;
using Trax.Api.GraphQL.TypeModules;
using Trax.Effect.Attributes;

namespace Trax.Api.Tests;

/// <summary>
/// Which classes a query model can reach, read off the CLR types before the schema exists, so a
/// declared gate is on every object type HotChocolate infers for them: through a property, a
/// collection, a method, or a method's task, but never through a scalar or a framework type.
/// </summary>
[TestFixture]
public class NavigationTargetDiscoveryTests
{
    [Test]
    public void EveryMemberShapeThatReturnsAnEntity_IsFollowed()
    {
        var targets = NavigationTargetPosture
            .Discover([typeof(DiscoveryModel)])
            .Select(t => t.EntityType);

        targets
            .Should()
            .BeEquivalentTo([
                typeof(ByProperty),
                typeof(ByList),
                typeof(ByEnumerable),
                typeof(ByArray),
                typeof(ByMethod),
                typeof(ByTask),
                typeof(ByValueTask),
                typeof(Transitive),
            ]);
    }

    [Test]
    public void ATargetCarriesThePostureItDeclares()
    {
        var targets = NavigationTargetPosture.Discover([typeof(DiscoveryModel)]);

        targets.Single(t => t.EntityType == typeof(ByMethod)).IsGated.Should().BeTrue();
        targets.Single(t => t.EntityType == typeof(ByProperty)).AllowAnonymous.Should().BeTrue();
    }

    [Test]
    public void AClassThatIsNotAQueryModel_HasNoNarrowedFieldSet()
    {
        QueryModelFieldSet.Restricted(typeof(ByProperty)).Should().BeNull();
    }

    public sealed class DiscoveryModel
    {
        public ByProperty? Property { get; set; }
        public List<ByList> List { get; set; } = [];
        public IEnumerable<ByEnumerable> Enumerable { get; set; } = [];
        public ByArray[] Array { get; set; } = [];
        public string Name { get; set; } = "";
        public int[] Scores { get; set; } = [];
        public Uri? Link { get; set; }

        public ByMethod GetByMethod() => new();

        public Task<ByTask> GetByTaskAsync() => Task.FromResult(new ByTask());

        public ValueTask<ByValueTask> GetByValueTaskAsync() => new(new ByValueTask());

        public void Touch() { }
    }

    [TraxAllowAnonymous]
    public sealed class ByProperty
    {
        public Transitive? Next { get; set; }
    }

    public sealed class ByList;

    public sealed class ByEnumerable;

    public sealed class ByArray;

    [TraxAuthorize(Roles = "Admin")]
    public sealed class ByMethod;

    public sealed class ByTask;

    public sealed class ByValueTask;

    public sealed class Transitive;
}
