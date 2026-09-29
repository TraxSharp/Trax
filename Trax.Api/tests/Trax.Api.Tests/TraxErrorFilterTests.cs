using FluentAssertions;
using HotChocolate;
using Trax.Api.Exceptions;
using Trax.Api.GraphQL.Errors;
using Trax.Core.Exceptions;
using Trax.Mediator.Exceptions;
using Trax.Scheduler.Services.RunExecutor;

namespace Trax.Api.Tests;

/// <summary>
/// The public shape of each Trax exception type on the GraphQL surface.
///
/// <para>The TrainException cases enforce
/// <c>docs/adr/0014-only-a-train-exceptions-own-message-reaches-the-client.md</c>.</para>
/// </summary>
[Property("adr", "docs/adr/0014-only-a-train-exceptions-own-message-reaches-the-client.md")]
[TestFixture]
public class TraxErrorFilterTests
{
    private const string Adr =
        "docs/adr/0014-only-a-train-exceptions-own-message-reaches-the-client.md";

    private TraxErrorFilter _filter = null!;

    [SetUp]
    public void SetUp()
    {
        _filter = new TraxErrorFilter();
    }

    #region TrainException

    [Test]
    public void OnError_TrainException_ExposesMessageWithTrainErrorCode()
    {
        var ex = new TrainException("Junction failed: input was invalid");
        var error = CreateError(ex);

        var result = _filter.OnError(error);

        result.Message.Should().Be("Junction failed: input was invalid");
        result.Code.Should().Be("TRAX_TRAIN_ERROR");
    }

    [Test]
    public void OnError_TrainExceptionCarryingAnotherExceptionType_ReturnsTheGenericMessage()
    {
        var json =
            """{"trainName":"My.Train","trainExternalId":"ext-1","type":"ArgumentException","junction":"Validate","message":"Bad input"}""";
        var error = CreateError(new TrainException(json));

        var result = _filter.OnError(error);

        result
            .Message.Should()
            .Be(
                TraxErrorFilter.TrainFailedMessage,
                "only a TrainException's own message is client-safe, per " + Adr
            );
        result.Code.Should().Be("TRAX_TRAIN_ERROR");
    }

    [Test]
    public void OnError_RemoteRunFailure_CarryingADriverException_KeepsItsDetailOut()
    {
        var remote = new RemoteRunResponse(
            MetadataId: 7,
            IsError: true,
            ErrorMessage: "Failed to connect to 10.0.3.7:5432 (db-primary.internal)",
            ExceptionType: "NpgsqlException",
            FailureJunction: "LoadOrdersJunction"
        ).ToTrainException();

        var result = _filter.OnError(CreateError(remote));

        result.Message.Should().Be(TraxErrorFilter.TrainFailedMessage, Adr);
        result.Message.Should().NotContain("10.0.3.7").And.NotContain("db-primary");
        result.Message.Should().NotContain("NpgsqlException").And.NotContain("LoadOrdersJunction");
        result.Code.Should().Be("TRAX_TRAIN_ERROR");
    }

    [Test]
    public void OnError_RemoteRunFailure_CarryingATrainException_ExposesItsMessageOnly()
    {
        var remote = new RemoteRunResponse(
            MetadataId: 7,
            IsError: true,
            ErrorMessage: "Order 42 is already closed.",
            ExceptionType: nameof(TrainException),
            FailureJunction: "CloseOrderJunction"
        ).ToTrainException();

        var result = _filter.OnError(CreateError(remote));

        result
            .Message.Should()
            .Be(
                "Order 42 is already closed.",
                "a TrainException's message is written for the client wherever it ran, per " + Adr
            );
        result.Code.Should().Be("TRAX_TRAIN_ERROR");
    }

    [Test]
    public void OnError_RemoteRunFailure_WithoutAType_ReturnsTheGenericMessage()
    {
        var remote = new RemoteRunResponse(
            MetadataId: 0,
            IsError: true,
            ErrorMessage: "worker at 10.0.3.9 could not resolve the train"
        ).ToTrainException();

        var result = _filter.OnError(CreateError(remote));

        result.Message.Should().Be(TraxErrorFilter.TrainFailedMessage, Adr);
    }

    [Test]
    public void OnError_RemoteEndpointStatusFailure_ReturnsTheGenericMessage()
    {
        // The shape HttpRunExecutor throws for a non-success status: the body is the worker's
        // (or a proxy's) response, whatever it contains.
        var ex = new TrainException(
            "Remote run endpoint returned HTTP 502: <html>upstream 10.0.3.7:8080 refused</html>"
        );

        var result = _filter.OnError(CreateError(ex));

        result.Message.Should().Be(TraxErrorFilter.TrainFailedMessage, Adr);
    }

    #endregion

    #region TrainAuthorizationException

    [Test]
    public void OnError_TrainAuthorizationException_ReturnsGenericMessageNotReason()
    {
        // The filter must never leak the train name, policy, or role that caused
        // the denial. An unauthenticated attacker could otherwise enumerate the
        // full admin surface via error messages alone.
        var ex = new TrainAuthorizationException("My.Internal.AdminTrain", "Missing role: Admin");
        var error = CreateError(ex);

        var result = _filter.OnError(error);

        result.Message.Should().Be("Not authorized.");
        result.Code.Should().Be("TRAX_AUTHORIZATION");
        result.Message.Should().NotContain("My.Internal.AdminTrain");
        result.Message.Should().NotContain("Admin");
        result.Message.Should().NotContain("role");
    }

    [Test]
    public void OnError_TrainAuthorizationException_PolicyNameNotLeaked()
    {
        var ex = new TrainAuthorizationException(
            "Any.Train",
            "Policy 'TopSecretPolicy' not satisfied."
        );
        var error = CreateError(ex);

        var result = _filter.OnError(error);

        result.Message.Should().Be("Not authorized.");
        result.Message.Should().NotContain("TopSecretPolicy");
        result.Message.Should().NotContain("Policy");
    }

    #endregion

    #region TrainNotFoundException

    [Test]
    public void OnError_TrainNotFoundException_ReturnsGenericMessage_NotRequestedName()
    {
        // An attacker probing with arbitrary names must not be able to distinguish
        // "train exists but requires auth" from "train does not exist", or enumerate
        // the registered trains through a "did you mean..." path.
        var ex = new TrainNotFoundException("Probed.Secret.InternalTrain");
        var error = CreateError(ex);

        var result = _filter.OnError(error);

        result.Message.Should().Be("The requested train was not found.");
        result.Code.Should().Be("TRAX_TRAIN_NOT_FOUND");
        result.Message.Should().NotContain("Probed.Secret.InternalTrain");
    }

    #endregion

    #region AmbiguousTrainNameException

    [Test]
    public void OnError_AmbiguousTrainNameException_IncludesCandidateFullNames()
    {
        // Ambiguity is a misconfiguration by a trusted caller who already knows at
        // least one FullName they typed. Surfacing candidates helps them pick the
        // right one. This is a trade-off with enumeration risk, but the caller had
        // to reference a real short name to get here.
        var ex = new AmbiguousTrainNameException("IMyTrain", ["Ns.A.IMyTrain", "Ns.B.IMyTrain"]);
        var error = CreateError(ex);

        var result = _filter.OnError(error);

        result.Message.Should().Contain("ambiguous");
        result.Message.Should().Contain("Ns.A.IMyTrain");
        result.Message.Should().Contain("Ns.B.IMyTrain");
        result.Code.Should().Be("TRAX_AMBIGUOUS_TRAIN");
    }

    #endregion

    #region Masked exceptions

    [Test]
    public void OnError_InvalidOperationException_RetainsDefaultMaskedMessage()
    {
        // Regression: the old filter surfaced InvalidOperationException.Message
        // verbatim. That leaked details like deserialization messages and, in some
        // consumer code paths, stack-trace-shaped strings. We now mask these.
        var ex = new InvalidOperationException("Connection string 'Server=internal;' was bad");
        var error = CreateError(ex, "Unexpected Execution Error");

        var result = _filter.OnError(error);

        result.Message.Should().Be("Unexpected Execution Error");
        result.Message.Should().NotContain("internal");
    }

    [Test]
    public void OnError_UnknownException_RetainsDefaultMessage()
    {
        var ex = new NullReferenceException("Object reference not set");
        var error = CreateError(ex, "Unexpected Execution Error");

        var result = _filter.OnError(error);

        result.Message.Should().Be("Unexpected Execution Error");
    }

    [Test]
    public void OnError_NoException_RetainsOriginalError()
    {
        var error = ErrorBuilder.New().SetMessage("Some GraphQL validation error").Build();

        var result = _filter.OnError(error);

        result.Message.Should().Be("Some GraphQL validation error");
    }

    #endregion

    #region Helpers

    private static IError CreateError(Exception ex, string? message = null)
    {
        return ErrorBuilder
            .New()
            .SetMessage(message ?? "Unexpected Execution Error")
            .SetException(ex)
            .Build();
    }

    #endregion
}
