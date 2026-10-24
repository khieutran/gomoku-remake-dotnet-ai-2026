namespace Gomoku.Core;

public sealed class Game
{
    private readonly Board _board;
    private readonly RuleSet _rules;
    private readonly List<Move> _moves = [];
    private Stone _nextStone;
    private GameStatus _status = GameStatus.InProgress;

    private Game(RuleSet rules, Stone openingStone)
    {
        ArgumentNullException.ThrowIfNull(rules);
        if (openingStone is not (Stone.X or Stone.O))
        {
            throw new ArgumentOutOfRangeException(nameof(openingStone));
        }

        _rules = rules;
        _board = new Board(rules.BoardSize);
        _nextStone = openingStone;
    }

    public static Game Start(RuleSet rules, Stone openingStone) => new(rules, openingStone);

    internal GameStatus Status => _status;

    public MoveResult TryPlay(Position position)
    {
        if (_status != GameStatus.InProgress)
        {
            return MoveResult.Finished;
        }

        if (!_board.Contains(position))
        {
            return MoveResult.OutOfBounds;
        }

        if (!_board.TryPlace(position, _nextStone))
        {
            return MoveResult.Occupied;
        }

        var playedStone = _nextStone;
        _moves.Add(new Move(_moves.Count + 1, position, playedStone));
        _status = RuleEvaluator.Evaluate(_board.Snapshot(), position, playedStone, _rules);
        if (_status == GameStatus.InProgress)
        {
            _nextStone = playedStone == Stone.X ? Stone.O : Stone.X;
        }

        return MoveResult.Played;
    }

    public GameSnapshot Snapshot() => new(
        _board.Snapshot(),
        _status,
        _status == GameStatus.InProgress ? _nextStone : null,
        Array.AsReadOnly(_moves.ToArray()));
}
