namespace Gomoku.Core;

public sealed class Match
{
    private readonly RuleSet _rules;
    private readonly List<GameSnapshot> _completedGames = [];
    private Game _game;
    private Stone _openingStone = Stone.X;
    private int _xScore;
    private int _oScore;

    private Match(RuleSet rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        _rules = rules;
        _game = Game.Start(rules, _openingStone);
    }

    public static Match Start(RuleSet rules) => new(rules);

    public MoveResult TryPlay(Position position)
    {
        var result = _game.TryPlay(position);
        if (result != MoveResult.Played)
        {
            return result;
        }

        switch (_game.Status)
        {
            case GameStatus.XWon:
                _xScore += 3;
                break;
            case GameStatus.OWon:
                _oScore += 3;
                break;
            case GameStatus.Draw:
                _xScore++;
                _oScore++;
                break;
        }

        return MoveResult.Played;
    }

    public bool StartNextGame()
    {
        if (_game.Status == GameStatus.InProgress)
        {
            return false;
        }

        _completedGames.Add(_game.Snapshot());
        _openingStone = _openingStone == Stone.X ? Stone.O : Stone.X;
        _game = Game.Start(_rules, _openingStone);
        return true;
    }

    public MatchSnapshot Snapshot() => new(
        _xScore,
        _oScore,
        _openingStone,
        _game.Snapshot(),
        Array.AsReadOnly(_completedGames.ToArray()));
}
