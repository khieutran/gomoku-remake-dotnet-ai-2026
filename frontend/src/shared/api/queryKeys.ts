import type { OwnerIdentity } from './contracts'
const owner = ({ scope, ownerId }: OwnerIdentity) => ['owner', scope, ownerId] as const
export const queryKeys = {
  owner,
  match: (identity: OwnerIdentity, matchId: string) => [...owner(identity), 'match', matchId] as const,
  active: (identity: OwnerIdentity, cursor: string | null = null, limit = 20) => [...owner(identity), 'active', cursor, limit] as const,
  history: (identity: OwnerIdentity, cursor: string | null = null, limit = 20) => [...owner(identity), 'history', cursor, limit] as const,
  historyGame: (identity: OwnerIdentity, gameId: string) => [...owner(identity), 'history-game', gameId] as const,
  rankingMe: (identity: OwnerIdentity) => [...owner(identity), 'ranking-me'] as const,
  leaderboard: (limit = 20) => ['leaderboard', limit] as const,
}
