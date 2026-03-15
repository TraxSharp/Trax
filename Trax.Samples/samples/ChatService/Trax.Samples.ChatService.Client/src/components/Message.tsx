import type { ChatMessageDto } from "../types";

interface MessageProps {
  message: ChatMessageDto;
  isOwn: boolean;
}

export function Message({ message, isOwn }: MessageProps) {
  const time = new Date(message.sentAt).toLocaleTimeString([], {
    hour: "2-digit",
    minute: "2-digit",
  });

  const classes = [
    "message",
    isOwn ? "message-own" : "message-other",
    message.pending ? "message-pending" : "",
  ]
    .filter(Boolean)
    .join(" ");

  return (
    <div className={classes}>
      <div className="message-meta">
        <span className="message-sender">{message.senderDisplayName}</span>
        <span className="message-time">{time}</span>
      </div>
      <div className="message-bubble">{message.content}</div>
    </div>
  );
}
