import { useState } from "react";
import { useMutation } from "@apollo/client";
import { CREATE_CHAT_ROOM } from "../graphql/mutations";
import { GET_CHAT_ROOMS } from "../graphql/queries";
import { useUser } from "../context/UserContext";

interface CreateRoomDialogProps {
  onClose: () => void;
  onCreated: (roomId: string) => void;
}

export function CreateRoomDialog({ onClose, onCreated }: CreateRoomDialogProps) {
  const { user } = useUser();
  const [name, setName] = useState("");

  const [createRoom, { loading }] = useMutation(CREATE_CHAT_ROOM, {
    refetchQueries: [GET_CHAT_ROOMS],
  });

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!name.trim()) return;

    const result = await createRoom({
      variables: {
        input: {
          name: name.trim(),
          userId: user.userId,
          displayName: user.displayName,
        },
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
        <h3>Create Chat Room</h3>
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
            Create
          </button>
        </div>
      </form>
    </div>
  );
}
