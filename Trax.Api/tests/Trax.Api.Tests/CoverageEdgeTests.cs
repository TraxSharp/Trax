using FluentAssertions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Trax.Api.Auth.Jwt;
using Trax.Api.GraphQL.Audit;
using Trax.Api.GraphQL.Types;

namespace Trax.Api.Tests;

[TestFixture]
public class CoverageEdgeTests
{
    #region JwtBuilder.CustomizeBearerOptions

    [Test]
    public void JwtBuilder_CustomizeBearerOptions_Stores_Customizer()
    {
        var builder = new JwtBuilder();
        builder.UseSymmetricKey("issuer", "audience", new byte[32]);

        Action<JwtBearerOptions> hook = _ => { };
        var returned = builder.CustomizeBearerOptions(hook);

        returned.Should().BeSameAs(builder);
        builder.BearerOptionsCustomizer.Should().BeSameAs(hook);
    }

    [Test]
    public void JwtBuilder_CustomizeBearerOptions_Null_Throws()
    {
        var builder = new JwtBuilder();

        Action act = () => builder.CustomizeBearerOptions(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    #endregion

    #region TraxAuditDisclaimerHostedService

    [Test]
    public async Task TraxAuditDisclaimerHostedService_Start_LogsAndReturns()
    {
        // The class is internal — instantiate via reflection.
        var t = typeof(TraxAuditChannel).Assembly.GetType(
            "Trax.Api.GraphQL.Audit.TraxAuditDisclaimerHostedService"
        );
        t.Should().NotBeNull();

        var loggerFactory = NullLoggerFactory.Instance;
        var instance = (Microsoft.Extensions.Hosting.IHostedService)
            Activator.CreateInstance(t!, loggerFactory)!;

        await instance.StartAsync(CancellationToken.None);
        await instance.StopAsync(CancellationToken.None);
    }

    #endregion

    #region JsonElementConverter — exercise every value kind

    [Test]
    public void JsonElementConverter_ToObject_Covers_AllValueKinds()
    {
        var json = """
            {
              "obj": {"x": 1},
              "arr": [1, 2, 3],
              "s": "hello",
              "i": 42,
              "d": 3.14,
              "longBig": 9007199254740993,
              "t": true,
              "f": false,
              "n": null
            }
            """;

        var result = JsonElementConverter.ToObject(json);
        var dict = (System.Collections.Generic.Dictionary<string, object?>)result!;

        dict["obj"].Should().BeOfType<System.Collections.Generic.Dictionary<string, object?>>();
        dict["arr"].Should().BeOfType<System.Collections.Generic.List<object?>>();
        dict["s"].Should().Be("hello");
        dict["i"].Should().Be(42L);
        dict["d"].Should().Be(3.14);
        dict["longBig"].Should().Be(9007199254740993L);
        dict["t"].Should().Be(true);
        dict["f"].Should().Be(false);
        dict["n"].Should().BeNull();
    }

    #endregion
}
