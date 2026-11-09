// The host: localhost:5280 in development, or what the build was given. The socket is the same origin over ws or wss.
const API_ORIGIN: string = import.meta.env.VITE_STATEMACHINE_API ?? "http://localhost:5280";

export const ENDPOINT = `${API_ORIGIN}/trax/graphql`;
export const SOCKET = `${API_ORIGIN.replace(/^http/, "ws")}/trax/graphql`;
