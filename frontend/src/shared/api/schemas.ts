import { z } from 'zod'

const id = z.uuid()
const integer = z.number().int().nonnegative()
const timestamp = z.iso.datetime({ offset: true })
export const stoneSchema = z.enum(['X', 'O'])
export const gameStatusSchema = z.enum(['InProgress', 'XWon', 'OWon', 'Draw'])
export const turnStatusSchema = z.enum(['human', 'ai_pending', 'ai_retrying', 'finished'])
export const moveSchema = z.object({ step: integer, row: integer, column: integer, stone: stoneSchema })
export const matchSchema = z.object({
  matchId: id, ruleVersion: z.string(), boardSize: z.number().int().positive(),
  boardColumns: z.number().int().positive().optional(),
  humanStone: stoneSchema.default('X'), xScore: integer, oScore: integer, revision: integer,
  currentGame: z.object({
    id, number: z.number().int().positive(), openingStone: stoneSchema,
    aiVersion: z.string().nullable(), status: gameStatusSchema, nextStone: stoneSchema.nullable(),
    turnStatus: turnStatusSchema, aiErrorCode: z.string().nullable(), aiRetryAt: timestamp.nullable(),
    moves: z.array(moveSchema),
  }),
})
const historyFields = {
  gameId: id, matchId: id, number: z.number().int().positive(), status: gameStatusSchema,
  openingStone: stoneSchema, ruleVersion: z.string(), boardSize: z.number().int().positive(),
  boardColumns: z.number().int().positive().nullable().optional(),
  aiVersion: z.string().nullable(), startedAt: timestamp, finishedAt: timestamp,
}
export const historySummarySchema = z.object({ ...historyFields, moveCount: integer, humanStone: stoneSchema.nullable().optional() })
export const historyGameSchema = z.object({ ...historyFields, moves: z.array(moveSchema.extend({ actor: z.enum(['Human', 'AI']), playedAt: timestamp })) })
export const historyPageSchema = z.object({ items: z.array(historySummarySchema), nextCursor: z.string().nullable() })
// Active lists use the server's opaque cursor, including owner binding; offsets are rejected.
export const activeMatchPageSchema = z.object({
  items: z.array(z.object({ matchId: id, gameId: id, revision: integer, updatedAt: timestamp })),
  nextCursor: z.string().nullable(),
})
export const accountSchema = z.object({ userId: id, email: z.string() })
export const guestSessionSchema = z.object({ guestSessionId: id, expiresAt: timestamp.optional() })
export const rankingEntrySchema = z.object({ accountId: id, points: integer, rank: z.number().int().positive() })
export const rankingPageSchema = z.object({ items: z.array(rankingEntrySchema) })
export const rankingPositionSchema = rankingEntrySchema.extend({ rank: z.number().int().positive().nullable() })
export const csrfSchema = z.object({ requestToken: z.string().min(1) })
export const errorSchema = z.object({ code: z.string(), message: z.string(), traceId: z.string().optional(), errors: z.array(z.string()).nullable().optional() })
export const noContentSchema = z.undefined()
