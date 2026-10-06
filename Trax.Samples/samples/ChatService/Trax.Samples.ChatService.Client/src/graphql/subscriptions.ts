import { gql } from "@apollo/client";

export const ON_CHAT_EVENT = gql`
  subscription OnChatEvent($chatRoomId: UUID!) {
    onChatEvent(chatRoomId: $chatRoomId) {
      chatRoomId
      eventType
      payload
      timestamp
      trainExternalId
    }
  }
`;
