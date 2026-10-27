import { afterEach, describe, expect, it, vi } from 'vitest'
import { z } from 'zod'
import { apiGet, apiPost } from './http'
import { ApiError } from './errors'
import { matchSchema } from './schemas'
const matchFixture = { matchId: 'd042f3a1-2038-43dc-bb2a-b4f6b752292b', boardSize: 20, ruleVersion: 'v1', revision: 1, xScore: 0, oScore: 0, currentGame: { id: 'd042f3a1-2038-43dc-bb2a-b4f6b752292b', number: 1, openingStone: 'X', aiVersion: null, status: 'InProgress', nextStone: 'O', turnStatus: 'ai_pending', aiErrorCode: null, aiRetryAt: null, moves: [{ step: 1, row: 0, column: 1, stone: 'X' }] } }
const schema = z.object({ result: z.string() })
const json = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status })
afterEach(() => vi.unstubAllGlobals())
describe('HTTP boundary', () => {
  it.each([200, 201, 202])('parses a successful Match response with HTTP %i', async status => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(json(matchFixture, status)))
    await expect(apiGet('/matches/id', matchSchema)).resolves.toMatchObject({ revision: 1, currentGame: { turnStatus: 'ai_pending' } })
  })
  it('GET sends same-origin cookies without fetching or sending CSRF', async () => {
    const fetcher = vi.fn().mockResolvedValue(json({ result: 'ok' }))
    vi.stubGlobal('fetch', fetcher)
    expect(await apiGet('/auth/me', schema)).toEqual({ result: 'ok' })
    expect(fetcher).toHaveBeenCalledWith('/api/v1/auth/me', expect.objectContaining({ credentials: 'same-origin', method: 'GET' }))
    expect(fetcher.mock.calls[0][1].headers['X-CSRF-TOKEN']).toBeUndefined()
  })
  it.each(['/guest-sessions', '/auth/login', '/matches?scope=guest'])('protects POST %s and retries csrf_invalid once with identical serialized payload', async path => {
    const fetcher = vi.fn().mockResolvedValueOnce(json({ requestToken: 'old' })).mockResolvedValueOnce(json({ code: 'csrf_invalid', message: 'expired' }, 400)).mockResolvedValueOnce(json({ requestToken: 'fresh' })).mockResolvedValueOnce(json({ result: 'ok' }, 201))
    vi.stubGlobal('fetch', fetcher)
    const body = { commandId: 'one-intent', expectedRevision: 0 }
    await expect(apiPost(path, body, schema)).resolves.toEqual({ result: 'ok' })
    const first = fetcher.mock.calls[1][1]
    const retry = fetcher.mock.calls[3][1]
    expect(first.credentials).toBe('same-origin')
    expect(first.headers['X-CSRF-TOKEN']).toBe('old')
    expect(retry.headers['X-CSRF-TOKEN']).toBe('fresh')
    expect(retry.body).toBe(first.body)
    expect(JSON.parse(retry.body)).toEqual(body)
  })
  it('stops after one CSRF retry', async () => {
    const fetcher = vi.fn().mockImplementation((path: string) => Promise.resolve(path.endsWith('/csrf') ? json({ requestToken: 'token' }) : json({ code: 'csrf_invalid', message: 'no' }, 400)))
    vi.stubGlobal('fetch', fetcher)
    await expect(apiPost('/auth/logout', {}, schema)).rejects.toMatchObject({ status: 400, code: 'csrf_invalid' })
    expect(fetcher).toHaveBeenCalledTimes(4)
  })
  it.each([400, 401, 409, 500])('preserves error code and trace for HTTP %i', async status => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(json({ code: 'server_specific_code', message: 'Message', traceId: 'trace' }, status)))
    const error = await apiGet('/matches/id', schema).catch((value: unknown) => value)
    expect(error).toBeInstanceOf(ApiError)
    expect(error).toMatchObject({ status, code: 'server_specific_code', traceId: 'trace' })
  })
  it('rejects invalid successful JSON and handles 204 logout', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValueOnce(json({ wrong: true })).mockResolvedValueOnce(json({ requestToken: 'token' })).mockResolvedValueOnce(new Response(null, { status: 204 })))
    await expect(apiGet('/matches/id', schema)).rejects.toMatchObject({ code: 'invalid_response' })
    await expect(apiPost('/auth/logout', {}, z.undefined())).resolves.toBeUndefined()
  })
  it('forwards cancellation and rejects cross-origin paths', async () => {
    const fetcher = vi.fn().mockResolvedValue(json({ result: 'ok' }))
    vi.stubGlobal('fetch', fetcher)
    const signal = new AbortController().signal
    await apiGet('/matches', schema, signal)
    expect(fetcher.mock.calls[0][1].signal).toBe(signal)
    await expect(apiGet('https://other.test/api', schema)).rejects.toThrow()
    expect(fetcher).toHaveBeenCalledTimes(1)
  })
})
