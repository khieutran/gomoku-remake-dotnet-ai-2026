import type { z } from 'zod'
import type { accountSchema, activeMatchPageSchema, guestSessionSchema, historyGameSchema, historyPageSchema, historySummarySchema, matchSchema, moveSchema, rankingEntrySchema, rankingPageSchema, rankingPositionSchema } from './schemas'

export type OwnerScope = 'account' | 'guest'
export type OwnerIdentity = { scope: OwnerScope; ownerId: string }
export type MatchDto = z.infer<typeof matchSchema>
export type MoveDto = z.infer<typeof moveSchema>
export type HistoryPageDto = z.infer<typeof historyPageSchema>
export type HistoryGameDto = z.infer<typeof historyGameSchema>
export type HistorySummaryDto = z.infer<typeof historySummarySchema>
export type ActiveMatchPageDto = z.infer<typeof activeMatchPageSchema>
export type AccountDto = z.infer<typeof accountSchema>
export type GuestSessionDto = z.infer<typeof guestSessionSchema>
export type RankingEntryDto = z.infer<typeof rankingEntrySchema>
export type RankingPageDto = z.infer<typeof rankingPageSchema>
export type RankingPositionDto = z.infer<typeof rankingPositionSchema>
export type CreateMatchCommand = { commandId: string }
export type NextGameCommand = { commandId: string; expectedRevision: number }
export type PlayMoveCommand = NextGameCommand & { row: number; column: number }
