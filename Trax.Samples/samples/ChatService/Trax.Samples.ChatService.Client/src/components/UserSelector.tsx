import { useUser } from "../context/UserContext";
import { USERS } from "../types";

export function UserSelector() {
  const { user, setUser } = useUser();

  return (
    <div className="user-selector">
      <label htmlFor="user-select">Logged in as:</label>
      <select
        id="user-select"
        value={user.userId}
        onChange={(e) => {
          const next = USERS.find((u) => u.userId === e.target.value);
          if (next) setUser(next);
        }}
      >
        {USERS.map((u) => (
          <option key={u.userId} value={u.userId}>
            {u.displayName}
          </option>
        ))}
      </select>
    </div>
  );
}
