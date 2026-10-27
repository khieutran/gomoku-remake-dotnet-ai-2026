import { apiGet, apiPost } from '../../shared/api/http';
import { accountSchema, guestSessionSchema, noContentSchema } from '../../shared/api/schemas';

export const sessionApi = {
  me: (signal?: AbortSignal) => apiGet('/auth/me', accountSchema, signal),
  currentGuest: (signal?: AbortSignal) => apiGet('/guest-sessions/current', guestSessionSchema, signal),
  guest: () => apiPost('/guest-sessions', {}, guestSessionSchema),
  login: (email: string, password: string) => apiPost('/auth/login', { email, password }, accountSchema),
  register: (email: string, password: string) => apiPost('/auth/register', { email, password }, accountSchema),
  logout: () => apiPost('/auth/logout', {}, noContentSchema),
};
