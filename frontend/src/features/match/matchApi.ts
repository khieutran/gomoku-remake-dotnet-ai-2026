import { apiGet, apiPost } from '../../shared/api/http';
import { activeMatchPageSchema, matchSchema } from '../../shared/api/schemas';
import type { ActiveMatchPageDto, CreateMatchCommand, MatchDto, NextGameCommand, OwnerScope, PlayMoveCommand } from '../../shared/api/contracts';

const path = (scope: OwnerScope, id?: string) => `/matches${id ? `/${encodeURIComponent(id)}` : ''}?scope=${scope}`;
const actionPath = (scope: OwnerScope, id: string, action: string) => `/matches/${encodeURIComponent(id)}/${action}?scope=${scope}`;
export const matchApi = {
  active: (scope: OwnerScope, cursor?: string, signal?: AbortSignal): Promise<ActiveMatchPageDto> =>
    apiGet(`/matches/active?scope=${scope}&limit=20${cursor ? `&cursor=${encodeURIComponent(cursor)}` : ''}`, activeMatchPageSchema, signal),
  createMatch: (scope: OwnerScope, commandId: string): Promise<MatchDto> => apiPost(path(scope), { commandId } satisfies CreateMatchCommand, matchSchema),
  getMatch: (scope: OwnerScope, id: string, signal?: AbortSignal): Promise<MatchDto> => apiGet(path(scope, id), matchSchema, signal),
  playMove: (scope: OwnerScope, id: string, command: PlayMoveCommand): Promise<MatchDto> => apiPost(actionPath(scope, id, 'moves'), command, matchSchema),
  nextGame: (scope: OwnerScope, id: string, command: NextGameCommand): Promise<MatchDto> => apiPost(actionPath(scope, id, 'next-game'), command, matchSchema),
};
