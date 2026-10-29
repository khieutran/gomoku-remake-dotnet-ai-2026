import { createContext, useContext } from 'react';

export type Session = { scope: 'account'; ownerId: string; email?: string } | { scope: 'guest'; ownerId: string } | { scope: 'none' };
export type SessionContextValue = {
  session: Session;
  ready: boolean;
  error: string | null;
  chooseGuest: () => Promise<void>;
  login: (email: string, password: string) => Promise<void>;
  register: (email: string, password: string) => Promise<void>;
  logout: () => Promise<void>;
  requireAccount: () => Promise<void>;
};
export const SessionContext = createContext<SessionContextValue | null>(null);

export function useSession(): SessionContextValue {
  const context = useContext(SessionContext);
  if (!context) throw new Error('useSession must be used under SessionProvider');
  return context;
}
