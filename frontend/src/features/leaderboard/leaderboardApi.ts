import { apiGet } from '../../shared/api/http';
import { rankingPageSchema, rankingPositionSchema } from '../../shared/api/schemas';

export const leaderboardApi = {
  top: (signal?: AbortSignal) => apiGet('/leaderboard?limit=20', rankingPageSchema, signal),
  me: (signal?: AbortSignal) => apiGet('/leaderboard/me', rankingPositionSchema, signal),
};
