using AwesomeAssertions;
using HotChocolate.Execution;
using HotChocolate.Types;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Trax.Effect.Attributes;
using Trax.Mediator.Services.TrainDiscovery;

namespace Trax.Api.Tests;

/// <summary>
/// A <c>[TraxAuthorize]</c> on a query model's own resolver method gates that field on every host,
/// whatever else the host registers. The directive is emitted by the same type interceptor that
/// censuses type extensions, and that interceptor is part of every schema. Guard for
/// <c>docs/adr/0003-a-type-extension-field-declares-its-own-posture.md</c>.
/// </summary>
[Property("adr", "docs/adr/0003-a-type-extension-field-declares-its-own-posture.md")]
[TestFixture]
public class DeclaredMemberPostureTests
{
    private const string Query = "{ discover { memberPostureNotes { nodes { title secret } } } }";

    private static Task<IHost> StartAsync(bool withTypeExtension) =>
        PostureHost.StartAsync(
            g =>
            {
                g.AddDbContext<MemberPostureContext>();
                if (withTypeExtension)
                    g.AddTypeExtension<HarmlessRootField>();
                return g;
            },
            s =>
                s.AddInMemoryContext<MemberPostureContext>(c =>
                    c.Notes.Add(new MemberPostureNote { Id = 1, Title = "open" })
                )
        );

    [TestCase(false)]
    [TestCase(true)]
    public async Task AGatedMemberOfAnAnonymousModel_RefusesAnAnonymousCaller(
        bool withTypeExtension
    )
    {
        using var host = await StartAsync(withTypeExtension);

        (await host.PostAsync(Query))
            .Should()
            .Contain(
                "TRAX_AUTHORIZATION",
                "docs/adr/0003-a-type-extension-field-declares-its-own-posture.md: a member's "
                    + "declared posture is enforced on every host"
            )
            .And.NotContain("member-secret");
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task AGatedMemberOfAnAnonymousModel_RefusesACallerWithoutTheRole(
        bool withTypeExtension
    )
    {
        using var host = await StartAsync(withTypeExtension);

        (await host.PostAsync(Query, PostureHost.SupportKey))
            .Should()
            .Contain("TRAX_AUTHORIZATION")
            .And.NotContain("member-secret");
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task AGatedMemberOfAnAnonymousModel_ServesACallerInTheRole(bool withTypeExtension)
    {
        using var host = await StartAsync(withTypeExtension);

        (await host.PostAsync(Query, PostureHost.AdminKey))
            .Should()
            .Contain("member-secret")
            .And.NotContain("\"errors\"");
    }

    [Test]
    public async Task TheRestOfTheModel_StaysAnonymous()
    {
        using var host = await StartAsync(withTypeExtension: false);

        (await host.PostAsync("{ discover { memberPostureNotes { nodes { title } } } }"))
            .Should()
            .Contain("\"open\"")
            .And.NotContain("\"errors\"");
    }

    /// <summary>
    /// A model exposed through an interface builds its type from that interface's members, and
    /// keeps the class's gate on it.
    /// </summary>
    [Test]
    public async Task AGatedModelExposedThroughAnInterface_KeepsItsGate()
    {
        using var host = await PostureHost.StartAsync(
            g => g.AddDbContext<ExposedContext>(),
            s =>
                s.AddInMemoryContext<ExposedContext>(c =>
                    c.Notes.Add(new ExposedNote { Id = 1, Title = "exposed-title" })
                )
        );
        const string query = "{ discover { exposedNotes { nodes { title } } } }";

        (await host.PostAsync(query, PostureHost.SupportKey))
            .Should()
            .Contain("TRAX_AUTHORIZATION")
            .And.NotContain("exposed-title");
        (await host.PostAsync(query, PostureHost.AdminKey))
            .Should()
            .Contain("exposed-title")
            .And.NotContain("\"errors\"");
    }

    /// <summary>
    /// A train's output type is built by HotChocolate from the CLR type, so a gated member of it
    /// carries the same directive, on a host with no type extension.
    /// </summary>
    [Test]
    public async Task AGatedMemberOfATrainOutputType_CarriesTheRoleGate()
    {
        using var host = await PostureHost.StartAsync(g => g, trains: [OutputTrain()]);

        var executor = await host
            .Services.GetRequiredService<IRequestExecutorProvider>()
            .GetExecutorAsync("trax");
        var output = executor
            .Schema.Types.OfType<ObjectType>()
            .Single(t => t.RuntimeType == typeof(MemberPostureOutput));

        var gate = output
            .Fields["secret"]
            .Directives.Where(d => d.Type.Name == "authorize")
            .Select(d => d.ToValue<HotChocolate.Authorization.AuthorizeDirective>())
            .Should()
            .ContainSingle()
            .Which;
        gate.Roles.Should().Equal("Admin");
        output.Fields["value"].Directives.Should().NotContain(d => d.Type.Name == "authorize");
    }

    private static TrainRegistration OutputTrain() =>
        new()
        {
            ServiceType = typeof(IMemberPostureTrain),
            ImplementationType = typeof(IMemberPostureTrain),
            InputType = typeof(MemberPostureInput),
            OutputType = typeof(MemberPostureOutput),
            Lifetime = ServiceLifetime.Scoped,
            ServiceTypeName = nameof(IMemberPostureTrain),
            ImplementationTypeName = nameof(IMemberPostureTrain),
            InputTypeName = nameof(MemberPostureInput),
            OutputTypeName = nameof(MemberPostureOutput),
            RequiredPolicies = [],
            RequiredRoles = [],
            HasAuthorizeAttribute = false,
            HasAllowAnonymousAttribute = true,
            IsQuery = true,
            IsMutation = false,
            IsBroadcastEnabled = false,
            IsRemote = false,
            GraphQLOperations = GraphQLOperation.Run,
        };

    public interface IMemberPostureTrain;

    public sealed record MemberPostureInput
    {
        public string Value { get; init; } = "";
    }

    public sealed class MemberPostureOutput
    {
        public string Value { get; init; } = "";

        [TraxAuthorize(Roles = "Admin")]
        public string GetSecret() => "output-secret";
    }

    public interface IExposedNote
    {
        string Title { get; }
    }

    [TraxQueryModel(ExposeAs = typeof(IExposedNote))]
    [TraxAuthorize(Roles = "Admin")]
    public sealed class ExposedNote : IExposedNote
    {
        public int Id { get; set; }

        public string Title { get; set; } = "";

        public string Hidden { get; set; } = "";
    }

    public sealed class ExposedContext(DbContextOptions<ExposedContext> options)
        : DbContext(options)
    {
        public DbSet<ExposedNote> Notes => Set<ExposedNote>();
    }

    public sealed class MemberPostureContext(DbContextOptions<MemberPostureContext> options)
        : DbContext(options)
    {
        public DbSet<MemberPostureNote> Notes => Set<MemberPostureNote>();
    }

    [TraxQueryModel]
    [TraxAllowAnonymous]
    public sealed class MemberPostureNote
    {
        public int Id { get; set; }

        public string Title { get; set; } = "";

        [TraxAuthorize(Roles = "Admin")]
        public string GetSecret() => "member-secret";
    }

    [ExtendObjectType("RootQuery")]
    public sealed class HarmlessRootField
    {
        [TraxAllowAnonymous]
        public string Harmless() => "harmless";
    }
}
