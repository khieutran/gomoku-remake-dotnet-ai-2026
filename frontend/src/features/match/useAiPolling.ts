import { useEffect, useRef } from 'react';
import type { MatchDto } from '../../shared/api/contracts';

export function useAiPolling(snapshot: MatchDto | null, refresh: () => Promise<void>) {
  const refreshRef = useRef(refresh);
  useEffect(() => { refreshRef.current = refresh; }, [refresh]);
  const game = snapshot?.currentGame;
  const pending = game?.turnStatus === 'ai_pending' || game?.turnStatus === 'ai_retrying';
  useEffect(() => {
    if (!pending) return;
    let stopped = false;
    let timer: ReturnType<typeof setTimeout> | undefined;
    let errors = 0;
    let inFlight = false;
    const schedule = (delay: number) => { timer = setTimeout(() => void tick(), delay); };
    const tick = async () => {
      // focus and visibilitychange both fire on resume; never run two polls at once.
      if (stopped || inFlight || document.visibilityState === 'hidden') return;
      inFlight = true;
      try { await refreshRef.current(); errors = 0; }
      catch { errors++; }
      finally { inFlight = false; }
      if (!stopped && document.visibilityState === 'visible') schedule(errors ? Math.min(10000, 1000 * 2 ** errors) : game?.turnStatus === 'ai_retrying' ? 2000 : 1000);
    };
    const visible = () => {
      if (document.visibilityState === 'hidden') clearTimeout(timer);
      else { clearTimeout(timer); void tick(); }
    };
    document.addEventListener('visibilitychange', visible);
    window.addEventListener('focus', visible);
    if (document.visibilityState !== 'hidden') void tick();
    return () => { stopped = true; clearTimeout(timer); document.removeEventListener('visibilitychange', visible); window.removeEventListener('focus', visible); };
  }, [snapshot?.matchId, game?.id, game?.turnStatus, pending]);
}
