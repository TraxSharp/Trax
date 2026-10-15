import { configureTrax, type TraxConnection } from "./trax/index.g";

export const API_URL = "http://localhost:5260/trax/graphql";
export const SOCKET_URL = "ws://localhost:5260/trax/graphql";

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
