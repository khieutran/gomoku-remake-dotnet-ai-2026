namespace Gomoku.Application.Identity;

public static class OwnerSelector
{
    public static OwnerIdentity? Select(Guid? accountId, Guid? guestId, string? scope)
    {
        if (scope is not null && scope is not ("account" or "guest"))
        {
            throw new ArgumentException("Scope must be account or guest.", nameof(scope));
        }

        if (scope == "account" || scope is null && accountId.HasValue)
        {
            return accountId is { } id ? new OwnerIdentity(OwnerScope.Account, id) : null;
        }

        return guestId is { } guest ? new OwnerIdentity(OwnerScope.Guest, guest) : null;
    }
}
