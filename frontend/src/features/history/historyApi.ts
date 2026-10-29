import { apiGet } from '../../shared/api/http';
import { activeMatchPageSchema, historyGameSchema, historyPageSchema } from '../../shared/api/schemas';
import type { OwnerScope } from '../../shared/api/contracts';

function listPath(path: string, scope: OwnerScope, cursor: string | null, limit: number): string {
  const params = new URLSearchParams({ scope, limit: String(limit) });
  if (cursor !== null) params.set('cursor', cursor);
  return `${path}?${params}`;
}

export function listActive(scope: OwnerScope, cursor: string | null = null, limit = 20, signal?: AbortSignal) {
  return apiGet(listPath('/matches/active', scope, cursor, limit), activeMatchPageSchema, signal);
}

export function listHistory(scope: OwnerScope, cursor: string | null = null, limit = 20, signal?: AbortSignal) {
  return apiGet(listPath('/history', scope, cursor, limit), historyPageSchema, signal);
}

export function getHistoryGame(scope: OwnerScope, gameId: string, signal?: AbortSignal) {
  return apiGet(`/history/${encodeURIComponent(gameId)}?scope=${scope}`, historyGameSchema, signal);
}
