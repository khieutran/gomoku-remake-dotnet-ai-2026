import { useCallback, useEffect, useRef, useState } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import type { MatchDto, MatchSetup, NextGameCommand, OwnerIdentity, PlayMoveCommand } from '../../shared/api/contracts';
import { queryKeys } from '../../shared/api/queryKeys';
import { ApiError } from '../../shared/api/errors';
import { matchApi } from './matchApi';
import { useAiPolling } from './useAiPolling';

type PendingCommand = { kind: 'move'; body: PlayMoveCommand; matchId: string } | { kind: 'next'; body: NextGameCommand; matchId: string } | { kind: 'create'; commandId: string; setup?: MatchSetup };
const messageFor = (reason: unknown) => {
  if (reason instanceof ApiError) {
    if (reason.status === 401) return 'Phiên đã hết hạn. Đăng nhập hoặc mở lại phiên khách để tiếp tục.';
    if (reason.code === 'stale_revision') return 'Ván đã thay đổi. Đã tải lại bàn cờ; hãy chọn nước mới.';
    if (reason.code === 'occupied') return 'Ô này đã có quân. Đã tải lại bàn cờ; hãy chọn ô khác.';
    if (reason.code === 'wrong_turn') return 'Chưa đến lượt bạn. Đã tải lại bàn cờ; hãy chờ bot.';
    if (reason.code === 'game_finished') return 'Ván đã kết thúc. Đã tải lại kết quả.';
    if (reason.code === 'game_in_progress') return 'Ván hiện tại chưa kết thúc.';
  }
  return 'Mất kết nối. Đã kiểm tra lại ván; bạn có thể thử lại.';
};

export function useMatchFlow(identity: OwnerIdentity | null, id?: string, setup?: MatchSetup,
  fallbackSetup: MatchSetup = { humanStone: 'X', humanFirst: true, boardSize: 20 }) {
  const query = useQueryClient();
  const [snapshot, setSnapshot] = useState<MatchDto | null>(null);
  const [pendingMove, setPendingMove] = useState<{ step: number; row: number; column: number; stone: 'X' | 'O' } | null>(null);
  const [busy, setBusy] = useState(false);
  const [canRetry, setCanRetry] = useState(false);
  const [loading, setLoading] = useState(true);
  const [loadAttempt, setLoadAttempt] = useState(0);
  const [error, setError] = useState<string | null>(null);
  const [unauthorized, setUnauthorized] = useState(false);
  const command = useRef<PendingCommand | null>(null);
  const createAttempt = useRef<{ ownerKey: string; setupKey: string; commandId: string; promise: Promise<MatchDto> } | null>(null);
  const lock = useRef(false);
  const current = useRef<MatchDto | null>(null);
  const generation = useRef(0);
  const ownerKey = identity ? `${identity.scope}:${identity.ownerId}` : '';
  const creationSetup = setup ?? fallbackSetup;
  const setupKey = `${creationSetup.humanStone}:${creationSetup.humanFirst}:${creationSetup.boardSize}:${creationSetup.boardColumns ?? creationSetup.boardSize}`;
  const setServer = useCallback((data: MatchDto, owner: OwnerIdentity) => {
    current.current = data;
    setSnapshot(data);
    setPendingMove(null);
    query.setQueryData(queryKeys.match(owner, data.matchId), data);
  }, [query]);
  const refresh = useCallback(async () => {
    if (!identity || !current.current) return;
    const run = generation.current;
    try {
      const data = await matchApi.getMatch(identity.scope, current.current.matchId);
      if (run === generation.current) setServer(data, identity);
    } catch (reason) {
      if (run === generation.current && reason instanceof ApiError && reason.status === 401) {
        query.removeQueries({ queryKey: queryKeys.owner(identity) });
        current.current = null; setSnapshot(null); setPendingMove(null); setUnauthorized(true); setError(messageFor(reason));
      }
      throw reason;
    }
  }, [identity, query, setServer]);

  useEffect(() => {
    const run = ++generation.current;
    current.current = null;
    command.current = createAttempt.current?.ownerKey === ownerKey && createAttempt.current.setupKey === setupKey
      ? { kind: 'create', commandId: createAttempt.current.commandId, setup: creationSetup } : null;
    lock.current = false;
    queueMicrotask(() => {
      if (run !== generation.current) return;
      setSnapshot(null); setPendingMove(null); setError(null); setUnauthorized(false); setLoading(true); setCanRetry(false);
    });
    if (!identity) { queueMicrotask(() => { if (run === generation.current) setLoading(false); }); return; }
    const boot = async () => {
      try {
        let data: MatchDto;
        if (id) data = await matchApi.getMatch(identity.scope, id);
        else {
          const active = setup ? { items: [] } : await matchApi.active(identity.scope);
          if (run !== generation.current) return;
          if (active.items.length) data = await matchApi.getMatch(identity.scope, active.items[0].matchId);
          else {
            let attempt = createAttempt.current;
            if (!attempt || attempt.ownerKey !== ownerKey || attempt.setupKey !== setupKey) {
              const commandId = crypto.randomUUID();
              attempt = { ownerKey, setupKey, commandId, promise: matchApi.createMatch(identity.scope, commandId, creationSetup) };
              createAttempt.current = attempt;
            }
            command.current = { kind: 'create', commandId: attempt.commandId, setup: creationSetup };
            data = await attempt.promise;
            if (run === generation.current) { command.current = null; createAttempt.current = null; }
          }
        }
        if (run === generation.current) setServer(data, identity);
      } catch (reason) {
        if (run !== generation.current) return;
        setError(messageFor(reason));
        setUnauthorized(reason instanceof ApiError && reason.status === 401);
      } finally { if (run === generation.current) setLoading(false); }
    };
    void boot();
    return () => { generation.current = run + 1; };
  // ownerKey keeps requests scoped to one session even when identity object changes per render.
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [ownerKey, id, setupKey, loadAttempt, setServer]);

  const execute = useCallback(async (item: PendingCommand) => {
    if (!identity || lock.current) return;
    const run = generation.current;
    lock.current = true; setBusy(true); setCanRetry(false); setError(null);
    try {
      const data = item.kind === 'move' ? await matchApi.playMove(identity.scope, item.matchId, item.body)
        : item.kind === 'next' ? await matchApi.nextGame(identity.scope, item.matchId, item.body)
          : await matchApi.createMatch(identity.scope, item.commandId, item.setup);
      if (run !== generation.current) return;
      setServer(data, identity); command.current = null; if (item.kind === 'create') createAttempt.current = null;
    } catch (reason) {
      if (run !== generation.current) return;
      setPendingMove(null);
      if (reason instanceof ApiError && reason.status === 401) {
        query.removeQueries({ queryKey: queryKeys.owner(identity) });
        current.current = null; setSnapshot(null); setUnauthorized(true); command.current = null;
      } else {
        try {
          const data = item.kind === 'create' ? null : await matchApi.getMatch(identity.scope, item.matchId);
          if (run !== generation.current) return;
          if (data) {
            setServer(data, identity);
            if (item.kind === 'move' && data.revision > item.body.expectedRevision || item.kind === 'next' && data && data.revision > item.body.expectedRevision) command.current = null;
          } else {
            if (item.kind !== 'create' || !item.setup) {
              const page = await matchApi.active(identity.scope);
              if (page.items.length) { const recovered = await matchApi.getMatch(identity.scope, page.items[0].matchId); if (run !== generation.current) return; setServer(recovered, identity); command.current = null; }
            }
          }
        } catch (recoveryError) {
          if (run !== generation.current) return;
          if (recoveryError instanceof ApiError && recoveryError.status === 401) { setSnapshot(null); setUnauthorized(true); command.current = null; }
        }
        if (reason instanceof ApiError && reason.status === 409) command.current = null;
      }
      setError(messageFor(reason));
    } finally { if (run === generation.current) { lock.current = false; setBusy(false); setCanRetry(command.current !== null); } }
  }, [identity, query, setServer]);
  const play = useCallback((row: number, column: number) => {
    const data = current.current;
    if (!identity || !data || lock.current || command.current || data.currentGame.turnStatus !== 'human' || data.currentGame.moves.some(move => move.row === row && move.column === column)) return;
    const body = { commandId: crypto.randomUUID(), row, column, expectedRevision: data.revision };
    command.current = { kind: 'move', matchId: data.matchId, body };
    setPendingMove({ step: data.currentGame.moves.length + 1, row, column, stone: data.humanStone });
    void execute(command.current);
  }, [identity, execute]);
  const nextGame = useCallback(() => {
    const data = current.current;
    if (!identity || !data || lock.current || command.current || data.currentGame.turnStatus !== 'finished') return;
    command.current = { kind: 'next', matchId: data.matchId, body: { commandId: crypto.randomUUID(), expectedRevision: data.revision } };
    void execute(command.current);
  }, [identity, execute]);
  const recover = useCallback(() => {
    const item = command.current;
    if (item?.kind === 'create' && item.setup) {
      void execute(item);
    } else if (item?.kind === 'create' && identity) {
      const run = generation.current;
      void matchApi.active(identity.scope).then(async page => {
        if (run !== generation.current) return;
        if (page.items.length) {
          const data = await matchApi.getMatch(identity.scope, page.items[0].matchId);
          if (run !== generation.current) return;
          setServer(data, identity); command.current = null; createAttempt.current = null; setError(null);
        } else void execute(item);
      }).catch(reason => { if (run === generation.current) setError(messageFor(reason)); });
    } else if (item) void execute(item);
    else if (current.current) void refresh().catch(reason => setError(messageFor(reason)));
    else setLoadAttempt(value => value + 1);
  }, [execute, identity, refresh, setServer]);
  useAiPolling(snapshot, refresh);
  return { snapshot, pendingMove, play, nextGame, recover, loading, busy, error, unauthorized, canRetry };
}
