using Microsoft.AspNetCore.Identity;

namespace Gomoku.Infrastructure.Persistence;

public sealed class ApplicationUser : IdentityUser<Guid>
{
    // Shown on the leaderboard and home screen; null for accounts created before display names existed.
    public string? DisplayName { get; set; }
    public long HistoryRevision { get; set; }
    public int RankingPoints { get; set; }
    public int RankingGames { get; set; }
}
