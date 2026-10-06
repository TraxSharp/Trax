using HotChocolate;
using HotChocolate.Execution;
using HotChocolate.Subscriptions;
using HotChocolate.Types;
using Microsoft.EntityFrameworkCore;
using Trax.Api.Auth;
using Trax.Effect.Attributes;
using Trax.Samples.ChatService.Auth;
using Trax.Samples.ChatService.Data;

namespace Trax.Samples.ChatService.Subscriptions;

/// <summary>
/// Adds <c>onChatEvent(chatRoomId:)</c> to the schema's subscription root. Trax names that root
/// <c>LifecycleSubscriptions</c> (it carries Trax's own <c>onTrainCompleted</c> and siblings), so
/// the extension targets that name: an extension of a type that does not exist, such as
/// <c>OperationTypeNames.Subscription</c>, is dropped by HotChocolate without an error.
/// </summary>
/// <remarks>
/// Two checks guard the field. <c>[TraxAuthorize]</c> is its posture: a field added to a root type
/// inherits no gate, so Trax refuses to start a host whose root extension declares none, and the
/// directive refuses a caller without the role. The subscribe resolver then admits only a
/// participant of the room, so knowing a room id is not enough to listen to it.
/// </remarks>
[ExtendObjectType("LifecycleSubscriptions")]
public class ChatSubscriptions
{
    /// <summary>The topic a room's events are published on; <c>ChatLifecycleHook</c> sends to it.</summary>
    public static string Topic(Guid chatRoomId) => $"ChatRoom:{chatRoomId}";

    [TraxAuthorize(Roles = nameof(ChatRole.User))]
    [Subscribe(With = nameof(SubscribeToChatEventAsync))]
    public ChatSubscriptionEvent OnChatEvent(
        Guid chatRoomId,
        [EventMessage] ChatSubscriptionEvent message
    ) => message;

    /// <summary>
    /// Opens the room's event stream for a participant, and refuses anyone else when they subscribe.
    /// </summary>
    public async ValueTask<ISourceStream<ChatSubscriptionEvent>> SubscribeToChatEventAsync(
        Guid chatRoomId,
        TraxCaller caller,
        ChatDbContext db,
        ITopicEventReceiver receiver,
        CancellationToken cancellationToken
    )
    {
        var userId = caller.Principal?.Id;
        var isParticipant =
            userId is not null
            && await db.ChatParticipants.AnyAsync(
                p => p.ChatRoomId == chatRoomId && p.UserId == userId,
                cancellationToken
            );

        if (!isParticipant)
            throw new GraphQLException(
                ErrorBuilder
                    .New()
                    .SetMessage("Not authorized.")
                    .SetCode("TRAX_AUTHORIZATION")
                    .Build()
            );

        return await receiver.SubscribeAsync<ChatSubscriptionEvent>(
            Topic(chatRoomId),
            cancellationToken
        );
    }
}
