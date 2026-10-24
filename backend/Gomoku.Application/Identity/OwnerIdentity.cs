namespace Gomoku.Application.Identity;

public enum OwnerScope
{
    Account,
    Guest
}

public readonly record struct OwnerIdentity(OwnerScope Scope, Guid Id);
