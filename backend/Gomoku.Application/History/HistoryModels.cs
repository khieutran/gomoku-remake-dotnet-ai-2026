using System.Globalization;
using System.Text;

namespace Gomoku.Application.History;

public sealed record HistoryGameSummary(Guid GameId, Guid MatchId, int Number,
    string Status, string OpeningStone, string RuleVersion, int BoardSize, string? AiVersion,
    DateTimeOffset StartedAt, DateTimeOffset FinishedAt, int MoveCount, string? HumanStone = null,
    int? BoardColumns = null);

public sealed record HistoryMove(int Step, int Row, int Column, string Stone, string Actor,
    DateTimeOffset PlayedAt);

public sealed record HistoryGameDetail(Guid GameId, Guid MatchId, int Number,
    string Status, string OpeningStone, string RuleVersion, int BoardSize, string? AiVersion,
    DateTimeOffset StartedAt, DateTimeOffset FinishedAt, IReadOnlyList<HistoryMove> Moves,
    int? BoardColumns = null);

public sealed record HistoryPage(IReadOnlyList<HistoryGameSummary> Items, string? NextCursor);

public readonly record struct HistoryCursor(DateTimeOffset FinishedAt, Guid GameId)
{
    public string Encode()
    {
        var value = $"{FinishedAt.ToUniversalTime().Ticks.ToString(CultureInfo.InvariantCulture)}:{GameId:N}";
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(value))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    public static bool TryDecode(string? encoded, out HistoryCursor? cursor)
    {
        cursor = null;
        if (encoded is null) return true;
        if (encoded.Length is 0 or > 128) return false;

        try
        {
            var base64 = encoded.Replace('-', '+').Replace('_', '/');
            var value = Encoding.UTF8.GetString(Convert.FromBase64String(
                base64.PadRight((base64.Length + 3) / 4 * 4, '=')));
            var parts = value.Split(':', 2);
            if (parts.Length != 2 ||
                !long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks) ||
                !Guid.TryParseExact(parts[1], "N", out var gameId) || gameId == Guid.Empty)
                return false;
            cursor = new HistoryCursor(new DateTimeOffset(ticks, TimeSpan.Zero), gameId);
            return true;
        }
        catch (Exception exception) when (exception is FormatException or ArgumentOutOfRangeException)
        {
            return false;
        }
    }
}
