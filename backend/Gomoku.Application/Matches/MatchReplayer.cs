using Gomoku.Core;

namespace Gomoku.Application.Matches;

public sealed class StateIntegrityException(string message) : Exception(message);

public sealed class UnsupportedRuleVersionException(string version)
    : Exception($"Unsupported rule version: {version}");

public static class MatchReplayer
{
    public static Match Replay(MatchRecord stored)
    {
        if (stored.RuleVersion != RuleSet.CurrentVersion)
        {
            throw new UnsupportedRuleVersionException(stored.RuleVersion);
        }
        if (!MatchBoardSizes.Supports(stored.BoardSize, stored.Columns) || stored.Games.Count == 0)
        {
            throw new StateIntegrityException("Unsupported board size or missing game.");
        }

        if (stored.HumanStone is not (Stone.X or Stone.O))
            throw new StateIntegrityException("Human stone is invalid.");
        var match = Match.Start(new RuleSet(stored.BoardSize, stored.Columns), stored.Games[0].OpeningStone);
        var commandCount = 0L;
        for (var gameIndex = 0; gameIndex < stored.Games.Count; gameIndex++)
        {
            var game = stored.Games[gameIndex];
            var before = match.Snapshot();
            if (game.Number != gameIndex + 1 || game.OpeningStone != before.OpeningStone)
            {
                throw new StateIntegrityException("Game number or opening stone does not match Core.");
            }

            for (var moveIndex = 0; moveIndex < game.Moves.Count; moveIndex++)
            {
                var move = game.Moves[moveIndex];
                var current = match.Snapshot().CurrentGame;
                var expectedActor = current.NextStone == stored.HumanStone ? PlayerActor.Human : PlayerActor.AI;
                if (move.Step != moveIndex + 1 || move.Stone != current.NextStone || move.Actor != expectedActor ||
                    match.TryPlay(new Position(move.Row, move.Column)) != MoveResult.Played)
                {
                    throw new StateIntegrityException("Stored move does not match Core replay.");
                }
                commandCount++;
            }

            var status = match.Snapshot().CurrentGame.Status;
            var expectedState = status == GameStatus.InProgress ? GameRecordState.Active : GameRecordState.Finished;
            if (game.RuleStatus != status || game.State != expectedState)
            {
                throw new StateIntegrityException("Stored game result does not match Core.");
            }

            if (gameIndex < stored.Games.Count - 1)
            {
                if (!match.StartNextGame())
                {
                    throw new StateIntegrityException("Next game exists before current game finished.");
                }
                commandCount++;
            }
        }

        var result = match.Snapshot();
        if (stored.CurrentGameId != stored.Games[^1].Id ||
            stored.XScore != result.XScore || stored.OScore != result.OScore ||
            stored.Revision != commandCount)
        {
            throw new StateIntegrityException("Stored Match score, current game, or revision does not match Core.");
        }

        return match;
    }
}
