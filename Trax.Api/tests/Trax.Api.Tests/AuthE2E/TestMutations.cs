using System.Runtime.CompilerServices;
using HotChocolate;
using HotChocolate.Execution;
using HotChocolate.Subscriptions;
using HotChocolate.Types;
using Microsoft.AspNetCore.Http;
using Trax.Api.Auth;
using Trax.Api.GraphQL.Subscriptions;
using Trax.Effect.Attributes;

namespace Trax.Api.Tests.AuthE2E;

/// <summary>
/// Mutation field that fans out an event to every subscriber of
/// <see cref="TestSubscriptions.TopicName"/>. Used by tests to trigger
/// subscription delivery deterministically.
/// </summary>
[ExtendObjectType("RootMutation")]
public sealed class TestMutations
{
    // Anonymous on purpose: the tests poke this without credentials to fan an event out.
    [TraxAllowAnonymous]
    public async Task<bool> PokeWhoAmI(
        string tag,
        [Service] ITopicEventSender sender,
        System.Threading.CancellationToken ct
    )
    {
        await sender.SendAsync(TestSubscriptions.TopicName, tag, ct).ConfigureAwait(false);
        return true;
    }
}
