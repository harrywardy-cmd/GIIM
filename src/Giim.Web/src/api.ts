/** Every change must carry this header; the API refuses changes without it (cross-site request protection). */
export const csrfHeaders = { 'X-GIIM-Request': '1' }

/** Fired when the session has ended, so the app can show the sign-in page. */
export const signedOutEvent = 'giim:signed-out'

/** Small fetch wrapper: adds the request header, throws the API's problem detail as the error message. */
export async function api<T>(url: string, init?: RequestInit & { json?: unknown }): Promise<T> {
  const { json, ...rest } = init ?? {}
  const response = await fetch(url, {
    ...rest,
    headers: {
      ...csrfHeaders,
      ...(json !== undefined ? { 'Content-Type': 'application/json' } : {}),
      ...rest.headers,
    },
    body: json !== undefined ? JSON.stringify(json) : rest.body,
  })

  if (response.status === 401) window.dispatchEvent(new Event(signedOutEvent))
  if (!response.ok) {
    const problem = await response.json().catch(() => null)
    const message =
      response.status === 403 ? "You don't have permission to do that." : (problem?.detail ?? `Request failed (${response.status})`)
    throw new Error(message)
  }
  return response.status === 204 ? (undefined as T) : response.json()
}
