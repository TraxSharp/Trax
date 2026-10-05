export interface ChatRoomSummary {
  id: string;
  name: string;
  participantCount: number;
  lastMessageAt: string | null;
}

export interface ChatMessageDto {
  id: string;
  senderUserId: string;
  senderDisplayName: string;
  content: string;
  sentAt: string;
  pending?: boolean;
  /** A line the room shows about itself, such as someone being added, rather than a message. */
  system?: boolean;
}

export interface ChatRoomPerson {
  userId: string;
  displayName: string;
  isMember: boolean;
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
  // The id the server stores for this caller: Trax qualifies it with the scheme that
  // authenticated the key, so "alice" arrives as "TraxApiKey:alice".
  userId: string;
  displayName: string;
}

export const USERS: User[] = [
  { key: "alice-key-do-not-use-in-production", userId: "TraxApiKey:alice", displayName: "Alice" },
  { key: "bob-key-do-not-use-in-production", userId: "TraxApiKey:bob", displayName: "Bob" },
  { key: "charlie-key-do-not-use-in-production", userId: "TraxApiKey:charlie", displayName: "Charlie" },
];
