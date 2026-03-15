import { createContext, useContext, useState, useMemo, type ReactNode } from "react";
import { ApolloProvider } from "@apollo/client";
import { createApolloClient } from "../apollo";
import { USERS, type User } from "../types";

interface UserContextValue {
  user: User;
  setUser: (user: User) => void;
}

const UserContext = createContext<UserContextValue | null>(null);

export function useUser(): UserContextValue {
  const ctx = useContext(UserContext);
  if (!ctx) throw new Error("useUser must be used within UserProvider");
  return ctx;
}

export function UserProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<User>(USERS[0]);

  const client = useMemo(() => createApolloClient(user.key), [user.key]);

  return (
    <UserContext.Provider value={{ user, setUser }}>
      <ApolloProvider client={client}>{children}</ApolloProvider>
    </UserContext.Provider>
  );
}
