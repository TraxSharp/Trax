import { useState, useEffect, useRef } from "react";
import { useQuery, useMutation, useSubscription } from "@apollo/client";
import { GET_CHAT_HISTORY, GET_CHAT_ROOMS } from "../graphql/queries";
import { SEND_MESSAGE } from "../graphql/mutations";
import { ON_CHAT_EVENT } from "../graphql/subscriptions";
import { useUser } from "../context/UserContext";
import { Message } from "./Message";
import type { ChatMessageDto, ChatRoomSummary } from "../types";
import { Avatar } from "./Avatar";
import { InvitePanel } from "./InvitePanel";

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

  // The room list is already cached by the sidebar; read the room's name and size from it.
  const { data: roomsData, refetch: refetchRooms } = useQuery(GET_CHAT_ROOMS, { fetchPolicy: "cache-first" });
  const room: ChatRoomSummary | undefined = roomsData?.discover?.getChatRooms?.rooms?.find(
    (r: ChatRoomSummary) => r.id === roomId,
  );
  const [copied, setCopied] = useState(false);
  const [inviting, setInviting] = useState(false);
  const copyId = async () => {
    try {
      await navigator.clipboard.writeText(roomId);
      setCopied(true);
      setTimeout(() => setCopied(false), 1500);
    } catch {
      // Clipboard unavailable (insecure context); the id is still selectable in the chip.
    }
  };

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
      if (!event) return;

      // Someone was added or joined: say so in the room, and refresh its member count.
      if (event.eventType === "UserJoined") {
        try {
          const p = JSON.parse(event.payload);
          const who = p.displayName ?? p.DisplayName ?? "Someone";
          const by = p.invitedByDisplayName ?? p.InvitedByDisplayName;
          const line: ChatMessageDto = {
            id: `system-${event.trainExternalId}`,
            senderUserId: "",
            senderDisplayName: "",
            content: by ? `${by} added ${who}` : `${who} joined`,
            sentAt: event.timestamp,
            system: true,
          };
          setMessages((prev) => (prev.some((m) => m.id === line.id) ? prev : [...prev, line]));
        } catch {
          // Ignore malformed payloads
        }
        refetchRooms();
        return;
      }
      if (event.eventType !== "MessageSent") return;

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
        input: { chatRoomId: roomId, content },
      },
    });
  };

  return (
    <div className="chat-room">
      <header className="chat-room-header">
        <Avatar name={room?.name ?? "Room"} size="lg" />
        <div className="chat-room-title">
          <h2>{room?.name ?? "Chat room"}</h2>
          <span className="chat-room-meta">
            {room ? `${room.participantCount} member${room.participantCount !== 1 ? "s" : ""}` : "Loading…"}
            <span className="live">
              <span className="live-dot" /> Live
            </span>
          </span>
        </div>
        <div className="invite-anchor">
          <button className={`invite-button ${inviting ? "on" : ""}`} onClick={() => setInviting(!inviting)}>
            + Invite
          </button>
          {inviting && <InvitePanel roomId={roomId} onClose={() => setInviting(false)} />}
        </div>
        <button className="chat-room-id" onClick={copyId} title="Copy the room ID, so someone else can join">
          <span className="chat-room-id-label">{copied ? "Copied" : "Room ID"}</span>
          <code>{roomId}</code>
        </button>
      </header>

      <div className="chat-messages">
        {loading && <div className="chat-loading">Loading messages…</div>}
        {!loading && messages.length === 0 && (
          <div className="chat-empty">No messages yet. Say hello.</div>
        )}
        {messages.map((msg, i) => (
          <Message
            key={msg.id}
            message={msg}
            isOwn={msg.senderUserId === user.userId}
            showSender={i === 0 || messages[i - 1].system || messages[i - 1].senderUserId !== msg.senderUserId}
          />
        ))}
        <div ref={messagesEndRef} />
      </div>

      <form className="chat-input" onSubmit={handleSend}>
        <input
          type="text"
          placeholder={`Message ${room?.name ?? "the room"} as ${user.displayName}`}
          value={draft}
          onChange={(e) => setDraft(e.target.value)}
          autoFocus
        />
        <button type="submit" disabled={!draft.trim()} aria-label="Send">
          Send
        </button>
      </form>
    </div>
  );
}
