using System.Data.Common;
using AwesomeAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Configuration.Aws;
using Trax.Effect.Configuration.Deploy;
using Trax.Effect.Data.InMemory.Extensions;
using Trax.Effect.Data.Services.DeployMigration;
using Trax.Effect.Extensions;

namespace Trax.Effect.Tests.Integration.UnitTests.Configuration;

/// <summary>
/// <c>AddTraxAwsSecretsAsync</c> reads each secret <c>trax deploy</c> lists into the host's configuration: a whole
/// secret into a key, one property of a JSON secret, or a connection string's password, quoted so a <c>;</c> in it
/// cannot cut the string. It reports itself in the deploy manifest, and refuses an entry it cannot satisfy by name.
/// </summary>
[TestFixture]
public class TraxAwsSecretsTests
{
    private const string Rds = """{ "username": "trax", "password": "p;a'ss\"w=rd" }""";

    private static ConfigurationManager Configuration(params (string Key, string Value)[] values)
    {
        var configuration = new ConfigurationManager();
        configuration.AddInMemoryCollection(
            values.Select(v => new KeyValuePair<string, string?>(v.Key, v.Value))
        );
        return configuration;
    }

    private static Task<string?> Read(string id, CancellationToken ct) =>
        Task.FromResult<string?>(
            id switch
            {
                "orders-staging/app/Stripe__ApiKey" => "sk_test",
                "arn:aws:secretsmanager:us-east-1:1:secret:rds" => Rds,
                "not-json" => "plain",
                _ => throw new InvalidOperationException($"secret {id} not found"),
            }
        );

    [Test]
    public async Task A_secret_is_read_into_its_key()
    {
        var configuration = Configuration(
            ("Trax:AwsSecrets:0:SecretId", "orders-staging/app/Stripe__ApiKey"),
            ("Trax:AwsSecrets:0:Key", "Stripe__ApiKey")
        );

        await TraxAwsSecretsExtensions.AddAsync(configuration, Read, CancellationToken.None);

        configuration["Stripe:ApiKey"].Should().Be("sk_test");
    }

    [Test]
    public async Task A_database_password_is_set_on_its_connection_string_quoted()
    {
        var configuration = Configuration(
            (
                "ConnectionStrings:Trax",
                "Host=db.example.com;Database=orders;Username=trax;SSL Mode=VerifyFull"
            ),
            ("Trax:AwsSecrets:0:SecretId", "arn:aws:secretsmanager:us-east-1:1:secret:rds"),
            ("Trax:AwsSecrets:0:JsonKey", "password"),
            ("Trax:AwsSecrets:0:PasswordOf", "Trax")
        );

        await TraxAwsSecretsExtensions.AddAsync(configuration, Read, CancellationToken.None);

        var parsed = new DbConnectionStringBuilder
        {
            ConnectionString = configuration.GetConnectionString("Trax"),
        };
        parsed["Password"].Should().Be("p;a'ss\"w=rd");
        parsed["Host"].Should().Be("db.example.com");
        parsed["SSL Mode"].Should().Be("VerifyFull");
    }

    [Test]
    public async Task One_secret_read_by_two_entries_is_fetched_once()
    {
        var reads = new List<string>();
        var configuration = Configuration(
            ("ConnectionStrings:Trax", "Host=db"),
            ("Trax:AwsSecrets:0:SecretId", "arn:aws:secretsmanager:us-east-1:1:secret:rds"),
            ("Trax:AwsSecrets:0:JsonKey", "password"),
            ("Trax:AwsSecrets:0:PasswordOf", "Trax"),
            ("Trax:AwsSecrets:1:SecretId", "arn:aws:secretsmanager:us-east-1:1:secret:rds"),
            ("Trax:AwsSecrets:1:JsonKey", "username"),
            ("Trax:AwsSecrets:1:Key", "Db:User")
        );

        await TraxAwsSecretsExtensions.AddAsync(
            configuration,
            (id, ct) =>
            {
                reads.Add(id);
                return Read(id, ct);
            },
            CancellationToken.None
        );

        reads.Should().ContainSingle();
        configuration["Db:User"].Should().Be("trax");
    }

    [TestCase("SecretId", "", "*Trax:AwsSecrets:0 names no SecretId*")]
    [TestCase("Key", null, "*Trax:AwsSecrets:0 names neither Key nor PasswordOf*")]
    [TestCase("JsonKey", "token", "*reads token from secret not-json, which is not JSON*")]
    [TestCase(
        "PasswordOf",
        "Missing",
        "*sets the password of ConnectionStrings:Missing, which is not set*"
    )]
    public async Task An_entry_it_cannot_satisfy_is_refused_by_name(
        string field,
        string? value,
        string message
    )
    {
        var entry = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["SecretId"] = "not-json",
            ["Key"] = "X",
        };
        entry[field] = value;
        if (string.Equals(field, "PasswordOf", StringComparison.Ordinal))
            entry.Remove("Key");
        var configuration = Configuration([
            .. entry
                .Where(e => e.Value is not null)
                .Select(e => ($"Trax:AwsSecrets:0:{e.Key}", e.Value ?? "")),
        ]);

        var act = () =>
            TraxAwsSecretsExtensions.AddAsync(configuration, Read, CancellationToken.None);

        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage(message);
    }

    [Test]
    public async Task The_host_reports_it_reads_its_secrets_from_aws()
    {
        var configuration = Configuration();
        await TraxAwsSecretsExtensions.AddAsync(configuration, Read, CancellationToken.None);
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddTrax(trax => trax.AddEffects(effects => effects.UseInMemory()));
        var provider = services.BuildServiceProvider();
        await using var providerDisposal = provider.ConfigureAwait(false);

        var manifest = await DeployManifestExport.BuildAsync(
            provider,
            host: null,
            CancellationToken.None
        );

        manifest.SecretSources.Should().Equal(TraxAwsSecretsExtensions.SourceName);
        DeployManifest.Read(manifest.Write()).SecretSources.Should().Equal("aws-secrets");
    }

    [Test]
    public async Task A_host_without_a_secrets_reader_reports_none()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(Configuration());
        services.AddTrax(trax => trax.AddEffects(effects => effects.UseInMemory()));
        var provider = services.BuildServiceProvider();
        await using var providerDisposal = provider.ConfigureAwait(false);

        var manifest = await DeployManifestExport.BuildAsync(
            provider,
            host: null,
            CancellationToken.None
        );

        manifest.SecretSources.Should().BeEmpty();
    }
}
