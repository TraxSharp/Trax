using HotChocolate;
using HotChocolate.Types;

namespace Trax.Samples.ChatService.Subscriptions;

[ExtendObjectType(OperationTypeNames.Subscription)]
public class ChatSubscriptions
{
    [Subscribe]
    [Topic("ChatRoom:{chatRoomId}")]
    public ChatSubscriptionEvent OnChatEvent(
        Guid chatRoomId,
        [EventMessage] ChatSubscriptionEvent message
    ) => message;
}
