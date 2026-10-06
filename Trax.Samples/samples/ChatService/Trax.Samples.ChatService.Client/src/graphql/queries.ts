import { gql } from "@apollo/client";

export const GET_CHAT_ROOMS = gql`
  query GetChatRooms {
    discover {
      getChatRooms {
        rooms {
          id
          name
          participantCount
          lastMessageAt
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

export const GET_CHAT_ROOM_PEOPLE = gql`
  query GetChatRoomPeople($input: GetChatRoomPeopleInput!) {
    discover {
      getChatRoomPeople(input: $input) {
        people {
          userId
          displayName
          isMember
        }
      }
    }
  }
`;
