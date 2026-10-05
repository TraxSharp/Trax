import { useState } from "react";
import { useMutation } from "@apollo/client";
import { CREATE_CHAT_ROOM } from "../graphql/mutations";
import { GET_CHAT_ROOMS } from "../graphql/queries";

interface CreateRoomDialogProps {
  onClose: () => void;
  onCreated: (roomId: string) => void;
}

export function CreateRoomDialog({ onClose, onCreated }: CreateRoomDialogProps) {
  const [name, setName] = useState("");

  const [createRoom, { loading }] = useMutation(CREATE_CHAT_ROOM, {
    refetchQueries: [GET_CHAT_ROOMS],
  });

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!name.trim()) return;

    const result = await createRoom({
      variables: {
        input: { name: name.trim() },
      },
    });

    const roomId = result.data?.dispatch?.createChatRoom?.output?.chatRoomId;
    if (roomId) onCreated(roomId);
    onClose();
  };

  return (
    <div className="dialog-overlay" onClick={onClose}>
      <form
        className="dialog"
        onClick={(e) => e.stopPropagation()}
        onSubmit={handleSubmit}
      >
        <h3>New room</h3>
        <p className="dialog-hint">You join it as its first member. Share its ID so others can join.</p>
        <input
          type="text"
          placeholder="Room name"
          value={name}
          onChange={(e) => setName(e.target.value)}
          autoFocus
        />
        <div className="dialog-actions">
          <button type="button" onClick={onClose}>
            Cancel
          </button>
          <button type="submit" disabled={loading || !name.trim()}>
            Create room
          </button>
        </div>
      </form>
    </div>
  );
}
