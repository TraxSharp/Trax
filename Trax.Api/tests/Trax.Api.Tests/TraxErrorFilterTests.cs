using FluentAssertions;
using HotChocolate;
using Trax.Api.Exceptions;
using Trax.Api.GraphQL.Errors;
using Trax.Core.Exceptions;
using Trax.Mediator.Exceptions;

namespace Trax.Api.Tests;

[TestFixture]
public class TraxErrorFilterTests
{
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
    public void OnError_TrainExceptionWithJsonMessage_PreservesFullMessage()
    {
        var json =
            """{"trainName":"My.Train","trainExternalId":"ext-1","type":"ArgumentException","junction":"Validate","message":"Bad input"}""";
        var ex = new TrainException(json);
        var error = CreateError(ex);

        var result = _filter.OnError(error);

        result.Message.Should().Be(json);
        result.Code.Should().Be("TRAX_TRAIN_ERROR");
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
