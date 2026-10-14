// Decides whether graphql-ws should reconnect after a socket close. We retry indefinitely on
// transient failures (network blips, server restarts, broker hiccups), but NOT on auth/handshake
// rejections: the credential won't fix itself by retrying, and hammering the server with a bad
// token is pointless. A credential change terminates and reconnects the socket explicitly, so
// re-auth is handled out of band. graphql-ws reports connection_init rejections with 44xx close
// codes (4401 Unauthorized / 4403 Forbidden / 4400 Bad Request / 4429 Too Many Requests).
const NON_RETRYABLE_CLOSE_CODES = new Set([4400, 4401, 4403, 4429]);

export function shouldRetryWs(errOrCloseEvent: unknown): boolean {
  const code = (errOrCloseEvent as { code?: number } | null | undefined)?.code;
  if (typeof code === "number" && NON_RETRYABLE_CLOSE_CODES.has(code)) return false;
  return true;
}
