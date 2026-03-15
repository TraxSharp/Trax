import { gql } from "@apollo/client";

export const CREATE_CHAT_ROOM = gql`
  mutation CreateChatRoom($input: CreateChatRoomInput!) {
    dispatch {
      createChatRoom(input: $input) {
        externalId
        output {
          chatRoomId
          name
          createdAt
        }
      }
    }
  }
`;

export const JOIN_CHAT_ROOM = gql`
  mutation JoinChatRoom($input: JoinChatRoomInput!) {
    dispatch {
      joinChatRoom(input: $input) {
        externalId
        output {
          chatRoomId
          userId
          displayName
          joinedAt
        }
      }
    }
  }
`;

export const SEND_MESSAGE = gql`
  mutation SendMessage($input: SendMessageInput!) {
    dispatch {
      sendMessage(input: $input) {
        externalId
        output {
          messageId
          chatRoomId
          senderUserId
          senderDisplayName
          content
          sentAt
        }
      }
    }
  }
`;
