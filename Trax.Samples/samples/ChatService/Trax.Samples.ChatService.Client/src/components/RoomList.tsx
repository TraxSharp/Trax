import { useState } from "react";
import { useQuery, useMutation } from "@apollo/client";
import { GET_CHAT_ROOMS } from "../graphql/queries";
import { JOIN_CHAT_ROOM } from "../graphql/mutations";
import { useUser } from "../context/UserContext";
import { CreateRoomDialog } from "./CreateRoomDialog";
import type { ChatRoomSummary } from "../types";

interface RoomListProps {
  selectedRoomId: string | null;
  onSelectRoom: (roomId: string) => void;
}

export function RoomList({ selectedRoomId, onSelectRoom }: RoomListProps) {
  const { user } = useUser();
  const [showCreate, setShowCreate] = useState(false);
  const [joinRoomId, setJoinRoomId] = useState<string | null>(null);

  const { data, loading } = useQuery(GET_CHAT_ROOMS, {
    variables: { input: { userId: user.userId } },
    pollInterval: 5000,
  });

  const [joinRoom] = useMutation(JOIN_CHAT_ROOM, {
    refetchQueries: [GET_CHAT_ROOMS],
  });

  const rooms: ChatRoomSummary[] =
    data?.discover?.getChatRooms?.rooms ?? [];

  const handleJoin = async (roomId: string) => {
    setJoinRoomId(roomId);
    await joinRoom({
      variables: {
        input: {
          chatRoomId: roomId,
          userId: user.userId,
          displayName: user.displayName,
        },
      },
    });
    setJoinRoomId(null);
    onSelectRoom(roomId);
  };

  return (
    <div className="room-list">
      <div className="room-list-header">
        <h2>Rooms</h2>
        <button onClick={() => setShowCreate(true)}>+ New</button>
      </div>

      {loading && rooms.length === 0 && (
        <div className="room-list-empty">Loading...</div>
      )}

      {rooms.map((room) => (
        <div
          key={room.id}
          className={`room-item ${room.id === selectedRoomId ? "room-item-selected" : ""}`}
          onClick={() => onSelectRoom(room.id)}
        >
          <div className="room-item-name">{room.name}</div>
          <div className="room-item-meta">
            {room.participantCount} member{room.participantCount !== 1 && "s"}
            {room.unreadCount > 0 && (
              <span className="room-item-unread">{room.unreadCount}</span>
            )}
          </div>
        </div>
      ))}

      {!loading && rooms.length === 0 && (
        <div className="room-list-empty">
          No rooms yet. Create one or ask another user to invite you.
        </div>
      )}

      <div className="room-list-join">
        <h3>Join a Room</h3>
        <p className="room-list-join-hint">
          Paste a room ID to join an existing room.
        </p>
        <form
          onSubmit={(e) => {
            e.preventDefault();
            const input = e.currentTarget.elements.namedItem(
              "roomId",
            ) as HTMLInputElement;
            if (input.value.trim()) handleJoin(input.value.trim());
          }}
        >
          <input
            name="roomId"
            type="text"
            placeholder="Room ID"
            disabled={joinRoomId !== null}
          />
          <button type="submit" disabled={joinRoomId !== null}>
            Join
          </button>
        </form>
      </div>

      {showCreate && (
        <CreateRoomDialog
          onClose={() => setShowCreate(false)}
          onCreated={onSelectRoom}
        />
      )}
    </div>
  );
}
