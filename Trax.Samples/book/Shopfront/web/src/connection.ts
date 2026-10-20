import { configureTrax } from "./trax/index.g";

// Where the hub is: from the build's environment, or the hub `dotnet run` starts.
const HUB = import.meta.env.VITE_HUB_URL ?? "http://localhost:5080";

// Signs in as a customer with a Development token from the hub, and points the generated client at
// it. Every request carries the token; the socket, which cannot carry a header, sends it when it
// opens. A deployed storefront gets its token from the identity provider the hub trusts instead.
export async function signIn(user: string): Promise<void> {
  const token = await (await fetch(`${HUB}/dev/token/${user}`)).text();
  configureTrax({
    url: `${HUB}/trax/graphql`,
    socketUrl: `${HUB.replace("http", "ws")}/trax/graphql`,
    headers: () => ({ Authorization: `Bearer ${token}` }),
    connectionParams: () => ({ authToken: token }),
    user,
  });
}
