import { useState, useEffect } from "react";
import { useUser } from "../context/UserContext";
import { UserSelector } from "./UserSelector";
import { RoomList } from "./RoomList";
import { ChatRoom } from "./ChatRoom";

export function App() {
  const { user } = useUser();
  const [selectedRoomId, setSelectedRoomId] = useState<string | null>(null);

  // Clear room selection when switching users
  useEffect(() => {
    setSelectedRoomId(null);
  }, [user.userId]);

  return (
    <div className="app">
      <aside className="sidebar">
        <h1 className="app-title">Trax Chat</h1>
        <UserSelector />
        <RoomList
          selectedRoomId={selectedRoomId}
          onSelectRoom={setSelectedRoomId}
        />
      </aside>
      <main className="main">
        {selectedRoomId ? (
          <ChatRoom
            key={`${selectedRoomId}-${user.userId}`}
            roomId={selectedRoomId}
          />
        ) : (
          <div className="no-room">Select or create a room to start chatting.</div>
        )}
      </main>
    </div>
  );
}
