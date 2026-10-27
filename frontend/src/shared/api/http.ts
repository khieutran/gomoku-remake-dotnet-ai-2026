import type { z } from 'zod'
import { ApiError } from './errors'
import { csrfSchema, errorSchema } from './schemas'

function apiPath(path: string): string {
  if (!path.startsWith('/') || path.startsWith('//') || path.includes('\\')) {
    throw new TypeError('API paths must be relative to this origin.')
  }
  const fullPath = path.startsWith('/api/v1/') || path === '/api/v1' ? path : `/api/v1${path}`
  if (!new URL(fullPath, 'https://same-origin.test').pathname.startsWith('/api/v1/')) {
    throw new TypeError('API paths must stay under /api/v1/.')
  }
  return fullPath
}
async function parseResponse<T>(response: Response, schema: z.ZodType<T>): Promise<T> {
  let body: unknown
  try {
    body = response.status === 204 ? undefined : await response.json()
  } catch {
    throw new ApiError(response.status, 'invalid_response', 'The server returned an unreadable response.')
  }
  if (!response.ok) {
    const parsed = errorSchema.safeParse(body)
    if (parsed.success) throw new ApiError(response.status, parsed.data.code, parsed.data.message, parsed.data.traceId)
    throw new ApiError(response.status, 'http_error', 'The request could not be completed.')
  }
  const parsed = schema.safeParse(body)
  if (!parsed.success) throw new ApiError(response.status, 'invalid_response', 'The server response did not match the API contract.')
  return parsed.data
}
export async function apiGet<T>(path: string, schema: z.ZodType<T>, signal?: AbortSignal): Promise<T> {
  const response = await fetch(apiPath(path), { method: 'GET', credentials: 'same-origin', headers: { Accept: 'application/json' }, signal })
  return parseResponse(response, schema)
}
export async function apiPost<T>(path: string, body: unknown, schema: z.ZodType<T>, signal?: AbortSignal): Promise<T> {
  const url = apiPath(path)
  // Serialize once: a CSRF retry must preserve the original command and payload.
  const serialized = JSON.stringify(body)
  for (let attempt = 0; attempt < 2; attempt++) {
    // Obtain a fresh identity-bound token for each mutation; never persist it in browser storage.
    const { requestToken } = await apiGet('/auth/csrf', csrfSchema, signal)
    const response = await fetch(url, {
      method: 'POST', credentials: 'same-origin', signal,
      headers: { Accept: 'application/json', 'Content-Type': 'application/json', 'X-CSRF-TOKEN': requestToken }, body: serialized,
    })
    try {
      return await parseResponse(response, schema)
    } catch (error) {
      if (attempt === 0 && error instanceof ApiError && error.code === 'csrf_invalid') continue
      throw error
    }
  }
  throw new Error('Unreachable CSRF retry state.')
}
