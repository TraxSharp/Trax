using System.Text;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using Trax.Api.Auth.Jwt;

namespace Trax.Api.Tests.Auth;

/// <summary>
/// A symmetric JWT signing key carrying the <c>do-not-use-in-production</c> marker the Trax
/// templates and samples put on their demo keys starts only in Development, like a marked API
/// key.
/// </summary>
[TestFixture]
public class DemoJwtKeyEnvironmentTests
{
    private const string DemoKey = "trax-sample-signing-key-do-not-use-in-production";

    [TestCase("Production")]
    [TestCase("Staging")]
    public async Task A_marked_symmetric_key_refuses_to_start_outside_Development(string env)
    {
        var act = () => StartAsync(env, jwt => jwt.UseSymmetricKey("iss", "aud", Bytes(DemoKey)));

        (
            await act.Should()
                .ThrowAsync<InvalidOperationException>()
                .WithMessage("*do-not-use-in-production*Development*")
        )
            .Which.Message.Should()
            .NotContain("trax-sample-signing-key");
    }

    [Test]
    public async Task A_marked_key_passed_as_a_SecurityKey_refuses_to_start_outside_Development()
    {
        var act = () =>
            StartAsync(
                Environments.Production,
                jwt =>
                    jwt.UseSigningKey(
                        "iss",
                        "aud",
                        new SymmetricSecurityKey(Bytes(DemoKey.ToUpperInvariant()))
                    )
            );

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Test]
    public async Task A_marked_key_on_a_second_scheme_refuses_to_start_outside_Development()
    {
        var act = () =>
            StartAsync(
                Environments.Production,
                jwt =>
                    jwt.UseSymmetricKey("iss", "aud", Bytes("a-real-key-from-a-secret-store-0123")),
                jwt => jwt.UseSymmetricKey("iss2", "aud2", Bytes(DemoKey))
            );

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Test]
    public async Task A_marked_key_set_through_CustomizeTokenValidation_refuses_to_start_outside_Development()
    {
        var act = () =>
            StartAsync(
                Environments.Production,
                jwt =>
                    jwt.UseSymmetricKey("iss", "aud", Bytes("a-real-key-from-a-secret-store-0123"))
                        .CustomizeTokenValidation(tvp =>
                            tvp.IssuerSigningKey = new SymmetricSecurityKey(Bytes(DemoKey))
                        )
            );

        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage($"*'{JwtDefaults.SchemeName}'*");
    }

    [Test]
    public async Task A_marked_key_among_IssuerSigningKeys_refuses_to_start_outside_Development()
    {
        var act = () =>
            StartAsync(
                Environments.Production,
                jwt =>
                    jwt.UseSymmetricKey("iss", "aud", Bytes("a-real-key-from-a-secret-store-0123"))
                        .CustomizeTokenValidation(tvp =>
                            tvp.IssuerSigningKeys = [
                                new SymmetricSecurityKey(
                                    Bytes("another-real-key-0123456789abcdef")
                                ),
                                new SymmetricSecurityKey(Bytes(DemoKey)),
                            ]
                        )
            );

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Test]
    public async Task A_marked_key_set_through_CustomizeBearerOptions_refuses_to_start_outside_Development()
    {
        var act = () =>
            StartAsync(
                Environments.Production,
                jwt =>
                    jwt.UseSymmetricKey("iss", "aud", Bytes("a-real-key-from-a-secret-store-0123"))
                        .CustomizeBearerOptions(options =>
                            options.TokenValidationParameters.IssuerSigningKey = new JsonWebKey
                            {
                                Kty = JsonWebAlgorithmsKeyTypes.Octet,
                                K = Base64UrlEncoder.Encode(Bytes(DemoKey)),
                            }
                        )
            );

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Test]
    public async Task A_marked_key_set_through_CustomizeTokenValidation_starts_in_Development()
    {
        var act = () =>
            StartAsync(
                Environments.Development,
                jwt =>
                    jwt.UseSymmetricKey("iss", "aud", Bytes("a-real-key-from-a-secret-store-0123"))
                        .CustomizeTokenValidation(tvp =>
                            tvp.IssuerSigningKey = new SymmetricSecurityKey(Bytes(DemoKey))
                        )
            );

        await act.Should().NotThrowAsync();
    }

    [Test]
    public async Task A_marked_key_starts_in_Development()
    {
        var act = () =>
            StartAsync(
                Environments.Development,
                jwt => jwt.UseSymmetricKey("iss", "aud", Bytes(DemoKey))
            );

        await act.Should().NotThrowAsync();
    }

    [TestCase("Production")]
    [TestCase("Development")]
    public async Task An_unmarked_key_starts_in_every_environment(string env)
    {
        var act = () =>
            StartAsync(
                env,
                jwt =>
                    jwt.UseSymmetricKey("iss", "aud", Bytes("a-real-key-from-a-secret-store-0123"))
            );

        await act.Should().NotThrowAsync();
    }

    private static byte[] Bytes(string s) => Encoding.UTF8.GetBytes(s);

    private static async Task StartAsync(string environment, params Action<JwtBuilder>[] schemes)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IHostEnvironment>(new Environment(environment));
        services.AddLogging();
        for (var i = 0; i < schemes.Length; i++)
            services.AddTraxJwtAuth(i == 0 ? JwtDefaults.SchemeName : $"scheme{i}", schemes[i]);
        await using var provider = services.BuildServiceProvider();

        foreach (var hosted in provider.GetServices<IHostedService>())
            await hosted.StartAsync(CancellationToken.None);
    }

    private sealed class Environment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
