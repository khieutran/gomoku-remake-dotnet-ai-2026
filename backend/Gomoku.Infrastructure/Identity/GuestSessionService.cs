using System.Security.Cryptography;
using System.Text;
using Gomoku.Application.Identity;
using Gomoku.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Gomoku.Infrastructure.Identity;

public sealed class GuestSessionService(GomokuDbContext db, TimeProvider clock) : IGuestSessionService
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromDays(30);

    public async Task<CreatedGuestSession> CreateAsync(CancellationToken cancellationToken = default)
    {
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var expiresAt = clock.GetUtcNow() + Lifetime;
        var session = new GuestSession
        {
            Id = Guid.NewGuid(),
            TokenHash = SHA256.HashData(Encoding.UTF8.GetBytes(token)),
            CreatedAt = clock.GetUtcNow(),
            ExpiresAt = expiresAt
        };
        db.GuestSessions.Add(session);
        await db.SaveChangesAsync(cancellationToken);
        return new CreatedGuestSession(session.Id, token, expiresAt);
    }

    public async Task<Guid?> ResolveAsync(string? token, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 128)
        {
            return null;
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        var now = clock.GetUtcNow();
        return await db.GuestSessions
            .Where(x => x.TokenHash.SequenceEqual(hash) && x.ExpiresAt > now)
            .Select(x => (Guid?)x.Id)
            .SingleOrDefaultAsync(cancellationToken);
    }
}
