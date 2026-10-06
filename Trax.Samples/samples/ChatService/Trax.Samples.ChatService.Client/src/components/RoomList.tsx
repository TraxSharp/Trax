import { useEffect, useRef, useState } from "react";
import { useQuery } from "@apollo/client";
import { GET_CHAT_ROOMS } from "../graphql/queries";
import { CreateRoomDialog } from "./CreateRoomDialog";
import type { ChatRoomSummary } from "../types";
import { Avatar } from "./Avatar";

interface RoomListProps {
  selectedRoomId: string | null;
  onSelectRoom: (roomId: string) => void;
}

export function RoomList({ selectedRoomId, onSelectRoom }: RoomListProps) {
  const [showCreate, setShowCreate] = useState(false);
  // The server lists the caller's own rooms: the query takes no input.
  const { data, loading } = useQuery(GET_CHAT_ROOMS, {
    pollInterval: 5000,
  });

  const rooms: ChatRoomSummary[] =
    data?.discover?.getChatRooms?.rooms ?? [];

  // Rooms that show up after the list first loads (someone added you) are marked new until opened.
  const known = useRef<Set<string> | null>(null);
  const [fresh, setFresh] = useState<Set<string>>(new Set());
  useEffect(() => {
    if (!data) return;
    const ids = rooms.map((r) => r.id);
    if (known.current === null) {
      known.current = new Set(ids);
      return;
    }
    const added = ids.filter((id) => !known.current!.has(id));
    if (added.length === 0) return;
    added.forEach((id) => known.current!.add(id));
    // A room you just created opens straight away; it is not news to you.
    const news = added.filter((id) => id !== selectedRoomId);
    if (news.length > 0) setFresh((prev) => new Set([...prev, ...news]));
  }, [data, rooms, selectedRoomId]);

  const open = (roomId: string) => {
    setFresh((prev) => {
      const next = new Set(prev);
      next.delete(roomId);
      return next;
    });
    onSelectRoom(roomId);
  };

  return (
    <div className="room-list">
      <div className="group">
        <div className="room-list-header">
          <span className="group-label">Your rooms</span>
          <button className="ghost" onClick={() => setShowCreate(true)}>
            + New room
          </button>
        </div>

        {loading && rooms.length === 0 && <div className="room-list-empty">Loading…</div>}

        <div className="rooms">
          {rooms.map((room) => (
            <button
              key={room.id}
              className={`room-item ${room.id === selectedRoomId ? "room-item-selected" : ""}`}
              onClick={() => open(room.id)}
            >
              <Avatar name={room.name} />
              <span className="room-item-text">
                <span className="room-item-name">
                  {room.name}
                  {fresh.has(room.id) && <span className="new-badge">New</span>}
                </span>
                <span className="room-item-meta">
                  {room.participantCount} member{room.participantCount !== 1 && "s"}
                </span>
              </span>
            </button>
          ))}
        </div>

        {!loading && rooms.length === 0 && (
          <div className="room-list-empty">No rooms yet. Create one, or ask someone to add you to theirs.</div>
        )}
      </div>

      {showCreate && <CreateRoomDialog onClose={() => setShowCreate(false)} onCreated={onSelectRoom} />}
    </div>
  );
}
