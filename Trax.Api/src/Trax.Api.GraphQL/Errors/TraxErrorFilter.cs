using HotChocolate;
using Trax.Api.Exceptions;
using Trax.Core.Exceptions;

namespace Trax.Api.GraphQL.Errors;

/// <summary>
/// Replaces HotChocolate's default "Unexpected Execution Error" with the actual
/// exception message for train-related exceptions. Without this filter, all
/// unhandled exceptions are masked for security, which makes debugging impossible.
/// </summary>
/// <remarks>
/// Exposed exception types:
/// <list type="bullet">
/// <item><see cref="TrainException"/> — train execution failures (step errors, remote errors)</item>
/// <item><see cref="TrainAuthorizationException"/> — authorization failures</item>
/// <item><see cref="InvalidOperationException"/> — configuration/input errors (missing train, bad input)</item>
/// </list>
/// All other exception types retain the default "Unexpected Execution Error" message.
/// </remarks>
internal class TraxErrorFilter : IErrorFilter
{
    public IError OnError(IError error)
    {
        if (error.Exception is null)
            return error;

        return error.Exception switch
        {
            TrainAuthorizationException ex => error
                .WithMessage(ex.Message)
                .WithCode("TRAX_AUTHORIZATION"),
            TrainException ex => error.WithMessage(ex.Message).WithCode("TRAX_TRAIN_ERROR"),
            InvalidOperationException ex => error
                .WithMessage(ex.Message)
                .WithCode("TRAX_INVALID_OPERATION"),
            _ => error,
        };
    }
}
