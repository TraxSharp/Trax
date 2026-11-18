using System.Text.Json.Nodes;
using Trax.Api.Console.Terminal.Checks;
using Trax.Api.Console.Terminal.Operations;
using Trax.Client;

namespace Trax.Api.Console.Terminal.Lists;

/// <summary>What an action did: the host's own words, or why it was refused.</summary>
internal sealed record ActionOutcome(bool Succeeded, string Message, string? Code = null)
{
    public static ActionOutcome Of<T>(TraxAnswer<T> answer, Func<T, (bool Ok, string Message)> read)
    {
        if (!answer.Succeeded)
            return new ActionOutcome(
                Succeeded: false,
                answer.Message ?? "The host refused it.",
                answer.Code
            );
        var (ok, message) = read(answer.Data.NotNull());
        return new ActionOutcome(ok, message);
    }
}
