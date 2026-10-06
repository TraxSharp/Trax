import { useState, useEffect } from "react";
import { useUser } from "../context/UserContext";
import { UserSelector } from "./UserSelector";
import { RoomList } from "./RoomList";
import { ChatRoom } from "./ChatRoom";
import { Inspector } from "./Inspector";

export function App() {
  const { user } = useUser();
  const [selectedRoomId, setSelectedRoomId] = useState<string | null>(null);
  const [inspecting, setInspecting] = useState(() => {
    try {
      return localStorage.getItem("trax-chat-inspector") !== "closed";
    } catch {
      return true;
    }
  });
  const toggleInspector = (on: boolean) => {
    setInspecting(on);
    try {
      localStorage.setItem("trax-chat-inspector", on ? "open" : "closed");
    } catch {
      // Storage unavailable: the choice lasts for this page only.
    }
  };

  // Clear room selection when switching users
  useEffect(() => {
    setSelectedRoomId(null);
  }, [user.userId]);

  return (
    <div className={inspecting ? "app app-inspecting" : "app"}>
      <aside className="sidebar">
        <div className="intro">
          <span className="eyebrow">
            <span className="mark">T</span>
            Trax · Chat
          </span>
          <h1>Live chat on Trax trains</h1>
          <p className="subtitle">
            Every message is a train. When one completes, a lifecycle hook publishes it to the room, and
            each participant's GraphQL subscription receives it.
          </p>
          <button className={`hood-toggle ${inspecting ? "on" : ""}`} onClick={() => toggleInspector(!inspecting)}>
            <span className="hood-icon">{"</>"}</span>
            {inspecting ? "Hide what's under the hood" : "Show what's under the hood"}
          </button>
        </div>
        <UserSelector />
        <RoomList selectedRoomId={selectedRoomId} onSelectRoom={setSelectedRoomId} />
      </aside>
      <main className="main">
        {selectedRoomId ? (
          <ChatRoom key={`${selectedRoomId}-${user.userId}`} roomId={selectedRoomId} />
        ) : (
          <div className="no-room">
            <div className="no-room-card">
              <div className="no-room-icon">💬</div>
              <h2>Pick a room, or start one</h2>
              <p>
                Create a room, press <strong>+ Invite</strong> and add Bob. Open a second tab as Bob: the
                room is already in his list.
              </p>
            </div>
          </div>
        )}
      </main>
      {inspecting && <Inspector onClose={() => toggleInspector(false)} />}
    </div>
  );
}
