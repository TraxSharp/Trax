import { useUser } from "../context/UserContext";
import { USERS } from "../types";
import { Avatar } from "./Avatar";

export function UserSelector() {
  const { user, setUser } = useUser();

  return (
    <div className="group">
      <span className="group-label">Signed in as</span>
      <div className="user-selector" role="radiogroup" aria-label="Signed in as">
        {USERS.map((u) => (
          <button
            key={u.userId}
            role="radio"
            aria-checked={u.userId === user.userId}
            className={u.userId === user.userId ? "active" : ""}
            onClick={() => setUser(u)}
          >
            <Avatar name={u.displayName} size="sm" />
            {u.displayName}
          </button>
        ))}
      </div>
    </div>
  );
}
