namespace Gomoku.Application.Identity;

public readonly record struct CreatedGuestSession(Guid Id, string Token, DateTimeOffset ExpiresAt);

public interface IGuestSessionService
{
    Task<CreatedGuestSession> CreateAsync(CancellationToken cancellationToken = default);
    Task<Guid?> ResolveAsync(string? token, CancellationToken cancellationToken = default);
}
