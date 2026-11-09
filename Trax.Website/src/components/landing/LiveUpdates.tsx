import ChatServiceReplay from "./ChatServiceReplay";
import DemoSection from "./DemoSection";

// The ChatService sample, replayed from its recording along one path: two messages, Charlie refused, Charlie added,
// and a message he now receives.
export default function LiveUpdates() {
  return (
    <DemoSection
      title="Changes reach the people allowed to see them"
      intro={
        <p>
            Each message is a workflow, and when it finishes the server pushes it to everyone subscribed to the room.
            Someone outside the room is refused until they are added.
        </p>
      }
      links={[
        { href: "/docs/samples/chat-service", label: "Read the sample" },
        { href: "/docs/security/secure-a-graphql-host", label: "Securing a GraphQL host" },
      ]}
      source={{
        folder: "ChatService",
        files: [
          { path: "Trax.Samples.ChatService/Trains/SendMessage/SendMessageTrain.cs", label: "the workflow each message runs" },
          { path: "Trax.Samples.ChatService/Hooks/ChatLifecycleHook.cs", label: "publishes each finished message to the room's topic" },
          { path: "Trax.Samples.ChatService/Subscriptions/ChatSubscriptions.cs", label: "onChatEvent, admitting only the room's participants" },
          { path: "Trax.Samples.ChatService.Api/Program.cs", label: "API keys, the hook and the subscription wired up" },
        ],
        docs: "/docs/samples/chat-service",
        run: [
          "dotnet run --project samples/ChatService/Trax.Samples.ChatService.Api",
          "cd samples/ChatService/Trax.Samples.ChatService.Client && npm ci && npm run dev",
        ],
      }}
    >
      <ChatServiceReplay />
    </DemoSection>
  );
}
