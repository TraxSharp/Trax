import { configureTrax, type TraxConnection } from "./trax/index.g";

// The host: localhost:5260 in development, or what the build was given. The socket is the same origin over ws or wss.
const API_ORIGIN: string = import.meta.env.VITE_RECOVERY_API ?? "http://localhost:5260";

export const API_URL = `${API_ORIGIN}/trax/graphql`;
export const SOCKET_URL = `${API_ORIGIN.replace(/^http/, "ws")}/trax/graphql`;
/** The host's dashboard, for an operator's actions the page does not offer. */
export const DASHBOARD_URL = `${API_ORIGIN}/trax`;

// The demo user's key, registered by the host in Development only. The page is a user's: it starts runs, follows its
// own runs through runProgress, and drives its own topic map and food order. Nothing it sends is under operations.
const USER_KEY = "recovery-user-key-do-not-use-in-production";

const current: TraxConnection = {
  url: API_URL,
  socketUrl: SOCKET_URL,
  headers: () => ({ "X-Api-Key": USER_KEY }),
  // A browser cannot set headers on a socket: the key travels in connection_init.
  connectionParams: () => ({ apiKey: USER_KEY }),
};

configureTrax(current);

/** Where the host is and how the page signs in, for a second socket of its own (the phone's). */
export const connection = (): TraxConnection => current;
