import { gql } from "@apollo/client";

export const GET_CHAT_ROOMS = gql`
  query GetChatRooms($input: GetChatRoomsInput!) {
    discover {
      getChatRooms(input: $input) {
        rooms {
          id
          name
          participantCount
          lastMessageAt
          unreadCount
        }
      }
    }
  }
`;

export const GET_CHAT_HISTORY = gql`
  query GetChatHistory($input: GetChatHistoryInput!) {
    discover {
      getChatHistory(input: $input) {
        messages {
          id
          senderUserId
          senderDisplayName
          content
          sentAt
        }
      }
    }
  }
`;
