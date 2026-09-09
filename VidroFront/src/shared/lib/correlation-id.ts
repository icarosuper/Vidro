/**
 * One id per outbound API call, sent as `X-Correlation-ID`. The API already accepts the
 * header and echoes it back (`CorrelationIdMiddleware`), pushing it into every Serilog line
 * of that request — so an id the user reads on the error screen is enough to find the
 * request in Loki. Nothing here is a secret or a security token: it only has to be unique
 * enough to be greppable.
 */
export const CORRELATION_ID_HEADER = 'X-Correlation-ID'

export function newCorrelationId(): string {
  // crypto.randomUUID is undefined in a browser outside a secure context (plain http on a
  // non-localhost host), and a throw here would break every request instead of one log line.
  if (
    typeof crypto !== 'undefined' &&
    typeof crypto.randomUUID === 'function'
  ) {
    return crypto.randomUUID()
  }
  return `${Date.now().toString(36)}-${Math.random().toString(36).slice(2, 10)}`
}
