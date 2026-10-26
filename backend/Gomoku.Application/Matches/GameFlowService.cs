using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Gomoku.Application.Identity;
using Gomoku.Core;

namespace Gomoku.Application.Matches;

public sealed class GameFlowService(IGameFlowStore store, IAiTurnJobStore? jobs = null)
{
    public async Task<FlowResult> CreateMatchAsync(OwnerIdentity owner, string commandId,
        CancellationToken cancellationToken = default)
    {
        if (!ValidCommand(owner, commandId)) return FlowResult.Error("validation_error");
        var hash = Hash($"create|{RuleSet.CurrentVersion}|20");
        var previous = await store.FindReceiptAsync(owner, commandId, cancellationToken);
        if (previous is not null) return ReplayReceipt(previous, hash);

        var matchId = Guid.NewGuid();
        var gameId = Guid.NewGuid();
        var core = Match.Start(new RuleSet(20));
        var game = new GameRecord(gameId, 1, Stone.X, GameStatus.InProgress,
            GameRecordState.Active, [], AiVersions.LegalV1);
        var record = new MatchRecord(matchId, owner, RuleSet.CurrentVersion, 20,
            0, 0, gameId, 0, [game]);
        var receipt = new CommandRecord(owner, commandId, matchId, hash, 201, ToBody(record, core), 0);
        var outcome = await store.CreateAsync(record, receipt, cancellationToken);
        return await ResolveCommitAsync(outcome, receipt, cancellationToken);
    }

    public async Task<FlowResult> GetMatchAsync(OwnerIdentity owner, Guid matchId,
        CancellationToken cancellationToken = default)
    {
        var stored = await store.LoadAsync(owner, matchId, cancellationToken);
        if (stored is null) return FlowResult.Error("not_found");
        try
        {
            var core = MatchReplayer.Replay(stored);
            var pending = core.Snapshot().CurrentGame.NextStone == Stone.O;
            var progress = pending && jobs is not null
                ? await jobs.ReadProgressAsync(stored.CurrentGameId, stored.Revision, cancellationToken)
                : null;
            return new FlowResult("ok", 200, ToBody(stored, core, progress),
                matchId, null, stored.Revision);
        }
        catch (UnsupportedRuleVersionException)
        {
            return FlowResult.Error("unsupported_rule_version");
        }
        catch (StateIntegrityException)
        {
            return FlowResult.Error("state_integrity_error");
        }
    }

    public async Task<FlowResult> PlayAsync(OwnerIdentity owner, Guid matchId, int row, int column,
        PlayerActor actor, long expectedRevision, string commandId,
        CancellationToken cancellationToken = default, AiMoveMetadata? aiMetadata = null)
    {
        if (!ValidCommand(owner, commandId)) return FlowResult.Error("validation_error");
        if (actor == PlayerActor.Human && aiMetadata is not null)
            return FlowResult.Error("validation_error");
        var hash = Hash($"move|{matchId:N}|{expectedRevision}|{row}|{column}|{actor}");
        var previous = await store.FindReceiptAsync(owner, commandId, cancellationToken);
        if (previous is not null) return ReplayReceipt(previous, hash);

        var stored = await store.LoadAsync(owner, matchId, cancellationToken);
        if (stored is null) return FlowResult.Error("not_found");
        Match core;
        try { core = MatchReplayer.Replay(stored); }
        catch (UnsupportedRuleVersionException) { return FlowResult.Error("unsupported_rule_version"); }
        catch (StateIntegrityException) { return FlowResult.Error("state_integrity_error"); }
        if (stored.Revision != expectedRevision)
            return await ResolveStaleAsync(owner, commandId, hash, cancellationToken);

        var before = core.Snapshot().CurrentGame;
        if (before.Status != GameStatus.InProgress) return FlowResult.Error("game_finished");
        if (before.NextStone == Stone.X && actor != PlayerActor.Human ||
            before.NextStone == Stone.O && actor != PlayerActor.AI)
        {
            return FlowResult.Error("wrong_turn");
        }

        var played = core.TryPlay(new Position(row, column));
        if (played == MoveResult.OutOfBounds) return FlowResult.Error("invalid_move");
        if (played == MoveResult.Occupied) return FlowResult.Error("occupied");
        if (played != MoveResult.Played) return FlowResult.Error("game_finished");

        var after = core.Snapshot();
        var move = after.CurrentGame.Moves[^1];
        var added = new MoveRecord(move.Number, row, column, move.Stone, actor,
            aiMetadata?.ActualVersion, aiMetadata?.FallbackReason);
        var oldGame = stored.Games[^1];
        var completed = after.CurrentGame.Status != GameStatus.InProgress;
        var game = oldGame with
        {
            Moves = [..oldGame.Moves, added],
            RuleStatus = after.CurrentGame.Status,
            State = completed ? GameRecordState.Finished : GameRecordState.Active
        };
        var updated = stored with
        {
            XScore = after.XScore, OScore = after.OScore, Revision = stored.Revision + 1,
            Games = [..stored.Games.Take(stored.Games.Count - 1), game]
        };
        var httpStatus = !completed && after.CurrentGame.NextStone == Stone.O ? 202 : 200;
        var receipt = new CommandRecord(owner, commandId, matchId, hash, httpStatus,
            ToBody(updated, core), updated.Revision);
        var outcome = await store.CommitAsync(updated, expectedRevision, added, null,
            completed, receipt, cancellationToken);
        return await ResolveCommitAsync(outcome, receipt, cancellationToken);
    }

    public async Task<FlowResult> StartNextGameAsync(OwnerIdentity owner, Guid matchId,
        long expectedRevision, string commandId, CancellationToken cancellationToken = default)
    {
        if (!ValidCommand(owner, commandId)) return FlowResult.Error("validation_error");
        var hash = Hash($"next|{matchId:N}|{expectedRevision}");
        var previous = await store.FindReceiptAsync(owner, commandId, cancellationToken);
        if (previous is not null) return ReplayReceipt(previous, hash);

        var stored = await store.LoadAsync(owner, matchId, cancellationToken);
        if (stored is null) return FlowResult.Error("not_found");
        Match core;
        try { core = MatchReplayer.Replay(stored); }
        catch (UnsupportedRuleVersionException) { return FlowResult.Error("unsupported_rule_version"); }
        catch (StateIntegrityException) { return FlowResult.Error("state_integrity_error"); }
        if (stored.Revision != expectedRevision)
            return await ResolveStaleAsync(owner, commandId, hash, cancellationToken);
        if (!core.StartNextGame()) return FlowResult.Error("game_in_progress");

        var gameId = Guid.NewGuid();
        var game = new GameRecord(gameId, stored.Games.Count + 1,
            core.Snapshot().OpeningStone, GameStatus.InProgress, GameRecordState.Active,
            [], AiVersions.LegalV1);
        var updated = stored with
        {
            CurrentGameId = gameId, Revision = stored.Revision + 1,
            Games = [..stored.Games, game]
        };
        var httpStatus = game.OpeningStone == Stone.O ? 202 : 200;
        var receipt = new CommandRecord(owner, commandId, matchId, hash, httpStatus,
            ToBody(updated, core), updated.Revision);
        var outcome = await store.CommitAsync(updated, expectedRevision, null, game,
            false, receipt, cancellationToken);
        return await ResolveCommitAsync(outcome, receipt, cancellationToken);
    }

    private async Task<FlowResult> ResolveCommitAsync(CommitOutcome outcome, CommandRecord receipt,
        CancellationToken cancellationToken)
    {
        if (outcome == CommitOutcome.Committed) return FlowResult.Success(receipt);
        var previous = await store.FindReceiptAsync(receipt.Owner, receipt.CommandId, cancellationToken);
        if (previous is not null) return ReplayReceipt(previous, receipt.RequestHash);
        return FlowResult.Error(outcome == CommitOutcome.StaleRevision ? "stale_revision" : "idempotency_conflict");
    }

    private async Task<FlowResult> ResolveStaleAsync(OwnerIdentity owner, string commandId, string hash,
        CancellationToken cancellationToken)
    {
        var previous = await store.FindReceiptAsync(owner, commandId, cancellationToken);
        return previous is null ? FlowResult.Error("stale_revision") : ReplayReceipt(previous, hash);
    }

    private static FlowResult ReplayReceipt(CommandRecord previous, string hash) =>
        previous.RequestHash == hash ? FlowResult.Success(previous) : FlowResult.Error("idempotency_conflict");

    private static bool ValidCommand(OwnerIdentity owner, string commandId) =>
        owner.Id != Guid.Empty && !string.IsNullOrWhiteSpace(commandId) && commandId.Length <= 128;

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static string ToBody(MatchRecord record, Match core, AiTurnProgress? progress = null)
    {
        var snapshot = core.Snapshot();
        var current = record.Games[^1];
        return JsonSerializer.Serialize(new
        {
            matchId = record.Id,
            ruleVersion = record.RuleVersion,
            boardSize = record.BoardSize,
            xScore = snapshot.XScore,
            oScore = snapshot.OScore,
            revision = record.Revision,
            currentGame = new
            {
                id = current.Id,
                number = current.Number,
                openingStone = snapshot.OpeningStone.ToString(),
                aiVersion = current.AiVersion,
                status = snapshot.CurrentGame.Status.ToString(),
                nextStone = snapshot.CurrentGame.NextStone?.ToString(),
                turnStatus = snapshot.CurrentGame.Status != GameStatus.InProgress ? "finished"
                    : snapshot.CurrentGame.NextStone != Stone.O ? "human"
                    : progress is { Attempts: >= 3, LastErrorCode: not null }
                        ? "ai_retrying" : "ai_pending",
                aiErrorCode = progress is { Attempts: >= 3 } ? progress.LastErrorCode : null,
                aiRetryAt = progress is { Attempts: >= 3 }
                    ? (DateTimeOffset?)progress.NextAttemptAt : null,
                moves = snapshot.CurrentGame.Moves.Select(move => new
                {
                    step = move.Number,
                    row = move.Position.Row,
                    column = move.Position.Column,
                    stone = move.Stone.ToString()
                }).ToArray()
            }
        });
    }
}
