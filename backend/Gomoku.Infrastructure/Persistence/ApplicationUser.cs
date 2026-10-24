using Microsoft.AspNetCore.Identity;

namespace Gomoku.Infrastructure.Persistence;

public sealed class ApplicationUser : IdentityUser<Guid>
{
    public long HistoryRevision { get; set; }
}
