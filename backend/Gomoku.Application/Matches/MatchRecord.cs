using Gomoku.Application.Identity;
using Gomoku.Core;

namespace Gomoku.Application.Matches;

public enum PlayerActor
{
    Human,
    AI
}

public enum GameRecordState
{
    Active,
    Finished,
    Abandoned
}

public sealed record MoveRecord(int Step, int Row, int Column, Stone Stone, PlayerActor Actor,
    string? ActualAiVersion = null, string? AiFallbackReason = null);

public sealed record GameRecord(Guid Id, int Number, Stone OpeningStone, GameStatus RuleStatus,
    GameRecordState State, IReadOnlyList<MoveRecord> Moves, string? AiVersion = null);

public sealed record MatchRecord(Guid Id, OwnerIdentity Owner, string RuleVersion, int BoardSize,
    int XScore, int OScore, Guid CurrentGameId, long Revision, IReadOnlyList<GameRecord> Games);
