using Gomoku.AI;
using Gomoku.Application.Matches;
using Gomoku.Core;

namespace Gomoku.Api.Matches;

public sealed class AiTurnWorker(IServiceScopeFactory scopeFactory,
    ILogger<AiTurnWorker> logger) : BackgroundService
{
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan SearchBudget = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan HardTimeout = TimeSpan.FromSeconds(2);
    private static readonly SemaphoreSlim EngineSlot = new(1, 1);
    private static readonly IAiMoveSelector LegalFallback = new SeededLegalMoveSelector(2026);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            AiTurnClaim? claim;
            try
            {
                using var scope = scopeFactory.CreateScope();
                claim = await scope.ServiceProvider.GetRequiredService<IAiTurnJobStore>()
                    .ClaimNextAsync(LeaseDuration, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Could not claim an AI turn");
                try { await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken); }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
                continue;
            }

            if (claim is null)
            {
                try { await Task.Delay(TimeSpan.FromMilliseconds(250), stoppingToken); }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
                continue;
            }

            try
            {
                var result = await PlayClaimAsync(claim, stoppingToken);
                using var scope = scopeFactory.CreateScope();
                var jobs = scope.ServiceProvider.GetRequiredService<IAiTurnJobStore>();
                if (result == "ok")
                    await jobs.CompleteAsync(claim, stoppingToken);
                else
                    await jobs.RetryAsync(claim, result, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "AI turn {JobId} failed", claim.JobId);
                try
                {
                    using var scope = scopeFactory.CreateScope();
                    await scope.ServiceProvider.GetRequiredService<IAiTurnJobStore>()
                        .RetryAsync(claim, "worker_error", stoppingToken);
                }
                catch (Exception retryException)
                {
                    logger.LogError(retryException, "Could not release AI turn {JobId}", claim.JobId);
                }
            }
        }
    }

    private async Task<string> PlayClaimAsync(AiTurnClaim claim, CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var services = scope.ServiceProvider;
        var store = services.GetRequiredService<IGameFlowStore>();
        var commandId = $"ai:{claim.GameId:N}:{claim.ExpectedRevision}";
        var receipt = await store.FindReceiptAsync(claim.Owner, commandId, cancellationToken);
        if (receipt is not null && receipt.MatchId == claim.MatchId &&
            receipt.Revision == claim.ExpectedRevision + 1)
            return "ok";

        var record = await store.LoadAsync(claim.Owner, claim.MatchId, cancellationToken);
        if (record is null) return "match_missing";
        if (record.Revision > claim.ExpectedRevision &&
            record.Games.Any(game => game.Id == claim.GameId &&
                game.Moves.Any(move => move.Actor == PlayerActor.AI && move.Stone == Stone.O)))
            return "ok";
        if (record.Revision != claim.ExpectedRevision || record.CurrentGameId != claim.GameId)
            return "stale_ai_job";

        var game = MatchReplayer.Replay(record).Snapshot().CurrentGame;
        if (game.Status != GameStatus.InProgress || game.NextStone != Stone.O)
            return "wrong_ai_turn";
        var selected = await SelectWithFallbackAsync(game, record.Games[^1].AiVersion,
            services.GetRequiredService<IAiMoveSelector>(), cancellationToken);
        if (selected.Move is not { } move) return "ai_no_move";

        var played = await services.GetRequiredService<GameFlowService>().PlayAsync(
            claim.Owner, claim.MatchId, move.Row, move.Column,
            PlayerActor.AI, claim.ExpectedRevision, commandId, cancellationToken,
            selected.Metadata);
        return played.Code;
    }

    private async Task<(Position? Move, AiMoveMetadata Metadata)> SelectWithFallbackAsync(
        GameSnapshot game, string? pinnedVersion, IAiMoveSelector selector,
        CancellationToken cancellationToken)
    {
        Position? move = null;
        string? fallbackReason = null;
        if (pinnedVersion != AiVersions.LegalV1)
        {
            fallbackReason = "unsupported_ai_version";
        }
        else if (!await EngineSlot.WaitAsync(0, cancellationToken))
        {
            fallbackReason = "ai_engine_busy";
        }
        else
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var engineTask = Task.Run(() =>
            {
                try { return selector.SelectMove(game, Stone.O, SearchBudget, timeout.Token); }
                finally { EngineSlot.Release(); }
            });
            try
            {
                move = await engineTask.WaitAsync(HardTimeout, cancellationToken);
                if (move is null) fallbackReason = "ai_no_move";
                else if (!IsLegal(game.Board, move.Value)) fallbackReason = "ai_invalid_move";
            }
            catch (TimeoutException)
            {
                timeout.Cancel();
                fallbackReason = "ai_timeout";
                _ = engineTask.ContinueWith(task =>
                    logger.LogWarning(task.Exception, "Timed-out AI selector later failed"),
                    CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted,
                    TaskScheduler.Default);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                fallbackReason = "ai_timeout";
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "AI selector failed; using legal fallback");
                fallbackReason = "ai_error";
            }
        }

        if (fallbackReason is not null)
        {
            logger.LogWarning("AI fallback {Reason} for pinned version {AiVersion}",
                fallbackReason, pinnedVersion);
            move = LegalFallback.SelectMove(game, Stone.O, TimeSpan.Zero, cancellationToken);
        }
        return (move, new AiMoveMetadata(AiVersions.LegalV1, fallbackReason));
    }

    private static bool IsLegal(BoardSnapshot board, Position move) =>
        move.Row >= 0 && move.Row < board.Size && move.Column >= 0 &&
        move.Column < board.Size && board.Get(move) == Stone.Empty;
}
