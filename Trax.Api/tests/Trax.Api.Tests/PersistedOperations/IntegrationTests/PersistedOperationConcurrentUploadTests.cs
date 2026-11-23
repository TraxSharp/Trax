using System.Globalization;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Trax.Api.GraphQL.PersistedOperations.GraphQL.Models;
using Trax.Api.GraphQL.PersistedOperations.Services;
using Trax.Api.GraphQL.PersistedOperations.Storage;
using Trax.Api.Tests.PersistedOperations.Fixtures;
using Trax.Core.Extensions;

namespace Trax.Api.Tests.PersistedOperations.IntegrationTests;

/// <summary>
/// Uploads of one id are applied one at a time: each is checked against the row the previous one
/// left, and a refusal is a payload error with a code, never a thrown database error.
/// </summary>
[TestFixture]
[Category("Integration")]
public class PersistedOperationConcurrentUploadTests
{
    private const int Rounds = 20;

    private ServiceProvider? _sp;
    private IPersistedOperationsService? _service;
    private IPersistedOperationStore? _store;

    [OneTimeSetUp]
    public async Task OneTimeSetUp()
    {
        if (!PostgresFixture.IsPostgresReachable())
            Assert.Ignore("Postgres not reachable.");

        _sp = await GraphQLFixture.BuildAsync();
        _service = _sp.NotNull().GetRequiredService<IPersistedOperationsService>();
        _store = _sp.NotNull().GetRequiredService<IPersistedOperationStore>();
    }

    [OneTimeTearDown]
    public async Task OneTimeTearDown()
    {
        if (_sp is not null)
            if (_sp is not null)
                await _sp.DisposeAsync();
    }

    [SetUp]
    public Task SetUp() => PostgresFixture.ClearAsync();

    [Test]
    public async Task TwoFirstUploadsOfOneId_OneIsSaved_AndTheOtherIsRefusedWithACode()
    {
        for (var round = 0; round < Rounds; round++)
        {
            var id = string.Create(
                CultureInfo.InvariantCulture,
                $"race_first_{round}_{Guid.NewGuid():N}"
            );

            var results = await Task.WhenAll(
                UploadAsync(id, "query Greet { hello }"),
                UploadAsync(id, "query Greet { version }")
            );

            results
                .Count(r => r.Success)
                .Should()
                .Be(1, string.Create(CultureInfo.InvariantCulture, $"round {round}"));
            results
                .Single(r => !r.Success)
                .Errors.Single()
                .Code.Should()
                .Be(
                    ShapeDiffViolationException.CodeValue,
                    string.Create(CultureInfo.InvariantCulture, $"round {round}")
                );
        }
    }

    [Test]
    public async Task AnUploadRacingABypassingOne_IsCheckedAgainstWhatTheBypassSaved()
    {
        for (var round = 0; round < Rounds; round++)
        {
            var id = string.Create(
                CultureInfo.InvariantCulture,
                $"race_bypass_{round}_{Guid.NewGuid():N}"
            );
            await _store
                .NotNull()
                .UpsertAsync(id, "query Greet { hello }", options: null, CancellationToken.None);

            await Task.WhenAll(
                UploadAsync(id, "query Greet { version }", bypassShapeDiff: true),
                UploadAsync(id, "query Greet { hello # edited\n}")
            );

            // Either the edit landed first and the bypass replaced it, or the bypass landed first
            // and the edit, a shape change against it, was refused. Never the edit last.
            (await _store.NotNull().GetAsync(id, tenantKey: null, CancellationToken.None))
                .NotNull()
                .Document.Should()
                .Be(
                    "query Greet { version }",
                    string.Create(CultureInfo.InvariantCulture, $"round {round}")
                );
        }
    }

    [Test]
    public async Task TwoIdenticalFirstUploads_BothSucceed()
    {
        var id = $"race_same_{Guid.NewGuid():N}";

        var results = await Task.WhenAll(
            UploadAsync(id, "query Greet { hello }"),
            UploadAsync(id, "query Greet { hello }")
        );

        results.Should().OnlyContain(r => r.Success);
    }

    private Task<UploadPersistedOperationPayload> UploadAsync(
        string id,
        string document,
        bool bypassShapeDiff = false
    ) =>
        Task.Run(() =>
            _service
                .NotNull()
                .UploadAsync(
                    new UploadPersistedOperationInput(
                        id,
                        document,
                        BypassShapeDiff: bypassShapeDiff
                    ),
                    CancellationToken.None
                )
        );
}
