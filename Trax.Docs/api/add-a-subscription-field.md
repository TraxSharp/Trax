---
title: Add a subscription field
description: "Add your own GraphQL subscription field to the Trax schema, authorize who may listen to which topic, and publish to it from a lifecycle hook."
type: how-to
area: api
nav_order: 21
---

# Add a subscription field

The lifecycle fields stream train events. A domain feed of your own, such as a chat room's
messages, is a field you add to the same root and feed from a lifecycle hook. The
[Chat Service sample](/docs/samples/chat-service) is a complete one.

## Add the field

Trax's subscription root is named `LifecycleSubscriptions`, so a type extension targets that name:

```csharp compile=field
using HotChocolate;
using HotChocolate.Execution;
using HotChocolate.Subscriptions;
using HotChocolate.Types;
using Microsoft.EntityFrameworkCore;
using Trax.Api.Auth;
using Trax.Effect.Attributes;

[ExtendObjectType("LifecycleSubscriptions")]
public class ChatSubscriptions
{
    [TraxAuthorize(Roles = "User")]
    [Subscribe(With = nameof(SubscribeToChatEventAsync))]
    public ChatEvent OnChatEvent(Guid chatRoomId, [EventMessage] ChatEvent message) => message;

    public async ValueTask<ISourceStream<ChatEvent>> SubscribeToChatEventAsync(
        Guid chatRoomId,
        TraxCaller caller,
        ChatDbContext db,
        ITopicEventReceiver receiver,
        CancellationToken cancellationToken)
    {
        var userId = caller.Principal?.Id;
        if (userId is null || !await db.Participants.AnyAsync(
                p => p.RoomId == chatRoomId && p.UserId == userId, cancellationToken))
            throw new GraphQLException(ErrorBuilder.New()
                .SetMessage("Not authorized.").SetCode("TRAX_AUTHORIZATION").Build());

        return await receiver.SubscribeAsync<ChatEvent>($"ChatRoom:{chatRoomId}", cancellationToken);
    }
}
```

Register the extension on the GraphQL builder:

```csharp compile=field
using Trax.Api.GraphQL.Extensions;

builder.Services.AddTraxGraphQL(graphql => graphql.AddTypeExtension<ChatSubscriptions>());
```

- **Target `"LifecycleSubscriptions"`, not `OperationTypeNames.Subscription`.** The schema has no
  type named `Subscription`, so the host refuses to start, naming the extension class and
  `LifecycleSubscriptions` as the root it meant. HotChocolate on its own would drop the extension
  without an error; Trax refuses any class-declared extension whose target type is not in the
  schema.
- **Declare a posture on the field.** A field on a root type inherits no gate, so the host refuses
  to start when the field carries neither `[TraxAuthorize]` nor `[TraxAllowAnonymous]` (see
  [Fields Added by a Type Extension](/docs/authorization#fields-added-by-a-type-extension)).
- **Check the record in the subscribe resolver.** `[TraxAuthorize]` decides who may use the field;
  which topic a caller may listen to (their own room, their own orders) is the resolver's job. It
  runs once, when the client subscribes, and a refusal reaches the client as an `error` message for
  that subscription. The method named by `Subscribe(With = ...)` is not exposed as a field. A
  resolver parameter of a service type, such as `TraxCaller` or a `DbContext`, is resolved from the
  container.

## Publish to it

Send to the topic from an [`ITrainLifecycleHook`](/docs/sdk-reference/configuration/add-lifecycle-hook)
when the train whose result the feed carries completes:

```csharp compile=hook
using System.Text.Json;
using HotChocolate.Subscriptions;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Services.TrainLifecycleHook;
using Trax.Effect.Services.TrainLifecycleHookFactory;

public class ChatLifecycleHook(ITopicEventSender eventSender) : ITrainLifecycleHook
{
    public async Task OnCompleted(Metadata metadata, CancellationToken ct)
    {
        if (metadata.Name != typeof(ISendMessageTrain).FullName || metadata.Output is null)
            return;

        using var doc = JsonDocument.Parse(metadata.Output);
        var roomId = doc.RootElement.GetProperty("chatRoomId").GetGuid();
        await eventSender.SendAsync($"ChatRoom:{roomId}", new ChatEvent(roomId, metadata.Output), ct);
    }
}

public class ChatLifecycleHookFactory(IServiceProvider services) : ITrainLifecycleHookFactory
{
    public ITrainLifecycleHook Create() => ActivatorUtilities.CreateInstance<ChatLifecycleHook>(services);
}
```

Register the hook with the effects:

```csharp compile=hook
using Trax.Effect.Data.Sqlite.Extensions;
using Trax.Effect.Extensions;
using Trax.Effect.Provider.Json.Extensions;
using Trax.Mediator.Extensions;

builder.Services.AddTrax(trax => trax
    .AddEffects(effects => effects.UseSqlite(connectionString).AddJson().AddLifecycleHook<ChatLifecycleHookFactory>())
    .AddMediator(typeof(ChatLifecycleHook).Assembly));
```

`metadata.Name` is the train's canonical name, its service interface's `FullName`.
`metadata.Output` is the output serialized as camelCase JSON, and it begins with a `"$id"`
property; read the fields you need rather than forwarding it as your schema. A hook fires for every
train, so it filters by name itself; `[TraxBroadcast]` on the train is not needed for your own hook,
only for the lifecycle fields. Events your code sends through `ITopicEventSender` carry no
`sequence` and never cause a skip in the lifecycle fields' numbering.

The in-memory transport reaches subscribers on the process that sent the event. A hook that runs on
a worker process reaches no API node's subscribers; bridge it the way the lifecycle fields are
bridged, with [`UseBroadcaster()`](/docs/sdk-reference/configuration/use-broadcaster), or keep the
train on the API node.

## SDK Reference

> [Subscriptions](/docs/sdk-reference/graphql-api/subscriptions) | [AddTraxGraphQL](/docs/sdk-reference/graphql-api/add-trax-graphql) | [AddLifecycleHook](/docs/sdk-reference/configuration/add-lifecycle-hook) | [TraxAuthorize](/docs/sdk-reference/attributes/trax-authorize)
