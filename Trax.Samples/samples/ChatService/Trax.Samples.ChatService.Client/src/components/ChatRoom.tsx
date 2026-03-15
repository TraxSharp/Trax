import { useState, useEffect, useRef } from "react";
import { useQuery, useMutation, useSubscription } from "@apollo/client";
import { GET_CHAT_HISTORY } from "../graphql/queries";
import { SEND_MESSAGE } from "../graphql/mutations";
import { ON_CHAT_EVENT } from "../graphql/subscriptions";
import { useUser } from "../context/UserContext";
import { Message } from "./Message";
import type { ChatMessageDto } from "../types";

interface ChatRoomProps {
  roomId: string;
}

let pendingCounter = 0;

export function ChatRoom({ roomId }: ChatRoomProps) {
  const { user } = useUser();
  const [messages, setMessages] = useState<ChatMessageDto[]>([]);
  const [draft, setDraft] = useState("");
  const messagesEndRef = useRef<HTMLDivElement>(null);

  const { data, loading } = useQuery(GET_CHAT_HISTORY, {
    variables: { input: { chatRoomId: roomId, take: 100 } },
  });

  const [sendMessage] = useMutation(SEND_MESSAGE);

  // Load initial messages from query
  useEffect(() => {
    const fetched: ChatMessageDto[] =
      data?.discover?.getChatHistory?.messages ?? [];
    setMessages(fetched);
  }, [data]);

  // Subscribe to real-time events
  useSubscription(ON_CHAT_EVENT, {
    variables: { chatRoomId: roomId },
    onData: ({ data: subData }) => {
      const event = subData?.data?.onChatEvent;
      if (!event || event.eventType !== "MessageSent") return;

      try {
        const payload = JSON.parse(event.payload);
        const newMsg: ChatMessageDto = {
          id: payload.messageId ?? payload.MessageId ?? event.trainExternalId,
          senderUserId: payload.senderUserId ?? payload.SenderUserId ?? "",
          senderDisplayName:
            payload.senderDisplayName ?? payload.SenderDisplayName ?? "",
          content: payload.content ?? payload.Content ?? "",
          sentAt: payload.sentAt ?? payload.SentAt ?? event.timestamp,
        };

        setMessages((prev) => {
          // Replace pending message with matching content from the same sender
          const pendingIdx = prev.findIndex(
            (m) =>
              m.pending &&
              m.senderUserId === newMsg.senderUserId &&
              m.content === newMsg.content,
          );
          if (pendingIdx !== -1) {
            const updated = [...prev];
            updated[pendingIdx] = newMsg;
            return updated;
          }
          // Otherwise append if not a duplicate
          if (prev.some((m) => m.id === newMsg.id)) return prev;
          return [...prev, newMsg];
        });
      } catch {
        // Ignore malformed payloads
      }
    },
  });

  // Auto-scroll on new messages
  useEffect(() => {
    messagesEndRef.current?.scrollIntoView({ behavior: "smooth" });
  }, [messages]);

  const handleSend = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!draft.trim()) return;

    const content = draft.trim();
    setDraft("");

    // Add optimistic pending message immediately
    const pendingMsg: ChatMessageDto = {
      id: `pending-${++pendingCounter}`,
      senderUserId: user.userId,
      senderDisplayName: user.displayName,
      content,
      sentAt: new Date().toISOString(),
      pending: true,
    };
    setMessages((prev) => [...prev, pendingMsg]);

    await sendMessage({
      variables: {
        input: {
          chatRoomId: roomId,
          senderUserId: user.userId,
          content,
        },
      },
    });
  };

  return (
    <div className="chat-room">
      <div className="chat-room-header">
        <span className="chat-room-id">Room: {roomId}</span>
      </div>

      <div className="chat-messages">
        {loading && <div className="chat-loading">Loading messages...</div>}
        {messages.map((msg) => (
          <Message
            key={msg.id}
            message={msg}
            isOwn={msg.senderUserId === user.userId}
          />
        ))}
        <div ref={messagesEndRef} />
      </div>

      <form className="chat-input" onSubmit={handleSend}>
        <input
          type="text"
          placeholder="Type a message..."
          value={draft}
          onChange={(e) => setDraft(e.target.value)}
          autoFocus
        />
        <button type="submit" disabled={!draft.trim()}>
          Send
        </button>
      </form>
    </div>
  );
}
