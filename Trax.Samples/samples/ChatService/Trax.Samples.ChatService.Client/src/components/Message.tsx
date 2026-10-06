import type { ChatMessageDto } from "../types";
import { Avatar } from "./Avatar";

interface MessageProps {
  message: ChatMessageDto;
  isOwn: boolean;
  /** False when the previous message has the same sender, so the name and avatar are not repeated. */
  showSender: boolean;
}

export function Message({ message, isOwn, showSender }: MessageProps) {
  if (message.system) return <div className="message-system">{message.content}</div>;

  const time = new Date(message.sentAt).toLocaleTimeString([], {
    hour: "2-digit",
    minute: "2-digit",
  });

  const classes = [
    "message",
    isOwn ? "message-own" : "message-other",
    message.pending ? "message-pending" : "",
    showSender ? "message-first" : "",
  ]
    .filter(Boolean)
    .join(" ");

  return (
    <div className={classes}>
      {!isOwn && <div className="message-avatar">{showSender && <Avatar name={message.senderDisplayName} />}</div>}
      <div className="message-body">
        {showSender && (
          <div className="message-meta">
            <span className="message-sender">{isOwn ? "You" : message.senderDisplayName}</span>
            <span className="message-time">{time}</span>
          </div>
        )}
        <div className="message-bubble" title={time}>
          {message.content}
        </div>
        {message.pending && <div className="message-status">Sending…</div>}
      </div>
    </div>
  );
}
