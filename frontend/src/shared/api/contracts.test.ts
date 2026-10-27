import { describe, expect, it } from 'vitest'
import { activeMatchPageSchema, historyGameSchema, historyPageSchema, matchSchema, rankingPageSchema, rankingPositionSchema, guestSessionSchema, accountSchema } from './schemas'

const id = 'd042f3a1-2038-43dc-bb2a-b4f6b752292b'
const date = '2026-10-27T00:00:00+00:00'
export const matchFixture = { matchId: id, boardSize: 20, ruleVersion: 'v1', revision: 1, xScore: 0, oScore: 0, currentGame: { id, number: 1, openingStone: 'X', aiVersion: null, status: 'InProgress', nextStone: 'O', turnStatus: 'ai_pending', aiErrorCode: null, aiRetryAt: null, moves: [{ step: 1, row: 0, column: 1, stone: 'X' }] } }
const summary = { gameId: id, matchId: id, number: 1, status: 'XWon', openingStone: 'X', ruleVersion: 'v1', boardSize: 20, aiVersion: null, startedAt: date, finishedAt: date }
describe('wire contracts', () => {
  it.each(['human', 'ai_pending', 'ai_retrying', 'finished'])('parses Match turn %s returned by 200/201/202', turnStatus => {
    expect(matchSchema.parse({ ...matchFixture, currentGame: { ...matchFixture.currentGame, turnStatus } }).revision).toBe(1)
  })
  it('rejects missing revision and unknown stone', () => {
    const { revision: omitted, ...missing } = matchFixture
    void omitted
    expect(matchSchema.safeParse(missing).success).toBe(false)
    expect(matchSchema.safeParse({ ...matchFixture, currentGame: { ...matchFixture.currentGame, moves: [{ step: 1, row: 0, column: 0, stone: 'Z' }] } }).success).toBe(false)
  })
  it('uses opaque cursors for history and active matches', () => {
    expect(historyPageSchema.parse({ items: [{ ...summary, moveCount: 9 }], nextCursor: 'opaque' }).nextCursor).toBe('opaque')
    expect(activeMatchPageSchema.parse({ items: [{ matchId: id, gameId: id, revision: 2, updatedAt: date }], nextCursor: null }).nextCursor).toBeNull()
    expect(activeMatchPageSchema.safeParse({ items: [], nextOffset: 1 }).success).toBe(false)
    expect(historyGameSchema.parse({ ...summary, moves: [{ step: 1, row: 0, column: 0, stone: 'X', actor: 'Human', playedAt: date }] }).moves[0].step).toBe(1)
  })
  it('parses account, new/reused guest and ranking without invented counters', () => {
    expect(accountSchema.parse({ userId: id, email: 'player@example.test' }).userId).toBe(id)
    expect(guestSessionSchema.parse({ guestSessionId: id }).expiresAt).toBeUndefined()
    expect(guestSessionSchema.parse({ guestSessionId: id, expiresAt: date }).expiresAt).toBe(date)
    expect(rankingPageSchema.parse({ items: [{ accountId: id, points: 3, rank: 1 }] }).items[0].points).toBe(3)
    expect(rankingPositionSchema.parse({ accountId: id, points: 0, rank: null }).rank).toBeNull()
  })
})

import { queryKeys } from './queryKeys'
it('separates every private cache by scope and owner, with an owner prefix for cleanup', () => {
  const account = { scope: 'account' as const, ownerId: 'a' }
  const guest = { scope: 'guest' as const, ownerId: 'a' }
  expect(queryKeys.match(account, id)).not.toEqual(queryKeys.match(guest, id))
  expect(queryKeys.history(account, null)).not.toEqual(queryKeys.history({ ...account, ownerId: 'b' }, null))
  expect(queryKeys.match(account, id).slice(0, 3)).toEqual(queryKeys.owner(account))
  expect(queryKeys.active(account, null).slice(0, 3)).toEqual(queryKeys.owner(account))
  expect(queryKeys.rankingMe(account).slice(0, 3)).toEqual(queryKeys.owner(account))
})
