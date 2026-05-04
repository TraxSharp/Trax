using FluentAssertions;
using LanguageExt;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Core.Exceptions;
using Trax.Core.Junction;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Enums;
using Trax.Effect.Extensions;
using Trax.Effect.Services.ServiceTrain;
using Trax.Effect.Tests.Data.InMemory.Integration.Fixtures;

namespace Trax.Effect.Tests.Data.InMemory.Integration.IntegrationTests;

/// <summary>
/// End-to-end tests verifying that exception context from the full
/// Junction → Monad → ServiceTrain → Metadata → Database pipeline
/// produces correct, readable failure fields for the dashboard, API, and broadcaster.
/// </summary>
public class ExceptionPersistenceTests : TestSetup
{
    public override ServiceProvider ConfigureServices(IServiceCollection services) =>
        services
            .AddScopedTraxRoute<IJunctionFailingTrain, JunctionFailingTrain>()
            .AddScopedTraxRoute<IHttpErrorTrain, HttpErrorTrain>()
            .AddScopedTraxRoute<IJsonMessageTrain, JsonMessageTrain>()
            .AddScopedTraxRoute<ISpecialCharsTrain, SpecialCharsTrain>()
            .AddScopedTraxRoute<IMultiJunctionTrain, MultiJunctionTrain>()
            .AddScopedTraxRoute<IPlainExceptionTrain, PlainExceptionTrain>()
            .BuildServiceProvider();

    #region Junction Exception → Metadata Fields

    [Test]
    public async Task JunctionFails_FailureFieldsPopulatedCorrectly()
    {
        var train = Scope.ServiceProvider.GetRequiredService<IJunctionFailingTrain>();

        var act = async () => await train.Run("input");
        await act.Should().ThrowAsync<InvalidOperationException>();

        train.Metadata.Should().NotBeNull();
        train.Metadata!.TrainState.Should().Be(TrainState.Failed);
        train.Metadata.FailureException.Should().Be("InvalidOperationException");
        train.Metadata.FailureReason.Should().Be("junction failure");
        train.Metadata.FailureJunction.Should().Be(nameof(AlwaysFailsJunction));
    }

    [Test]
    public async Task JunctionFails_StackTraceContainsOriginalThrowSite()
    {
        var train = Scope.ServiceProvider.GetRequiredService<IJunctionFailingTrain>();

        var act = async () => await train.Run("input");
        await act.Should().ThrowAsync<InvalidOperationException>();

        train.Metadata.Should().NotBeNull();
        train.Metadata!.StackTrace.Should().NotBeNullOrEmpty();
        train.Metadata.StackTrace.Should().Contain(nameof(AlwaysFailsJunction));
    }

    [Test]
    public async Task JunctionFails_OriginalExceptionTypePreserved()
    {
        var train = Scope.ServiceProvider.GetRequiredService<IJunctionFailingTrain>();

        // The exception thrown to the caller should be the ORIGINAL type
        var act = async () => await train.Run("input");
        var ex = (await act.Should().ThrowAsync<InvalidOperationException>()).Which;

        // The message should be the ORIGINAL human-readable message, not JSON
        ex.Message.Should().Be("junction failure");
    }

    #endregion

    #region HTTP Error Exception (simulating real-world scenario)

    [Test]
    public async Task HttpError_FailureReasonContainsOriginalMessage()
    {
        var train = Scope.ServiceProvider.GetRequiredService<IHttpErrorTrain>();

        var act = async () => await train.Run("input");
        await act.Should().ThrowAsync<HttpRequestException>();

        train.Metadata.Should().NotBeNull();
        train.Metadata!.FailureException.Should().Be("HttpRequestException");
        train
            .Metadata.FailureReason.Should()
            .Be("Response status code does not indicate success: 500 (Internal Server Error).");
        train.Metadata.FailureJunction.Should().Be(nameof(HttpCallJunction));
        train.Metadata.StackTrace.Should().Contain(nameof(HttpCallJunction));
    }

    [Test]
    public async Task HttpError_ExceptionThrownToCallerIsOriginal()
    {
        var train = Scope.ServiceProvider.GetRequiredService<IHttpErrorTrain>();

        var act = async () => await train.Run("input");
        var ex = (await act.Should().ThrowAsync<HttpRequestException>()).Which;

        // Must be the original message, NOT a JSON blob
        ex.Message.Should()
            .Be("Response status code does not indicate success: 500 (Internal Server Error).");
    }

    #endregion

    #region JSON Content in Exception Message

    [Test]
    public async Task JsonInExceptionMessage_FailureReasonPreservesOriginalJson()
    {
        var train = Scope.ServiceProvider.GetRequiredService<IJsonMessageTrain>();

        var act = async () => await train.Run("input");
        await act.Should().ThrowAsync<InvalidOperationException>();

        train.Metadata.Should().NotBeNull();

        // The FailureReason must be the original JSON string, not double-encoded
        train.Metadata!.FailureReason.Should().Contain("\"success\":false");
        train.Metadata.FailureReason.Should().Contain("\"referenceId\":\"ref-123\"");
        train.Metadata.FailureJunction.Should().Be(nameof(JsonExceptionJunction));
    }

    [Test]
    public async Task JsonInExceptionMessage_MessageNotCorruptedByTraxPipeline()
    {
        var train = Scope.ServiceProvider.GetRequiredService<IJsonMessageTrain>();

        var act = async () => await train.Run("input");
        var ex = (await act.Should().ThrowAsync<InvalidOperationException>()).Which;

        // The original JSON message must be intact — not wrapped or mutated
        ex.Message.Should().Contain("\"success\":false");
    }

    #endregion

    #region Special Characters in Exception Message

    [Test]
    public async Task SpecialCharsInMessage_FailureReasonPreserved()
    {
        var train = Scope.ServiceProvider.GetRequiredService<ISpecialCharsTrain>();

        var act = async () => await train.Run("input");
        await act.Should().ThrowAsync<InvalidOperationException>();

        train.Metadata.Should().NotBeNull();
        train.Metadata!.FailureReason.Should().Contain("newlines");
        train.Metadata.FailureReason.Should().Contain("tabs");
        train.Metadata.FailureReason.Should().Contain("quotes");
        train.Metadata.FailureReason.Should().Contain("backslashes");
    }

    #endregion

    #region Multi-Junction Train (failure in second junction)

    [Test]
    public async Task MultiJunction_SecondJunctionFails_CorrectJunctionIdentified()
    {
        var train = Scope.ServiceProvider.GetRequiredService<IMultiJunctionTrain>();

        var act = async () => await train.Run("input");
        await act.Should().ThrowAsync<InvalidOperationException>();

        train.Metadata.Should().NotBeNull();
        train.Metadata!.FailureJunction.Should().Be(nameof(SecondJunctionFails));
        train.Metadata.FailureReason.Should().Be("second junction failed");
        train.Metadata.StackTrace.Should().Contain(nameof(SecondJunctionFails));
    }

    #endregion

    #region Plain Exception (not from junction — RunInternal override)

    [Test]
    public async Task PlainException_NotFromJunction_StillPersistsFailureFields()
    {
        var train = Scope.ServiceProvider.GetRequiredService<IPlainExceptionTrain>();

        var act = async () => await train.Run(Unit.Default);
        await act.Should().ThrowAsync<ArgumentException>();

        train.Metadata.Should().NotBeNull();
        train.Metadata!.TrainState.Should().Be(TrainState.Failed);
        train.Metadata.FailureException.Should().Be("ArgumentException");
        train.Metadata.FailureReason.Should().Be("plain failure");
        // No junction context — falls back to "TrainException" sentinel
        train.Metadata.FailureJunction.Should().Be("TrainException");
    }

    #endregion

    #region Database Persistence Round-Trip

    [Test]
    public async Task JunctionFails_FailureFieldsPersistedToDatabase()
    {
        var train = Scope.ServiceProvider.GetRequiredService<IJunctionFailingTrain>();

        var act = async () => await train.Run("input");
        await act.Should().ThrowAsync<InvalidOperationException>();

        // Read back from the database (not from the in-memory train object)
        var dataContext = Scope.ServiceProvider.GetRequiredService<IDataContext>();
        var persisted = await dataContext.Metadatas.FirstOrDefaultAsync(m =>
            m.Id == train.Metadata!.Id
        );

        persisted.Should().NotBeNull();
        persisted!.TrainState.Should().Be(TrainState.Failed);
        persisted.FailureException.Should().Be("InvalidOperationException");
        persisted.FailureReason.Should().Be("junction failure");
        persisted.FailureJunction.Should().Be(nameof(AlwaysFailsJunction));
        persisted.StackTrace.Should().Contain(nameof(AlwaysFailsJunction));
    }

    [Test]
    public async Task HttpError_FailureFieldsPersistedToDatabase()
    {
        var train = Scope.ServiceProvider.GetRequiredService<IHttpErrorTrain>();

        var act = async () => await train.Run("input");
        await act.Should().ThrowAsync<HttpRequestException>();

        var dataContext = Scope.ServiceProvider.GetRequiredService<IDataContext>();
        var persisted = await dataContext.Metadatas.FirstOrDefaultAsync(m =>
            m.Id == train.Metadata!.Id
        );

        persisted.Should().NotBeNull();
        persisted!.FailureException.Should().Be("HttpRequestException");
        persisted
            .FailureReason.Should()
            .Be("Response status code does not indicate success: 500 (Internal Server Error).");
        persisted.FailureJunction.Should().Be(nameof(HttpCallJunction));
        persisted.StackTrace.Should().NotBeNullOrEmpty();
    }

    [Test]
    public async Task JsonInMessage_FailureFieldsPersistedToDatabase()
    {
        var train = Scope.ServiceProvider.GetRequiredService<IJsonMessageTrain>();

        var act = async () => await train.Run("input");
        await act.Should().ThrowAsync<InvalidOperationException>();

        var dataContext = Scope.ServiceProvider.GetRequiredService<IDataContext>();
        var persisted = await dataContext.Metadatas.FirstOrDefaultAsync(m =>
            m.Id == train.Metadata!.Id
        );

        persisted.Should().NotBeNull();
        persisted!.FailureReason.Should().Contain("\"success\":false");
        persisted.FailureReason.Should().Contain("\"referenceId\":\"ref-123\"");
    }

    #endregion

    #region Dashboard / API Compatibility

    [Test]
    public async Task JunctionFails_FailureFieldsAreAllPlainStrings()
    {
        // The dashboard reads these fields as plain strings (ExceptionViewer.razor).
        // The GraphQL API exposes FailureJunction and FailureReason as string scalars.
        // None of these should be JSON or require further parsing.

        var train = Scope.ServiceProvider.GetRequiredService<IJunctionFailingTrain>();

        var act = async () => await train.Run("input");
        await act.Should().ThrowAsync<InvalidOperationException>();

        // FailureException should be a simple type name, not a full namespace or JSON
        train.Metadata!.FailureException.Should().NotContain("{");
        train.Metadata.FailureException.Should().NotContain("\"");

        // FailureJunction should be a simple class name
        train.Metadata.FailureJunction.Should().NotContain("{");
        train.Metadata.FailureJunction.Should().NotContain("\"");

        // FailureReason should be the original human-readable message
        train.Metadata.FailureReason.Should().Be("junction failure");

        // StackTrace should look like a .NET stack trace
        train.Metadata.StackTrace.Should().Contain("at ");
    }

    [Test]
    public async Task HttpError_FailureReasonIsNotJsonBlob()
    {
        // Previously, RailwayJunction mutated the exception message to be a JSON blob.
        // This would cause the dashboard to show raw JSON instead of a readable message.
        // Verify that the FailureReason is the original human-readable message.

        var train = Scope.ServiceProvider.GetRequiredService<IHttpErrorTrain>();

        var act = async () => await train.Run("input");
        await act.Should().ThrowAsync<HttpRequestException>();

        // The failure reason must NOT start with { — that would indicate a JSON blob
        train.Metadata!.FailureReason.Should().NotStartWith("{");
        train.Metadata.FailureReason.Should().StartWith("Response status code");
    }

    #endregion

    #region Junctions

    private class AlwaysFailsJunction : Junction<string, string>
    {
        public override Task<string> Run(string input) =>
            throw new InvalidOperationException("junction failure");
    }

    private class HttpCallJunction : Junction<string, string>
    {
        public override Task<string> Run(string input) =>
            throw new HttpRequestException(
                "Response status code does not indicate success: 500 (Internal Server Error)."
            );
    }

    private class JsonExceptionJunction : Junction<string, string>
    {
        public override Task<string> Run(string input) =>
            throw new InvalidOperationException(
                """{"success":false,"referenceId":"ref-123","error":"payment declined"}"""
            );
    }

    private class SpecialCharsJunction : Junction<string, string>
    {
        public override Task<string> Run(string input) =>
            throw new InvalidOperationException(
                "Message with\nnewlines,\ttabs, \"quotes\", and \\backslashes"
            );
    }

    private class FirstJunctionSucceeds : Junction<string, string>
    {
        public override Task<string> Run(string input) => Task.FromResult(input + "-processed");
    }

    private class SecondJunctionFails : Junction<string, string>
    {
        public override Task<string> Run(string input) =>
            throw new InvalidOperationException("second junction failed");
    }

    #endregion

    #region Trains

    private class JunctionFailingTrain : ServiceTrain<string, string>, IJunctionFailingTrain
    {
        protected override Task<Either<Exception, string>> Junctions() =>
            Chain<AlwaysFailsJunction>().Resolve();
    }

    private interface IJunctionFailingTrain : IServiceTrain<string, string> { }

    private class HttpErrorTrain : ServiceTrain<string, string>, IHttpErrorTrain
    {
        protected override Task<Either<Exception, string>> Junctions() =>
            Chain<HttpCallJunction>().Resolve();
    }

    private interface IHttpErrorTrain : IServiceTrain<string, string> { }

    private class JsonMessageTrain : ServiceTrain<string, string>, IJsonMessageTrain
    {
        protected override Task<Either<Exception, string>> Junctions() =>
            Chain<JsonExceptionJunction>().Resolve();
    }

    private interface IJsonMessageTrain : IServiceTrain<string, string> { }

    private class SpecialCharsTrain : ServiceTrain<string, string>, ISpecialCharsTrain
    {
        protected override Task<Either<Exception, string>> Junctions() =>
            Chain<SpecialCharsJunction>().Resolve();
    }

    private interface ISpecialCharsTrain : IServiceTrain<string, string> { }

    private class MultiJunctionTrain : ServiceTrain<string, string>, IMultiJunctionTrain
    {
        protected override Task<Either<Exception, string>> Junctions() =>
            Chain<FirstJunctionSucceeds>().Chain<SecondJunctionFails>().Resolve();
    }

    private interface IMultiJunctionTrain : IServiceTrain<string, string> { }

    private class PlainExceptionTrain : ServiceTrain<Unit, Unit>, IPlainExceptionTrain
    {
        protected override Task<Either<Exception, Unit>> RunInternal(Unit input) =>
            Task.FromResult<Either<Exception, Unit>>(new ArgumentException("plain failure"));
    }

    private interface IPlainExceptionTrain : IServiceTrain<Unit, Unit> { }

    #endregion
}
