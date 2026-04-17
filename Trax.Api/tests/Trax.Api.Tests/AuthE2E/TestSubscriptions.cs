using System.Runtime.CompilerServices;
using HotChocolate;
using HotChocolate.Execution;
using HotChocolate.Subscriptions;
using HotChocolate.Types;
using Microsoft.AspNetCore.Http;
using Trax.Api.Auth;

namespace Trax.Api.Tests.AuthE2E;

/// <summary>
/// Subscription fields used to verify that socket authentication attaches
/// the correct principal to each subscriber's execution context, and that
/// that attachment stays correct across concurrent subscribers with
/// different principals.
///
/// <para>
/// <c>whoAmI</c> subscribes to the test topic. Every time <c>pokeWhoAmI</c>
/// is mutated, the subscription fires and returns the authenticated
/// principal id as observed from the subscriber's own <c>HttpContext</c>.
/// If HC maintained a single shared context across subscribers, concurrent
/// users would see each other's identities; we assert they don't.
/// </para>
/// </summary>
[ExtendObjectType("LifecycleSubscriptions")]
public sealed class TestSubscriptions
{
    public const string TopicName = "trax-auth-e2e-whoami";

    [Subscribe(With = nameof(SubscribeWhoAmIAsync))]
    public string WhoAmI([EventMessage] string _, IHttpContextAccessor httpContextAccessor)
    {
        var principal = httpContextAccessor.HttpContext?.User;
        var id = principal?.FindFirst(TraxAuthClaimTypes.PrincipalId)?.Value;
        return string.IsNullOrEmpty(id) ? "anonymous" : id;
    }

    public async IAsyncEnumerable<string> SubscribeWhoAmIAsync(
        [Service] ITopicEventReceiver receiver,
        [EnumeratorCancellation] CancellationToken ct
    )
    {
        var stream = await receiver.SubscribeAsync<string>(TopicName, ct);
        await foreach (var msg in stream.ReadEventsAsync().WithCancellation(ct))
            yield return msg;
    }
}

/// <summary>
/// Mutation field that fans out an event to every subscriber of
/// <see cref="TestSubscriptions.TopicName"/>. Used by tests to trigger
/// subscription delivery deterministically.
/// </summary>
[ExtendObjectType("RootMutation")]
public sealed class TestMutations
{
    public async Task<bool> PokeWhoAmI(
        string tag,
        [Service] ITopicEventSender sender,
        System.Threading.CancellationToken ct
    )
    {
        await sender.SendAsync(TestSubscriptions.TopicName, tag, ct);
        return true;
    }
}
