namespace Gomoku.Application.Matches;

/// <summary>Reads the champion only when a new Game is created.</summary>
public interface IAiVersionProvider
{
    string CurrentVersion { get; }
}
