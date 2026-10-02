namespace Trax.Core.Exceptions;

/// <summary>
/// Reads the failure class an exception already carries, for every place Trax records a failure:
/// a junction, and a decision.
/// </summary>
internal static class FailureClassification
{
    /// <summary>
    /// The classification an exception already carries, from wherever it was first recorded.
    /// </summary>
    /// <remarks>
    /// A failure that reached this junction from somewhere that classified it, such as a remote
    /// worker or a nested train, keeps that answer. The data attached here replaces whatever the
    /// exception carried before, so without this the class would be dropped on the way through.
    ///
    /// <para>Only an exception whose type is exactly <see cref="TrainException"/> carries a class
    /// in its message, because that is the type Trax rebuilds a recorded failure as. A type
    /// derived from it is treated like any other exception. Any other exception's message is its own
    /// text, and is never read as a record. A value outside <see cref="FailureClass"/> is carried
    /// as <see cref="FailureClass.Unclassified"/>, as the remote wire already does.</para>
    /// </remarks>
    internal static FailureClass? Carried(Exception e)
    {
        if (e.Data["TrainExceptionData"] is TrainExceptionData attached)
            return Defined(attached.FailureClass);

        // Exactly TrainException: a consumer subclass carries its own text (often a remote
        // system's response body), and reading a class out of it would let that text choose
        // whether the failure is retried.
        if (e.GetType() != typeof(TrainException) || !e.Message.StartsWith('{'))
            return null;

        try
        {
            return Defined(
                System
                    .Text.Json.JsonSerializer.Deserialize<TrainExceptionData>(e.Message)
                    ?.FailureClass
            );
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }

    private static FailureClass? Defined(FailureClass? failureClass) =>
        failureClass is { } value && !Enum.IsDefined(value)
            ? FailureClass.Unclassified
            : failureClass;
}
