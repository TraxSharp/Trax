import { useMutation, useQuery } from "@apollo/client";
import { useEffect, useRef, useState } from "react";
import { INVITE_TO_CHAT_ROOM } from "../graphql/mutations";
import { GET_CHAT_ROOMS, GET_CHAT_ROOM_PEOPLE } from "../graphql/queries";
import type { ChatRoomPerson } from "../types";
import { Avatar } from "./Avatar";

/**
 * Who the signed-in member can add to the room. The server decides who that is (the people it knows,
 * minus the caller) and adds them under the name it holds; the client only names who to add.
 */
export function InvitePanel({ roomId, onClose }: { roomId: string; onClose(): void }) {
  const panel = useRef<HTMLDivElement>(null);
  const [error, setError] = useState<string | null>(null);
  const { data, loading } = useQuery(GET_CHAT_ROOM_PEOPLE, {
    variables: { input: { chatRoomId: roomId } },
    fetchPolicy: "network-only",
  });
  const [invite, { loading: adding }] = useMutation(INVITE_TO_CHAT_ROOM, {
    refetchQueries: [GET_CHAT_ROOM_PEOPLE, GET_CHAT_ROOMS],
  });
  const people: ChatRoomPerson[] = data?.discover?.getChatRoomPeople?.people ?? [];

  // Close on Escape or a click outside the panel.
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => e.key === "Escape" && onClose();
    const onClick = (e: MouseEvent) => {
      if (panel.current && !panel.current.contains(e.target as Node)) onClose();
    };
    window.addEventListener("keydown", onKey);
    window.addEventListener("mousedown", onClick);
    return () => {
      window.removeEventListener("keydown", onKey);
      window.removeEventListener("mousedown", onClick);
    };
  }, [onClose]);

  const add = async (person: ChatRoomPerson) => {
    setError(null);
    try {
      await invite({ variables: { input: { chatRoomId: roomId, userId: person.userId } } });
    } catch (e) {
      setError((e as Error).message);
    }
  };

  return (
    <div className="invite-panel" ref={panel} role="dialog" aria-label="Invite people">
      <div className="invite-head">
        <h3>Invite people</h3>
        <p>They join straight away, and the room appears in their list.</p>
      </div>
      {loading && <div className="invite-empty">Loading…</div>}
      {!loading && people.length === 0 && <div className="invite-empty">There is no one else to invite.</div>}
      <ul className="invite-list">
        {people.map((p) => (
          <li key={p.userId}>
            <Avatar name={p.displayName} />
            <span className="invite-name">{p.displayName}</span>
            {p.isMember ? (
              <span className="invite-in">In room</span>
            ) : (
              <button onClick={() => add(p)} disabled={adding}>
                Add
              </button>
            )}
          </li>
        ))}
      </ul>
      {error && <div className="invite-error">{error}</div>}
    </div>
  );
}
