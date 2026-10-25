using Gomoku.Application.Identity;
using System.Globalization;
using System.Text.Json.Serialization;

namespace Gomoku.Application.Matches;

public sealed record ActiveMatchSummary(Guid MatchId, Guid GameId, long Revision,
    DateTimeOffset UpdatedAt, [property: JsonIgnore] DateTimeOffset CreatedAt);
public sealed record ActiveMatchPage(IReadOnlyList<ActiveMatchSummary> Items, string? NextCursor);

public readonly record struct ActiveMatchCursor(OwnerScope Scope, Guid OwnerId,
    DateTimeOffset CreatedAt, Guid MatchId)
{
    public string Encode() => string.Join(':', "v1", ((int)Scope).ToString(CultureInfo.InvariantCulture),
        OwnerId.ToString("N"), CreatedAt.ToUniversalTime().Ticks.ToString(CultureInfo.InvariantCulture),
        MatchId.ToString("N"));

    public static bool TryDecode(string value, out ActiveMatchCursor cursor)
    {
        cursor = default;
        var parts = value.Split(':');
        if (parts.Length != 5 || parts[0] != "v1" ||
            !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var scope) ||
            scope is not (0 or 1) ||
            !Guid.TryParseExact(parts[2], "N", out var ownerId) || ownerId == Guid.Empty ||
            !long.TryParse(parts[3], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks) ||
            !Guid.TryParseExact(parts[4], "N", out var matchId) || matchId == Guid.Empty)
            return false;
        try
        {
            cursor = new ActiveMatchCursor((OwnerScope)scope, ownerId,
                new DateTimeOffset(ticks, TimeSpan.Zero), matchId);
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
    }
}

public interface IActiveMatchReader
{
    Task<IReadOnlyList<ActiveMatchSummary>> ReadAsync(OwnerIdentity owner, ActiveMatchCursor? cursor, int take,
        CancellationToken cancellationToken = default);
}
