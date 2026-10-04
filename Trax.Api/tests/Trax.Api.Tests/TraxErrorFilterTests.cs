using AwesomeAssertions;
using HotChocolate;
using Trax.Api.Exceptions;
using Trax.Api.GraphQL.Errors;
using Trax.Core.Exceptions;
using Trax.Effect.Exceptions;
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
        )
        {
            // The runner offers a train author's own message, and only that (central 0028).
            PublicMessage = "Order 42 is already closed.",
        }.ToTrainException();

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
        // What HttpRunExecutor throws for a non-success status: the body is the worker's (or a
        // proxy's) response, whatever it contains.
        var ex = new RemoteRunException(
            "Remote run endpoint returned HTTP 502: <html>upstream 10.0.3.7:8080 refused</html>"
        );

        var result = _filter.OnError(CreateError(ex));

        result.Message.Should().Be(TraxErrorFilter.TrainFailedMessage, Adr);
        result.Code.Should().Be("TRAX_TRAIN_ERROR");
    }

    [Test]
    public void OnError_LambdaFunctionError_ReturnsTheGenericMessage()
    {
        // What LambdaRunExecutor throws for a FunctionError: the payload is the function's raw
        // error document, never written for a client.
        var ex = new RemoteRunException(
            "Lambda function 'trax-runner' returned error: Unhandled. "
                + """{"errorType":"NpgsqlException","errorMessage":"Failed to connect to 10.0.3.7:5432"}"""
        );

        var result = _filter.OnError(CreateError(ex));

        result.Message.Should().Be(TraxErrorFilter.TrainFailedMessage, Adr);
        result.Message.Should().NotContain("10.0.3.7").And.NotContain("trax-runner");
    }

    [Test]
    public void OnError_RemoteRunFailure_FromARunnerThatOffersNoPublicMessage_ReturnsTheGenericMessage()
    {
        // A runner that predates the public message sends none, so even a TrainException it ran
        // reads as a failed train (central 0028's stated consequence).
        var remote = new RemoteRunResponse(
            MetadataId: 7,
            IsError: true,
            ErrorMessage: "Order 42 is already closed.",
            ExceptionType: nameof(TrainException)
        ).ToTrainException();

        var result = _filter.OnError(CreateError(remote));

        result.Message.Should().Be(TraxErrorFilter.TrainFailedMessage, Adr);
    }

    [Test]
    public void OnError_PlainTrainException_WhoseMessageStartsLikeATransportFailure_PassesThrough()
    {
        // The filter reads the exception's type, not its wording: only a RemoteRunException is a
        // remote failure, so an author's message is never masked for what it happens to say.
        var ex = new TrainException("Remote run endpoint returned nothing for order 42.");

        var result = _filter.OnError(CreateError(ex));

        result.Message.Should().Be("Remote run endpoint returned nothing for order 42.");
    }

    [TestCase("{ order 42 is not closed yet }")]
    [TestCase("""{"trainName":"My.Train","message":"no type"}""")]
    [TestCase(
        """{"trainName":"My.Train","trainExternalId":"ext-1","type":"","junction":"Close","message":"m"}"""
    )]
    public void OnError_TrainExceptionStartingWithABraceButCarryingNothing_PassesThrough(
        string message
    )
    {
        // Only a message that parses as carried exception data with a type is read as one; any
        // other message is the author's, whatever its first character.
        var result = _filter.OnError(CreateError(new TrainException(message)));

        result.Message.Should().Be(message);
    }

    [Test]
    public void OnError_CarriedTrainExceptionWithNoMessage_ReturnsTheGenericMessage()
    {
        var json =
            """{"trainName":"My.Train","trainExternalId":"ext-1","type":"TrainException","junction":"Close","message":""}""";

        var result = _filter.OnError(CreateError(new TrainException(json)));

        result.Message.Should().Be(TraxErrorFilter.TrainFailedMessage, Adr);
    }

    [Test]
    public void OnError_TrainExceptionSubclass_ReturnsTheGenericMessage()
    {
        // Only an exact TrainException's message is a train author's, the same rule the
        // operations queueTrain and runTrain refusals apply.
        var ex = new ConsumerTrainException("Ledger 7 is locked by batch 0x2f on db-primary.");

        var result = _filter.OnError(CreateError(ex));

        result.Message.Should().Be(TraxErrorFilter.TrainFailedMessage, Adr);
        result.Code.Should().Be("TRAX_TRAIN_ERROR");
    }

    [Test]
    public void OnError_TrainAlreadyStartedException_ReturnsTheGenericMessage()
    {
        var ex = new TrainAlreadyStartedException(4242, "Contoso.Billing.ICloseLedgerTrain");

        var result = _filter.OnError(CreateError(ex));

        result.Message.Should().Be(TraxErrorFilter.TrainFailedMessage, Adr);
        result.Message.Should().NotContain("4242").And.NotContain("Contoso");
    }

    #endregion

    #region Exception detail

    private static IEnumerable<TestCaseData> MappedExceptions()
    {
        yield return new TestCaseData(new TrainException("Order 42 is closed.")).SetArgDisplayNames(
            nameof(TrainException)
        );
        yield return new TestCaseData(
            new RemoteRunException("Remote run endpoint returned HTTP 502")
        ).SetArgDisplayNames(nameof(RemoteRunException));
        yield return new TestCaseData(
            new ConsumerTrainException("internal detail")
        ).SetArgDisplayNames(nameof(ConsumerTrainException));
        yield return new TestCaseData(
            new TrainAuthorizationException("My.Train", "Missing role: Admin")
        ).SetArgDisplayNames(nameof(TrainAuthorizationException));
        yield return new TestCaseData(new TrainNotFoundException("My.Train")).SetArgDisplayNames(
            nameof(TrainNotFoundException)
        );
        yield return new TestCaseData(
            new AmbiguousTrainNameException("IMyTrain", ["Ns.A.IMyTrain", "Ns.B.IMyTrain"])
        ).SetArgDisplayNames(nameof(AmbiguousTrainNameException));
        yield return new TestCaseData(
            new TrainInputValidationException("IMyTrain", 2048, 1024)
        ).SetArgDisplayNames(nameof(TrainInputValidationException));
        yield return new TestCaseData(
            new NoTrainForInputException(typeof(TraxErrorFilterTests), ["Contoso.Trains"])
        ).SetArgDisplayNames(nameof(NoTrainForInputException));
    }

    [TestCaseSource(nameof(MappedExceptions))]
    public void OnError_MappedException_IsDetachedFromTheError(Exception ex)
    {
        var result = _filter.OnError(CreateError(ex));

        result
            .Exception.Should()
            .BeNull(
                "HotChocolate writes an attached exception's message and stack trace into the "
                    + "response when exception details are on, after the filters have run"
            );
    }

    [Test]
    public void OnError_UnmappedException_KeepsTheExceptionForHotChocolatesOwnHandling()
    {
        var ex = new InvalidOperationException("internal");

        var result = _filter.OnError(CreateError(ex));

        result.Exception.Should().BeSameAs(ex);
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

    #region NoTrainForInputException

    [Test]
    public void OnError_NoTrainForInputException_ReturnsGenericMessageWithHostConfigurationCode()
    {
        var ex = new NoTrainForInputException(typeof(TraxErrorFilterTests), ["Contoso.Trains"]);

        var result = _filter.OnError(CreateError(ex));

        result.Message.Should().Be(TraxErrorFilter.TrainNotRunMessage);
        result.Message.Should().NotContain("Contoso").And.NotContain(nameof(TraxErrorFilterTests));
        result.Code.Should().Be("TRAX_HOST_CONFIGURATION");
        result
            .Exception.Should()
            .BeNull(
                "an attached exception is written to the response when exception details are on"
            );
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

    private sealed class ConsumerTrainException(string message) : TrainException(message);
}
