export interface ChatRoomSummary {
  id: string;
  name: string;
  participantCount: number;
  lastMessageAt: string | null;
  unreadCount: number;
}

export interface ChatMessageDto {
  id: string;
  senderUserId: string;
  senderDisplayName: string;
  content: string;
  sentAt: string;
  pending?: boolean;
}

export interface ChatSubscriptionEvent {
  chatRoomId: string;
  eventType: string;
  payload: string;
  timestamp: string;
  trainExternalId: string;
}

export interface User {
  key: string;
  userId: string;
  displayName: string;
}

export const USERS: User[] = [
  { key: "alice-key-do-not-use-in-production", userId: "alice", displayName: "Alice" },
  { key: "bob-key-do-not-use-in-production", userId: "bob", displayName: "Bob" },
  { key: "charlie-key-do-not-use-in-production", userId: "charlie", displayName: "Charlie" },
];
