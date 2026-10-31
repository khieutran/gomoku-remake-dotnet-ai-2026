import { useCallback, useEffect, useRef, useState, type ReactNode } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { useLocation } from 'react-router-dom';
import { ApiError } from '../../shared/api/errors';
import { sessionApi } from './sessionApi';
import { SessionContext, type Session } from './sessionContext';

export function SessionProvider({ children }: { children: ReactNode }) {
  const queryClient = useQueryClient();
  const location = useLocation();
  const [session, setSession] = useState<Session>({ scope: 'none' });
  const sessionRef = useRef<Session>(session);
  const [ready, setReady] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const bootstrapped = useRef(false);
  const generation = useRef(0);

  const clearOwnerData = useCallback(async () => {
    await queryClient.cancelQueries();
    queryClient.clear();
  }, [queryClient]);

  const changeSession = useCallback(async (next: Session) => {
    const previous = sessionRef.current;
    if (previous.scope !== next.scope || (previous.scope !== 'none' && next.scope !== 'none' && previous.ownerId !== next.ownerId)) {
      await clearOwnerData();
    }
    sessionRef.current = next;
    setSession(next);
    setError(null);
    setReady(true);
  }, [clearOwnerData]);

  useEffect(() => {
    if (bootstrapped.current) return;
    const controller = new AbortController();
    const current = ++generation.current;
    setReady(false);
    void (async () => {
      try {
        const account = await sessionApi.me(controller.signal);
        if (current !== generation.current || controller.signal.aborted) return;
        bootstrapped.current = true;
        await changeSession({ scope: 'account', ownerId: account.userId, email: account.email, displayName: account.displayName });
      } catch (accountError) {
        if (current !== generation.current || controller.signal.aborted) return;
        if (!(accountError instanceof ApiError && accountError.status === 401)) {
          setError('Không kiểm tra được phiên. Hãy tải lại trang.');
          setReady(true);
          return;
        }
        try {
          const guest = await sessionApi.currentGuest(controller.signal);
          if (current !== generation.current || controller.signal.aborted) return;
          bootstrapped.current = true;
          await changeSession({ scope: 'guest', ownerId: guest.guestSessionId });
        } catch (guestError) {
          if (current !== generation.current || controller.signal.aborted) return;
          if (guestError instanceof ApiError && guestError.status === 401) bootstrapped.current = true;
          else setError('Không kiểm tra được phiên. Hãy tải lại trang.');
          setReady(true);
        }
      }
    })();
    return () => controller.abort();
  }, [location.pathname, changeSession]);

  const chooseGuest = useCallback(async () => {
    const current = ++generation.current;
    const guest = await sessionApi.guest();
    if (current === generation.current) {
      bootstrapped.current = true;
      await changeSession({ scope: 'guest', ownerId: guest.guestSessionId });
    }
  }, [changeSession]);

  const login = useCallback(async (email: string, password: string) => {
    const current = ++generation.current;
    const account = await sessionApi.login(email, password);
    if (current === generation.current) {
      bootstrapped.current = true;
      await changeSession({ scope: 'account', ownerId: account.userId, email: account.email, displayName: account.displayName });
    }
  }, [changeSession]);

  const register = useCallback(async (email: string, password: string, displayName: string) => {
    const current = ++generation.current;
    const account = await sessionApi.register(email, password, displayName);
    if (current === generation.current) {
      bootstrapped.current = true;
      await changeSession({ scope: 'account', ownerId: account.userId, email: account.email, displayName: account.displayName });
    }
  }, [changeSession]);

  const logout = useCallback(async () => {
    ++generation.current;
    await sessionApi.logout();
    bootstrapped.current = true;
    await changeSession({ scope: 'none' });
  }, [changeSession]);

  const requireAccount = useCallback(async () => {
    const current = ++generation.current;
    // A protected request has already reported 401. Hide its owner's data before
    // any network recheck can leave a stale snapshot on screen.
    await changeSession({ scope: 'none' });
    if (current !== generation.current) return;
    try {
      const account = await sessionApi.me();
      if (current === generation.current) await changeSession({ scope: 'account', ownerId: account.userId, email: account.email, displayName: account.displayName });
    } catch (reason) {
      if (current !== generation.current) return;
      if (!(reason instanceof ApiError && reason.status === 401)) setError('Không kiểm tra được phiên. Hãy thử lại.');
    }
  }, [changeSession]);

  return <SessionContext.Provider value={{ session, ready, error, chooseGuest, login, register, logout, requireAccount }}>{children}</SessionContext.Provider>;
}
